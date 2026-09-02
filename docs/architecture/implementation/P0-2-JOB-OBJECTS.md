# P0-2: Windows Job Object Lifecycle

## Problem

P0-1 provided process ownership tracking (which processes belong to which seat), but did not guarantee cleanup. If `Process.Kill()` failed or the service crashed between process launch and explicit kill, orphaned processes could survive seat teardown.

Windows Job Objects solve this: when a Job Object handle is closed, Windows terminates ALL processes assigned to it (with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`).

---

## Current Process Lifecycle (before P0-2)

```
SeatManager.ProvisionSeatAsync
  → VibepolloManager.StartAsync
    → ProcessInjector.LaunchVibepolloInSessionAsync (returns PID)
    → IProcessTracker.Register(identity, seatId, Provider)
  → [process runs]

SeatManager.TeardownSeatInternalAsync
  → VibepolloManager.Stop(seat)
    → Process.GetProcessById(pid).Kill(entireProcessTree: true)
    → IProcessTracker.Unregister(identity)
```

**Gap:** If `Kill()` fails (access denied, handle stale), the process survives.

---

## Job Object Design

### Architecture

```
Seat
 ├── IProcessTracker        (ownership: which PIDs belong to this seat)
 └── IProcessGroup           (cleanup guarantee: kill all on dispose)
       └── WindowsProcessGroup
             └── SafeJobHandle (Win32 Job Object with KILL_ON_JOB_CLOSE)
```

**ProcessTracker** and **ProcessGroup** are separate concerns:
- Tracker: "who owns this process?" (query, identity)
- Group: "terminate all processes in this group" (cleanup guarantee)

### Lifecycle

```
SeatManager.ProvisionSeatAsync
  → IProcessGroupManager.GetOrCreateForSeat(seatId)  ← creates Job Object
  → VibepolloManager.StartAsync
    → ProcessInjector.LaunchVibepolloInSessionAsync
    → IProcessTracker.Register(identity, seatId, Provider)
    → IProcessGroup.AssignProcess(pid)                ← assigns to Job
  → [process runs]

SeatManager.TeardownSeatInternalAsync
  → [all subsystem cleanup]
  → IProcessGroupManager.DisposeForSeat(seatId)       ← KILL_ON_JOB_CLOSE fires
```

---

## Win32 Implementation

### SafeJobHandle

Extends `SafeHandleZeroOrMinusOneIsInvalid` with `ReleaseHandle()` calling `CloseHandle`.

```csharp
public sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected override bool ReleaseHandle() => Kernel32.CloseHandle(handle);
}
```

### WindowsProcessGroup

Creates a Job Object and configures `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`:

```csharp
public WindowsProcessGroup()
{
    var handle = Kernel32.CreateJobObjectW(IntPtr.Zero, null);
    _jobHandle = new SafeJobHandle(handle);
    ConfigureKillOnClose();
}

private void ConfigureKillOnClose()
{
    var info = new JobObjectExtendedLimitInformation
    {
        BasicLimitInformation = new JobObjectBasicLimitInformation
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        }
    };
    Kernel32.SetInformationJobObject(_jobHandle, ...);
}
```

### Process Assignment

```csharp
public void AssignProcess(int processId)
{
    var processHandle = Kernel32.OpenProcess(
        JOB_OBJECT_ASSIGN_PROCESS | PROCESS_TERMINATE, false, (uint)processId);
    Kernel32.AssignProcessToJobObject(_jobHandle, processHandle);
    Kernel32.CloseHandle(processHandle);
}
```

**Edge cases handled:**
- Process already exited (PID invalid): `OpenProcess` returns null → no-op
- Process already in another job: `AssignProcessToJobObject` returns `ERROR_ACCESS_DENIED` → no-op
- Process ID ≤ 0: throws `ArgumentOutOfRangeException`

---

## Kill-on-Close

The core invariant:

```
Job Object handle closed (Dispose)
        ↓
Windows terminates ALL associated processes
```

This is the **safety net**, not the primary kill mechanism. `VibepolloManager.Stop()` still does explicit `Process.Kill(entireProcessTree: true)`. The Job Object is the fallback.

**Service crash behavior:** When the service process crashes, Windows closes all handles including Job Object handles. `KILL_ON_JOB_CLOSE` triggers, terminating seat processes. This is automatic — no recovery code needed.

---

## Process Assignment

### When assigned

1. After `ProcessInjector.LaunchVibepolloInSessionAsync` returns a valid PID
2. After `IProcessTracker.Register` succeeds
3. Best-effort: if assignment fails, logging continues, streaming is not blocked

### AssignProcessToJobObject limitations

- Process must not be in another job (unless nested jobs allowed)
- Process must be alive (or `OpenProcess` fails gracefully)
- Process must have accessible handle (SERVICE-level access required)

In unit test context, processes may already be in the test runner's job. Assignment fails silently — this is expected behavior.

---

## Cleanup Order

```
SeatManager.TeardownSeatInternalAsync
  │
  ├── _onConnectApps.Forget(seat.Id)
  ├── _inputHookManager.Uninstall()
  ├── _hidHide.UncloakForSession(seat)
  ├── UnassignControllersForSeat(seat.Id)
  ├── _controllerManager.DestroyController(seat)
  ├── _vibepolloManager.Stop(seat)         ← explicit Kill
  ├── _firewall.ClosePortsAsync(seat)
  ├── _displayManager.DestroyDisplayAsync(seat)
  ├── _sessionLauncher.DisconnectSession()
  ├── _sessionLauncher.LogoffSession()
  ├── _portAllocator.Release()
  ├── _configBuilder.CleanupConfig()
  │
  └── _processGroupManager.DisposeForSeat() ← KILL_ON_JOB_CLOSE safety net
```

The Job Object is disposed **last** — it's the final safety net after all explicit cleanup.

---

## Service Shutdown

### Normal shutdown

`SeatManager.TeardownAllAsync()` → each seat's teardown → `DisposeForSeat()` → Job Object closed → processes terminated.

### Unexpected crash

Windows closes all handles in the crashed process. Job Object handles close. `KILL_ON_JOB_CLOSE` fires. Seat processes are terminated.

**No orphan adoption needed** — Job Objects guarantee cleanup.

---

## Tests

21 new tests in `ProcessGroupTests.cs`:

### WindowsProcessGroupTests (10 tests)
- Constructor creates job object
- AssignProcess with current process
- AssignProcess with dead process (no-op)
- AssignProcess with zero/negative PID (throws)
- Dispose is idempotent
- AssignProcess after dispose (throws)
- KillOnClose terminates assigned process (best-effort in test context)
- Two independent seats (jobs don't cross)
- Multiple assignments (idempotent)

### WindowsProcessGroupManagerTests (11 tests)
- GetOrCreateForSeat creates new group
- GetOrCreateForSeat returns same group
- GetForSeat returns null when not exist
- GetForSeat returns group after create
- DisposeForSeat removes group
- DisposeForSeat non-existent is no-op
- DisposeForSeat doesn't affect other seats
- Dispose disposes all groups
- Dispose is idempotent
- GetOrCreateForSeat after dispose throws
- Concurrent GetOrCreate (50 threads)

---

## Known Windows Limitations

1. **Nested jobs:** If a process is already in a job without `JOB_OBJECT_LIMIT_BREAKAWAY_OK`, `AssignProcessToJobObject` fails with `ERROR_ACCESS_DENIED`. This is handled gracefully (no-op).

2. **Protected processes:** Some system processes cannot be assigned to jobs. Not relevant for MultiSeat (provider processes are user-mode).

3. **Process tree:** `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` kills only processes directly assigned to the job, not their children (unless children are also assigned). `VibepolloManager.Stop()` uses `entireProcessTree: true` for the primary kill; the Job Object catches stragglers.

---

## Files Changed

| File | Change |
|------|--------|
| `src/MultiSeat.Service/Interop/Kernel32.cs` | **MODIFIED** — +Job Object P/Invoke |
| `src/MultiSeat.Service/Interop/SafeJobHandle.cs` | **NEW** — SafeHandle wrapper |
| `src/MultiSeat.Shared/IProcessGroup.cs` | **NEW** — Interface |
| `src/MultiSeat.Shared/IProcessGroupManager.cs` | **NEW** — Interface |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessGroup.cs` | **NEW** — Implementation |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessGroupManager.cs` | **NEW** — Manager |
| `src/MultiSeat.Service/Streaming/VibepolloManager.cs` | **MODIFIED** — AssignProcess after Start |
| `src/MultiSeat.Service/Sessions/SeatManager.cs` | **MODIFIED** — Create early, dispose last |
| `src/MultiSeat.Service/Program.cs` | **MODIFIED** — DI registration |
| `src/MultiSeat.Tests/ProcessTracking/ProcessGroupTests.cs` | **NEW** — 21 tests |
| `docs/architecture/implementation/P0-2-JOB-OBJECTS.md` | **NEW** — This document |

---

## Architectural Invariants Verified

| Invariant | Status |
|-----------|--------|
| INV-JOB-1: Each Seat has at most one Job Object | ✅ Enforced by manager |
| INV-JOB-2: Job Object belongs to one Seat | ✅ Keyed by SeatId |
| INV-JOB-3: Process of Seat A not in Job of Seat B | ✅ Per-seat assignment |
| INV-JOB-4: Closing Job terminates processes | ✅ KILL_ON_JOB_CLOSE |
| INV-JOB-5: Job Object not in Domain/Core | ✅ In Service layer |
| INV-JOB-6: Not Vibepollo-specific | ✅ Generic PID assignment |
| INV-JOB-7: Tracker and Group separate | ✅ Independent abstractions |
| INV-JOB-8: No raw handles leak | ✅ SafeJobHandle |

---

## Next Step

**P0-3 — Process Cleanup / Orphan & Lifecycle Validation**

Will add:
- Startup orphan scan (find existing provider processes)
- Process exit event monitoring
- Health check integration with Job Object status
