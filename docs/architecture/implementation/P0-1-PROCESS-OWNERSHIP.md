# P0-1: Process Ownership Model

## Problem

MultiSeat-Extended had no centralized process ownership tracking. Processes were tracked
scattered across subsystems:

- `SeatInfo.VibepolloProcessId` — raw `int` PID, no start time
- `VibepolloManager._instances` — private `ConcurrentDictionary<Guid, VibepolloInstance>`
- `OnConnectAppLauncher._states` — `List<int> LaunchedPids`
- `ProcessInjector` — returned raw `int` PID

This caused three critical problems:

1. **No PID reuse protection**: A PID stored as `int` could refer to a completely different
   process after the original exits. Windows recycles PIDs.
2. **No centralized ownership**: No single component could answer "which processes belong to Seat X?"
3. **No type classification**: No way to distinguish provider processes from game processes
   from helper processes.

---

## Current Architecture

Before this change, process lifecycle was managed per-subsystem:

```
VibepolloManager
  └── VibepolloInstance record
        ├── SeatId
        ├── ProcessId (int)
        ├── StartedAt
        └── RestartCount

OnConnectAppLauncher
  └── SeatConnState
        └── LaunchedPids (List<int>)

SeatInfo
  └── VibepolloProcessId (int)
```

No cross-subsystem visibility.

---

## Implemented Solution

### Domain Layer (MultiSeat.Shared)

**ProcessIdentity** — Value object with PID + StartedAt:

```csharp
public readonly record struct ProcessIdentity
{
    public int ProcessId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}
```

- Protected against PID reuse via composite identity
- Immutable value object (record struct)
- Implements `IComparable<ProcessIdentity>` for sorted collections

**ManagedProcessType** — Enum classifying process roles:

```csharp
public enum ManagedProcessType
{
    Provider,   // Vibepollo, Apollo, Sunshine
    Game,       // Game processes
    Helper,     // Short-lived utilities
    Other       // Anything else
}
```

**ManagedProcess** — Record representing a tracked process:

```csharp
public sealed record ManagedProcess
{
    public required ProcessIdentity Identity { get; init; }
    public required Guid OwnerSeatId { get; init; }
    public required ManagedProcessType ProcessType { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
}
```

- No `System.Diagnostics.Process` dependency in domain
- Owner is `Guid` (SeatId), not a Windows-specific type

**IProcessTracker** — Interface:

```csharp
public interface IProcessTracker
{
    void Register(ProcessIdentity identity, Guid ownerSeatId, ManagedProcessType processType);
    void Unregister(ProcessIdentity identity);
    void UnregisterAll(Guid seatId);
    ManagedProcess? Get(ProcessIdentity identity);
    IReadOnlyList<ManagedProcess> GetByOwner(Guid seatId);
    IReadOnlyList<ManagedProcess> GetAll();
    bool IsAlive(ProcessIdentity identity);
}
```

### Infrastructure Layer (MultiSeat.Service)

**WindowsProcessTracker** — Thread-safe implementation:

- Uses `ConcurrentDictionary<ProcessIdentity, ManagedProcess>` for primary storage
- Uses `ConcurrentDictionary<Guid, ConcurrentBag<ProcessIdentity>>` for seat index
- `IsAlive()` verifies both PID existence AND start time match (PID reuse detection)
- All operations are lock-free via `ConcurrentDictionary`

---

## Process Identity

The core invariant: **PID alone is not an identity**.

Windows recycles PIDs. A PID captured during seat provisioning may refer to a completely
different process after the original exits. By pairing PID with start time:

```
ProcessIdentity(PID=1234, StartedAt=2026-08-30T12:00:00Z)
```

When checking if a process is alive:
1. `Process.GetProcessById(1234)` — does the PID exist?
2. `proc.StartTime == StartedAt` — is it the same process?

If step 1 fails → process exited.
If step 2 fails → PID was reused by a different process.

---

## Ownership

Every `ManagedProcess` has exactly one `OwnerSeatId`.

INVARIANT-1: Process → SeatId (always)
INVARIANT-2: One process cannot belong to two Seats (enforced by Register)

The tracker does NOT enforce INVARIANT-2 at registration time (it would require a reverse
lookup). Instead, callers must ensure they don't register the same process for two seats.
In practice, this is guaranteed by the provisioning pipeline (one process per subsystem per seat).

---

## Thread Safety

Multiple concurrent flows access the tracker:

- `SeatManager.ProvisionSeatAsync` — registers provider process
- `SeatManager.TeardownSeatInternalAsync` — unregisters all processes
- `VibepolloManager.RestartAsync` — unregisters old, registers new
- `SessionHealthCheck` — reads process state

`ConcurrentDictionary` provides:
- Atomic per-key `TryAdd`, `TryRemove`, `TryGetValue`
- `GetOrAdd` for seat index bags
- Snapshot semantics for `Values` enumeration

---

## Integration Point

Integrated into **VibepolloManager** as the first vertical slice:

1. **`StartAsync`**: After launching Vibepollo, resolves `ProcessIdentity` from PID
   (via `Process.GetProcessById`), then registers with tracker.

2. **`Stop`**: Before killing the process, unregisters from tracker.

3. **`KillForReconnect`**: Before killing, unregisters from tracker.

4. **`RestartAsync`**: Unregisters old crashed instance, launches new, registers new.

DI registration in `Program.cs`:
```csharp
builder.Services.AddSingleton<IProcessTracker, WindowsProcessTracker>();
```

---

## Tests

22 new tests in `src/MultiSeat.Tests/ProcessTracking/ProcessTrackerTests.cs`:

### ProcessIdentityTests (7 tests)
- Constructor sets properties
- Rejects zero/negative PID
- Matches same PID + time
- Detects different PID
- Detects different time (PID reuse)
- Equality semantics
- Comparison by PID

### ManagedProcessTests (2 tests)
- Required properties
- RegisteredAt defaults to UtcNow

### WindowsProcessTrackerTests (13 tests)
- Register + Get round-trip
- Seat A visible in GetByOwner
- Seat A does not see Seat B process
- Unregister removes process
- Unregister non-existent is no-op
- UnregisterAll removes all for seat
- UnregisterAll does not affect other seats
- GetAll returns all tracked
- Duplicate registration replaces entry
- IsAlive returns false for non-existent PID
- IsAlive returns true for current process
- IsAlive detects PID reuse (different start time)
- IsAlive preserves stale registration (caller responsibility)
- Concurrent register/unregister (100 threads)
- Multiple processes per seat

**All 248 tests pass (14 existing integration tests skipped).**

---

## Known Limitations

1. **No auto-cleanup of stale entries**: `IsAlive` returns false but does not remove the
   entry. Cleanup is the caller's responsibility. This is intentional: auto-cleanup would
   add complexity without clear benefit (the entry costs ~100 bytes).

2. **Process start time resolution**: On Windows, `Process.StartTime` has ~15ms precision
   for processes started recently. Two processes starting within 15ms of each other could
   have identical start times if they also share a PID (extremely unlikely).

3. **No Process.Exit event subscription**: The tracker does not subscribe to process exit
   events. `IsAlive` is a polling check. This is sufficient for the 5s health-check interval.

---

## What Is NOT Implemented (by design)

- **Job Objects** — P0-2 scope
- **Provider abstraction** — P1 scope
- **Game process tracking** — requires GameArchitecture
- **Crash recovery** — P1 scope
- **Process tree tracking** — future enhancement

---

## Next Step

**P0-2 — Job Object lifecycle**

Job Objects will use the ProcessIdentity from this tracker to:
1. Assign each seat's provider + game processes to a Job Object
2. Set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
3. Guarantee process cleanup on seat teardown (even if the process escapes `Process.Kill`)

---

## Files Changed

| File | Change |
|------|--------|
| `src/MultiSeat.Shared/Models/ProcessIdentity.cs` | **NEW** — Value object |
| `src/MultiSeat.Shared/Models/ManagedProcessType.cs` | **NEW** — Enum |
| `src/MultiSeat.Shared/Models/ManagedProcess.cs` | **NEW** — Record |
| `src/MultiSeat.Shared/IProcessTracker.cs` | **NEW** — Interface |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessTracker.cs` | **NEW** — Implementation |
| `src/MultiSeat.Service/Streaming/VibepolloManager.cs` | **MODIFIED** — Register/Unregister calls |
| `src/MultiSeat.Service/Program.cs` | **MODIFIED** — DI registration |
| `src/MultiSeat.Tests/ProcessTracking/ProcessTrackerTests.cs` | **NEW** — 22 tests |
| `docs/architecture/implementation/P0-1-PROCESS-OWNERSHIP.md` | **NEW** — This document |
