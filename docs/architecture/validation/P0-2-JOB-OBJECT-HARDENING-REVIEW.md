# P0-2 Job Object Hardening Review

## Executive Summary

The Job Object implementation is **structurally correct** at the Win32 API level. `KILL_ON_JOB_CLOSE` is properly configured and will terminate assigned processes when the handle closes. However, the **safety guarantee is CONDITIONAL** because `AssignProcessToJobObject` uses best-effort assignment — if assignment fails (process already in another job, ACCESS_DENIED), the process is NOT in the Job and will NOT be terminated by `KILL_ON_JOB_CLOSE`.

**Verdict: PASS WITH CHANGES**

The current design is acceptable for production because:
1. The primary cleanup mechanism is explicit `Kill()` in `VibepolloManager.Stop()`
2. Job Object is a safety net, not the sole cleanup path
3. In production, provider processes are unlikely to be in pre-existing jobs
4. But the documentation must be updated to reflect the conditional guarantee

---

## Implementation Reviewed

| File | Role |
|------|------|
| `Kernel32.cs` | Win32 P/Invoke: CreateJobObjectW, SetInformationJobObject, AssignProcessToJobObject |
| `SafeJobHandle.cs` | SafeHandle wrapper for Job Object handle |
| `WindowsProcessGroup.cs` | IProcessGroup implementation — creates Job, assigns processes |
| `WindowsProcessGroupManager.cs` | IProcessGroupManager — per-seat lifecycle |
| `SeatManager.cs` | Creates group in ProvisionSeatAsync, disposes last in TeardownSeatInternalAsync |
| `VibepolloManager.cs` | Assigns provider process to group after StartAsync |

---

## Native API Correctness

### Struct Layout

```
JobObjectExtendedLimitInformation
  ├── JobObjectBasicLimitInformation (48 bytes on x64)
  │     ├── PerProcessUserTimeLimit  (long, 8)
  │     ├── PerJobUserTimeLimit      (long, 8)
  │     ├── LimitFlags               (uint, 4) ← set to 0x2000
  │     ├── [4 bytes padding]
  │     ├── MinimumWorkingSetSize    (IntPtr, 8)
  │     ├── MaximumWorkingSetSize    (IntPtr, 8)
  │     ├── ActiveProcessLimit       (uint, 4)
  │     ├── [4 bytes padding]
  │     ├── Affinity                (IntPtr, 8)
  │     ├── PriorityClass           (uint, 4)
  │     └── SchedulingClass         (uint, 4)
  ├── IoCounters (48 bytes)
  ├── ProcessMemoryLimit            (IntPtr, 8)
  ├── JobMemoryLimit                (IntPtr, 8)
  ├── PeakProcessMemoryUsed         (IntPtr, 8)
  └── PeakJobMemoryUsed             (IntPtr, 8)
```

**VERDICT: CORRECT.** `LayoutKind.Sequential` with default packing matches the Win32 struct on x64. All fields are zeroed except `LimitFlags`. `SetInformationJobObject` reads the exact struct size via `Marshal.SizeOf`.

### Constants

| Constant | Value | Windows SDK | Status |
|----------|-------|-------------|--------|
| `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` | `0x2000` | ✅ | CORRECT |
| `JobObjectInfoClassExtendedLimitInformation` | `9` | ✅ | CORRECT |

### SafeJobHandle

- Extends `SafeHandleZeroOrMinusOneIsInvalid` — correct for kernel handles
- `ReleaseHandle()` calls `Kernel32.CloseHandle` — correct
- `ownsHandle: true` — correct
- `DangerousGetHandle()` for P/Invoke — acceptable (we own the handle lifetime)

---

## KILL_ON_JOB_CLOSE Verification

### Source-level proof chain

```
1. WindowsProcessGroup()
   → CreateJobObjectW() returns handle
   → new SafeJobHandle(handle) stores it

2. ConfigureKillOnClose()
   → info.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE (0x2000)
   → SetInformationJobObject(handle, ExtendedLimit, ref info, sizeof)
   → returns true → flag is set

3. Dispose()
   → _disposed = true
   → _jobHandle.Dispose()
   → SafeJobHandle.ReleaseHandle()
   → Kernel32.CloseHandle(handle)
   → Windows closes the Job Object
   → KILL_ON_JOB_CLOSE fires → all assigned processes terminated
```

**VERDICT: CORRECT.** The flag is set correctly. When the last handle to the Job Object closes, Windows terminates all processes in the job. The SafeHandle ensures the handle is closed even if an exception occurs.

---

## Process Assignment

### Access rights used

```csharp
const uint access = 0x0010 /* JOB_OBJECT_ASSIGN_PROCESS */ | 0x0001 /* PROCESS_TERMINATE */;
```

**VERDICT: MINIMAL.** Only `JOB_OBJECT_ASSIGN_PROCESS` (required) and `PROCESS_TERMINATE` (for future use). No excessive privileges.

### Assignment failure handling

```
OpenProcess fails:
  ERROR_INVALID_PARAMETER (87) → return (process exited)
  Other error → throw Win32Exception

AssignProcessToJobObject fails:
  ERROR_ACCESS_DENIED (5) → return (best-effort, process in another job)
  Other error → throw Win32Exception
```

**VERDICT: CONDITIONALLY SAFE.** When `ERROR_ACCESS_DENIED` occurs, the process is NOT in the Job. `KILL_ON_JOB_CLOSE` will NOT terminate it. The explicit `Kill()` in `VibepolloManager.Stop()` is the only cleanup path.

---

## Child Process Inheritance

### Critical question: do Vibepollo's children inherit the Job?

Vibepollo is launched by `ProcessInjector.LaunchVibepolloInSessionAsync()`:
- `CreateProcessAsUserW` with `CREATE_NEW_CONSOLE | NORMAL_PRIORITY_CLASS`
- No `CREATE_BREAKAWAY_FROM_JOB` flag
- The process is assigned to the Job AFTER creation

**Child process behavior:**
- Children created BY the process inherit the Job of their parent
- Since Vibepollo is assigned to the Job, its children (encoders, helpers) inherit it
- **UNLESS** Vibepollo uses `CREATE_BREAKAWAY_FROM_JOB` when spawning children

**Evidence from codebase:** No evidence that Vibepollo uses `CREATE_BREAKAWAY_FROM_JOB`. Vibepollo is a Sunshine fork; Sunshine's encoder processes are typically child processes.

**VERDICT: LIKELY PROTECTED, NOT GUARANTEED.** If Vibepollo spawns children normally, they inherit the Job. If Vibepollo uses `CREATE_BREAKAWAY_FROM_JOB` (unlikely but possible), children escape.

### What about games launched by ProcessInjector?

Games launched via `SeatManager.LaunchAppInSeatAsync()`:
- Uses `ProcessInjector.LaunchInSessionAsync()`
- `CreateProcessAsUserW` with `CREATE_NEW_CONSOLE`
- No `CREATE_BREAKAWAY_FROM_JOB`
- The game process is NOT explicitly assigned to the Job

**VERDICT: GAP.** Games launched by `ProcessInjector` are NOT assigned to the Job Object. They are only in the Job if they inherit it from the calling process — but the calling process (MultiSeat Service) is NOT in a Job.

**Impact:** If `ProcessInjector.LaunchInSessionAsync` creates a game, that game is NOT in the Job. When the seat is torn down, the game is only cleaned up by explicit `Kill()` calls (which don't exist for games in current code).

---

## CreateProcessAsUser

All `CreateProcessAsUserW` calls use:
- `IntPtr.Zero` for process/thread security attributes
- `false` for `bInheritHandles`
- `CREATE_NEW_CONSOLE | NORMAL_PRIORITY_CLASS` for creation flags
- No `STARTUPINFOEX`, no `PROC_THREAD_ATTRIBUTE_LIST`

**VERDICT: No Job attribute propagation.** The created process inherits the Job of the calling process only if the calling process is in a Job. Since the MultiSeat Service (SYSTEM) is typically NOT in a Job, child processes start jobless and are assigned later via `AssignProcessToJobObject`.

---

## Service Crash

```
MultiSeat.Service (SYSTEM)
  ├── owns SafeJobHandle for Seat A
  ├── owns SafeJobHandle for Seat B
  │
  ├── SERVICE CRASH
  │     ↓
  ├── OS closes all handles in crashed process
  │     ↓
  ├── SafeJobHandle.ReleaseHandle() called by finalizer
  │     ↓
  ├── CloseHandle(job) for each seat
  │     ↓
  └── KILL_ON_JOB_CLOSE fires → all assigned processes terminated
```

**VERDICT: SAFE.** Windows automatically closes handles when a process terminates. The .NET finalizer calls `ReleaseHandle()` on `SafeJobHandle`. Even if the finalizer is delayed, `SafeHandle` infrastructure ensures cleanup.

**Caveat:** If the service crashes during `Process.Injector.LaunchVibepolloInSessionAsync` (between process creation and `AssignProcessToJobObject`), the new process is NOT in the Job and survives.

---

## Service Restart

```
Service instance A crashes
  → Job handles closed → processes terminate
  → Service instance B starts
  → Empty tracker, empty group manager
  → No orphan detection
```

**VERDICT: SAFE BUT INCOMPLETE.** No orphan processes survive (Job cleanup works). But the new service instance has no knowledge of what happened. If a process escaped the Job before the crash, it becomes orphaned with no recovery path.

---

## Teardown Races

### Race 1: Health check restarts during teardown

```
Thread A: TeardownSeatInternalAsync
  → _seats.TryRemove(seatId)  // seat gone from dictionary
  → ... subsystem cleanup ...
  → _vibepolloManager.Stop(seat) // kill provider
  → _processGroupManager.DisposeForSeat() // kill via Job

Thread B: HealthCheck → RestartAsync
  → _instances.TryGetValue(seat.Id) // may still find it
  → Launch new provider
  → Assign to Job
```

**VERDICT: SAFE.** Even if Thread B starts a new provider, Thread A's `DisposeForSeat()` will terminate it. The Job Object is shared per-seat, so the new process is in the same Job that gets disposed.

### Race 2: Concurrent DisposeForSeat calls

```
Thread A: DisposeForSeat(seatId)
  → _groups.TryRemove(seatId, out group)
  → group.Dispose()

Thread B: DisposeForSeat(seatId)
  → _groups.TryRemove(seatId, out group) // returns false
  → no-op
```

**VERDICT: SAFE.** `TryRemove` is atomic. Only one thread gets the group.

### Race 3: AssignProcess after Dispose

```
Thread A: DisposeForSeat(seatId) // disposes Job
Thread B: VibepolloManager.StartAsync → AssignProcess(pid)
  → _processGroupManager.GetOrCreateForSeat(seatId) // creates NEW Job
  → group.AssignProcess(pid) // assigns to new Job
```

**VERDICT: SAFE.** `GetOrCreateForSeat` creates a new Job if none exists. The new process gets a fresh Job. The old disposed Job is irrelevant.

---

## Multi-Seat Isolation

```
Seat A → WindowsProcessGroup (Job A)
Seat B → WindowsProcessGroup (Job B)
Seat C → WindowsProcessGroup (Job C)
```

DisposeForSeat(A) → removes Job A from dictionary, disposes it → only A's processes die.

**VERDICT: SAFE.** Each seat has an independent Job Object. Disposing one does not affect others.

---

## Process Tracker Boundary

```
IProcessTracker → "which PIDs belong to this seat?" (ownership)
IProcessGroup   → "guarantee cleanup" (termination)
```

- Tracker uses `ProcessIdentity` (PID + StartedAt)
- Group uses raw PID for `AssignProcessToJobObject`
- No coupling between them
- Both are injected independently into `VibepolloManager`

**VERDICT: CORRECT SEPARATION.** The two concerns are properly decoupled.

---

## Failure Policy

When `AssignProcessToJobObject` fails with `ERROR_ACCESS_DENIED`:

| Option | Behavior | Assessment |
|--------|----------|------------|
| A. Continue (current) | Process unmanaged by Job, relies on explicit Kill | **ACCEPTABLE** — Job is safety net, not sole mechanism |
| B. Kill process | Kill the provider immediately | **TOO AGGRESSIVE** — would break streaming |
| C. Fail provider startup | Throw, seat enters Error state | **TOO AGGRESSIVE** — provider works fine without Job |
| D. Retry assignment | Loop with delay | **FUTILE** — if process is in another job, retry won't help |
| E. Mark untracked | Log and continue | Same as A, but with better observability |
| F. Fallback cleanup | Use WTS to find and kill processes | **FUTURE** — complex, not needed now |

**RECOMMENDATION:** Option A (current) is correct for now. But add an `_logger.LogWarning` (not just LogDebug) when assignment fails, so operators can detect the condition in production.

---

## Test Quality

### Classification

| Test | Type | Verifies |
|------|------|----------|
| Constructor_CreatesJobObject | STRUCTURAL | API contract |
| AssignProcess_CurrentProcess_Succeeds | BEHAVIORAL | API contract |
| AssignProcess_AlreadyExited_IsNoOp | BEHAVIORAL | Edge case |
| AssignProcess_ZeroPid_Throws | STRUCTURAL | Validation |
| AssignProcess_NegativePid_Throws | STRUCTURAL | Validation |
| Dispose_Idempotent | STRUCTURAL | API contract |
| AssignProcess_AfterDispose_Throws | STRUCTURAL | API contract |
| KillOnClose_TerminatesAssignedProcess | **ATTEMPTED REAL** | KILL_ON_JOB_CLOSE |
| TwoSeats_IndependentJobs | **ATTEMPTED REAL** | Seat isolation |
| MultipleProcesses_AllTracked | STRUCTURAL | API contract |
| (11 manager tests) | BEHAVIORAL/STRUCTURAL | Manager lifecycle |

### Critical assessment: KillOnClose test

The test starts `ping -t`, assigns to Job, disposes Job, then checks `WaitForExit(5000)`. If the process is already in the test runner's Job, `AssignProcessToJobObject` fails silently, and the test falls back to manual `Kill()`.

**VERDICT: NO PRODUCTION KILL_ON_JOB_CLOSE VERIFICATION.** The test proves the API contracts work, but does NOT prove that `KILL_ON_JOB_CLOSE` actually terminates processes in a real scenario.

### Recommendation

Add a comment in the test documenting this limitation. True KILL_ON_JOB_CLOSE verification requires running outside the test runner's Job — e.g., a separate executable launched by the test.

---

## Security Review

| Check | Result |
|-------|--------|
| Handle leak | ✅ SafeJobHandle + finally block |
| TOCTOU | ⚠️ Minor — process could exit between OpenProcess and AssignProcess |
| PID reuse | ✅ ProcessIdentity uses PID + StartedAt |
| Cross-seat assignment | ✅ Each seat has independent Job |
| Untrusted PID | ✅ SYSTEM service scope only |
| Excessive access | ✅ Only JOB_OBJECT_ASSIGN_PROCESS + PROCESS_TERMINATE |
| Job escape via BREAKAWAY | ⚠️ Not verified — depends on Vibepollo behavior |

---

## Architectural Invariants

| Invariant | Status | Evidence |
|-----------|--------|----------|
| INV-JOB-1: Each Seat has at most one Job | ✅ PASS | `_groups.GetOrAdd` per seatId |
| INV-JOB-2: Job belongs to one Seat | ✅ PASS | Keyed by seatId GUID |
| INV-JOB-3: Process of A not in Job of B | ✅ PASS | Per-seat assignment |
| INV-JOB-4: Closing Job terminates processes | ⚠️ PARTIAL | Only if assignment succeeded |
| INV-JOB-5: Job not in Domain/Core | ✅ PASS | In Service layer |
| INV-JOB-6: Not Vibepollo-specific | ✅ PASS | Generic PID assignment |
| INV-JOB-7: Tracker and Group separate | ✅ PASS | Independent interfaces |
| INV-JOB-8: No raw handles leak | ✅ PASS | SafeJobHandle |

**INV-JOB-4 is PARTIAL** because `AssignProcessToJobObject` may fail silently, leaving processes unassigned.

---

## Findings

### Finding 1: JOB OBJECT SAFETY GUARANTEE IS CONDITIONAL

**Severity: MEDIUM**

If `AssignProcessToJobObject` fails with `ERROR_ACCESS_DENIED` (process already in another job), the process is NOT in the Job. `KILL_ON_JOB_CLOSE` will NOT terminate it.

**Current code:**
```csharp
// WindowsProcessGroup.cs line 79
if (error == 5)
    return; // Best-effort: process may already be in another job
```

**Impact:** In production, provider processes are unlikely to be in pre-existing jobs. But if they are, the safety net fails silently.

**Mitigation:** The primary cleanup path (`VibepolloManager.Stop()` → `Process.Kill(entireProcessTree: true)`) is independent of the Job Object. The Job is a fallback.

### Finding 2: Games launched by ProcessInjector are NOT in the Job

**Severity: LOW (for current scope)**

`SeatManager.LaunchAppInSeatAsync()` calls `ProcessInjector.LaunchInSessionAsync()` which creates the process. The process is NOT assigned to the Job Object. No code assigns game processes to the Job.

**Impact:** Games are cleaned up only by explicit Kill calls (which don't exist for games in current code). This is a pre-P0-2 gap, not a regression.

### Finding 3: No orphan scan on service restart

**Severity: LOW (documented gap)**

After service crash/restart, the new instance starts with empty tracker and empty group manager. Processes that escaped the Job before the crash are orphaned with no detection.

**Mitigation:** P0-3 scope (orphan scan).

### Finding 4: KILL_ON_JOB_CLOSE not verified by tests

**Severity: LOW (test limitation)**

All KillOnClose tests run inside the test runner's Job Object, so `AssignProcessToJobObject` fails for test processes. The tests verify API contracts but not the actual termination guarantee.

**Recommendation:** Document this limitation. True verification requires a standalone test executable.

### Finding 5: logging when assignment fails is insufficient

**Severity: LOW**

When `AssignProcessToJobObject` fails, the code in `WindowsProcessGroup.AssignProcess` silently returns. The caller (`VibepolloManager`) logs at `LogWarning` level. But the `WindowsProcessGroup` itself does not log.

**Recommendation:** Add `_logger.LogWarning` in `WindowsProcessGroup.AssignProcess` when `ERROR_ACCESS_DENIED` occurs.

---

## Required Changes

### MUST FIX

None. The current implementation is functionally correct for the stated purpose (safety net, not sole mechanism).

### SHOULD FIX

1. **Update `IProcessGroup` documentation** to explicitly state: "AssignProcess is best-effort. If the process is already in another job, assignment fails silently. The safety guarantee applies only to successfully assigned processes."

2. **Add logging in `WindowsProcessGroup.AssignProcess`** when `ERROR_ACCESS_DENIED` occurs — at `LogWarning` level, not `LogDebug`.

3. **Document KILL_ON_JOB_CLOSE test limitation** — tests cannot verify the guarantee because test processes are in the runner's Job.

### NICE TO HAVE

4. **Consider `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`** — allows child processes to silently break away from the Job. This is NOT needed now (children inheriting the Job is the desired behavior), but should be documented as a future consideration.

---

## Verdict

### **PASS WITH CHANGES**

The Job Object implementation is structurally correct and will work as a safety net in production. The `KILL_ON_JOB_CLOSE` flag is properly configured. The SafeHandle lifecycle is correct. Multi-seat isolation is guaranteed.

The safety guarantee is **CONDITIONAL** on successful `AssignProcessToJobObject`, which may fail if the process is already in another job. This is an acceptable design trade-off because:
1. The primary cleanup path is explicit `Kill()`, not Job disposal
2. In production, provider processes are unlikely to be in pre-existing jobs
3. Making assignment mandatory would break streaming in edge cases

**Changes required before commit:**
1. Update documentation to reflect conditional guarantee
2. Add warning-level logging for assignment failures

**Can proceed to P0-3 after these changes.**

---

*Reviewed by: Buffy (Codebuff)*
*Date: 2026-08-30*
*Status: PASS WITH CHANGES*
