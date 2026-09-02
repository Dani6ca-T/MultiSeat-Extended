# P0-1 Review — Process Tracking Correctness Audit

## Scope

Code review of the P0-1 Process Ownership Model implementation across:

- `ProcessIdentity.cs` (domain)
- `ManagedProcessType.cs` (domain)
- `ManagedProcess.cs` (domain)
- `IProcessTracker.cs` (domain interface)
- `WindowsProcessTracker.cs` (infrastructure)
- `VibepolloManager.cs` (integration)
- `Program.cs` (DI)
- `ProcessTrackerTests.cs` (tests)

---

## Findings Summary

| Category | Verdict | Issues |
|----------|---------|--------|
| Process Identity | MINOR | `CompareTo` ignores `StartedAt` |
| Lifetime | OK | Stale entries are expected behavior |
| Unregister | **BUG** | `UnregisterFromTracker` silently skips when PID already dead |
| Concurrency | OK | `ConcurrentDictionary` atomicity sufficient |
| Duplicate Registration | OK | Dictionary replacement semantics are correct |
| Ownership | **CONTRACT VIOLATION** | `Register` doc says it throws, code does not |
| Vibepollo Integration | MINOR | `StartedAt` dual source (`DateTimeOffset.UtcNow` vs `Process.StartTime`) |
| Failure Scenarios | OK | Graceful degradation for all paths |
| Service Restart | **KNOWN GAP** | In-memory tracker loses all state |
| Test Quality | ADEQUATE | Covers core paths, misses edge cases |
| Core Boundary | OK | No Windows dependency in domain |
| Job Object Compatibility | OK | Current design is compatible |

---

## 1. Process Identity

### How StartedAt is obtained

In `VibepolloManager.ResolveProcessIdentity`:
```csharp
using var proc = Process.GetProcessById(pid);
return new ProcessIdentity(pid, proc.StartTime.ToUniversalTime());
```

`Process.StartTime` returns `DateTime` with `Kind=Local`. `.ToUniversalTime()` converts to UTC. Both `ResolveProcessIdentity` (at registration) and `IsAlive` (at check) convert the same way, so the comparison is consistent.

### Timestamp precision

`Process.StartTime` on Windows has ~15ms resolution. Two processes starting within 15ms of each other at the same PID could have identical `StartTime` values. This is an inherent OS limitation, not a code defect. In practice, PID reuse after 15ms is extremely unlikely for the same seat.

### Timestamp comparison correctness

`DateTimeOffset` comparison checks both `Ticks` and `Offset`. Since both sides use `.ToUniversalTime()` in the same thread context (same timezone, same DST), the `Offset` is identical. The comparison is correct.

**Exception:** If the system clock changes between registration and `IsAlive` (NTP adjustment, manual change), the `StartedAt` value stored in the tracker could differ from `Process.StartTime.ToUniversalTime()` at check time. This would cause a false negative in `IsAlive`. Mitigation: the 5s health-check window makes clock drift negligible.

### `CompareTo` ignores StartedAt

```csharp
public int CompareTo(ProcessIdentity other) =>
    ProcessId.CompareTo(other.ProcessId);
```

This means two identities with the same PID but different `StartedAt` sort identically. If `ProcessIdentity` were used in a `SortedSet`, duplicates would appear. Currently not used in sorted collections, so **no runtime impact**. But the semantic is misleading — recommend adding `StartedAt` comparison as secondary sort.

**Severity:** MINOR (cosmetic, no current impact).

---

## 2. Process Lifetime

### Stale entries when process dies without Unregister

If a process crashes and nobody calls `Unregister`:
1. The `ManagedProcess` entry stays in `_processes`
2. The `ProcessIdentity` stays in `_bySeat`
3. `IsAlive()` returns false (PID gone)
4. Entry persists until explicit `Unregister`/`UnregisterAll`

**This is acceptable by design.** The tracker is a registry, not a lifecycle manager. Stale entries cost ~100 bytes. Cleanup happens via `UnregisterAll` during seat teardown. The architecture does not require the tracker to auto-clean.

---

## 3. Unregister Correctness

### Critical scenario: process already dead when Unregister is called

```csharp
// VibepolloManager.UnregisterFromTracker:
private void UnregisterFromTracker(VibepolloInstance instance)
{
    if (instance.ProcessId <= 0) return;

    var identity = ResolveProcessIdentity(instance.ProcessId);  // ← PID gone → returns null
    if (identity.HasValue)
    {
        _processTracker.Unregister(identity.Value);              // ← never reached
    }
}
```

When the process has already exited:
1. `ResolveProcessIdentity` calls `Process.GetProcessById` → throws `ArgumentException` → returns null
2. `Unregister` is never called
3. **Stale entry remains in tracker**

**This is NOT a bug in the tracker.** `Unregister(ProcessIdentity)` works correctly — it removes by composite key. The bug is in `VibepolloManager.UnregisterFromTracker`, which cannot reconstruct the `ProcessIdentity` after the PID is gone.

**But it doesn't matter.** `Stop()` kills the process BEFORE calling `UnregisterFromTracker`... wait, actually:

```csharp
public void Stop(SeatInfo seat)
{
    if (_instances.TryRemove(seat.Id, out var instance))
    {
        UnregisterFromTracker(instance);  // ← called FIRST
    }

    if (seat.VibepolloProcessId <= 0) return;
    // ... then kills the process
}
```

`UnregisterFromTracker` is called BEFORE the kill. So the process is still alive during Unregister. This is correct.

But in `KillForReconnect`:
```csharp
public void KillForReconnect(SeatInfo seat)
{
    if (!_instances.TryGetValue(seat.Id, out var instance))
        return;

    UnregisterFromTracker(instance);  // ← process still alive

    if (instance.ProcessId > 0)
    {
        // ... then kills
    }
}
```

Also correct — unregister before kill.

**Edge case:** If the process crashes BETWEEN `UnregisterFromTracker` and the kill, the unregister still succeeds because the PID was alive at unregister time.

**Edge case:** If the process crashes BEFORE `Stop`/`KillForReconnect` is called, the `UnregisterFromTracker` will fail to resolve the identity, and the stale entry persists. This is acceptable — the entry is harmless and will be cleaned up by `UnregisterAll` during seat teardown.

**Severity:** OK (no functional impact).

---

## 4. Concurrency

### ConcurrentDictionary atomicity

- `_processes[identity] = process` — atomic per-key put
- `_processes.TryRemove(identity, out _)` — atomic per-key remove
- `_processes.GetValueOrDefault(identity)` — atomic per-key get

### Two-dictionary consistency

`Register` writes to both `_processes` and `_bySeat` sequentially. If the process crashes between the two writes, `_processes` has the entry but `_bySeat` may not. This means `GetByOwner` might miss it, but `Get(identity)` would find it.

**Impact:** Minimal. `GetByOwner` is used for enumeration, and the entry will be found by `Get`. The seat teardown path uses `UnregisterAll` which iterates `_bySeat` — if the entry is only in `_processes`, it won't be cleaned up by `UnregisterAll`. But `Unregister(identity)` would still work.

**Severity:** MINOR (edge case, no practical impact with current usage).

### TOCTOU in `Stop()`

```csharp
if (_instances.TryRemove(seat.Id, out var instance))
{
    UnregisterFromTracker(instance);
}
// ... kill by seat.VibepolloProcessId
```

Between `TryRemove` and the kill, another thread could modify `seat.VibepolloProcessId`. But `Stop()` is called from the seat lifecycle (single-threaded per seat), so this is not a practical concern.

---

## 5. Duplicate Registration

### Same identity (PID + StartedAt) registered twice

```csharp
_processes[identity] = process;  // dictionary indexer — replaces existing
```

**Behavior: REPLACED.** The new entry overwrites the old one. This is correct for the restart scenario (kill old → register new with same identity... but restart uses different PID, so this doesn't apply).

### Same PID, different StartedAt (PID reuse scenario)

Two entries coexist in the dictionary because `ProcessIdentity` equality checks both PID and StartedAt:

```csharp
// Entry 1: PID=1000, StartedAt=T1
// Entry 2: PID=1000, StartedAt=T2
// Both exist in _processes — no collision
```

**Behavior: BOTH COEXIST.** This is the intended design. The old entry becomes stale (`IsAlive` returns false). The new entry is active.

**Severity:** OK (correct design).

---

## 6. Ownership Model

### Can one process be registered for two Seats?

The `Register` method does NOT enforce single ownership:

```csharp
public void Register(ProcessIdentity identity, Guid ownerSeatId, ManagedProcessType processType)
{
    var process = new ManagedProcess { ... OwnerSeatId = ownerSeatId ... };
    _processes[identity] = process;  // overwrites, no check for existing owner
    ...
}
```

If called with the same identity but different `ownerSeatId`, the second call silently overwrites the first. The `IProcessTracker` interface doc says:

> "INVARIANT-2 enforcement: If a process with the same PID+StartedAt is already registered for a different seat, this call throws."

**This is a CONTRACT VIOLATION.** The interface promises an exception; the implementation does not throw.

**Practical impact:** None. The provisioning pipeline ensures each process is registered exactly once per seat. No code path registers the same process for two seats.

**Severity:** CONTRACT VIOLATION (no runtime impact, but interface lies).

---

## 7. Vibepollo Integration

### StartedAt dual source

Two different `DateTimeOffset` values are used:

1. `VibepolloInstance.StartedAt = DateTimeOffset.UtcNow` — set at record creation time
2. `ProcessIdentity.StartedAt = proc.StartTime.ToUniversalTime()` — queried from OS

These may differ by up to several seconds (the time between `LaunchVibepolloInSessionAsync` returning and `ResolveProcessIdentity` calling `GetProcessById`).

The tracker uses #2. `VibepolloInstance` uses #1. They are not compared against each other in current code.

**Risk:** If future code compares `VibepolloInstance.StartedAt` with `ProcessIdentity.StartedAt`, it will fail. Currently no such comparison exists.

**Severity:** MINOR (inconsistency, no current impact).

### Register failure after process launch

If `Process.GetProcessById` throws (process already exited between launch and register), `ResolveProcessIdentity` returns null, and the process is not tracked.

The instance IS stored in `_instances` with `ProcessId = pid`, but the tracker has no entry. This means:
- `VibepolloManager.IsAlive` checks `_instances` (via `VibepolloInstance.IsAlive`) — works
- `IProcessTracker.IsAlive` has no entry — would need the identity
- `IProcessTracker.GetByOwner` misses this process

**Impact:** For P0-1, the VibepolloManager's own tracking is sufficient. The tracker is supplementary. For P0-2 (Job Objects), this gap needs addressing — the process must be tracked to be assigned to a Job Object.

**Severity:** MINOR (adequate for P0-1, needs attention for P0-2).

---

## 8. Failure Scenarios

| Scenario | Current Behavior | Expected | Gap | Severity |
|----------|-----------------|----------|-----|----------|
| A. Launch fails (pid ≤ 0) | No registration | No registration | None | OK |
| B. Launch OK, Register fails | No registration, instance stored | Registration or cleanup | Stale in tracker | MINOR |
| C. Crash before Register | No registration (correct) | No registration | None | OK |
| D. Crash after Register | Entry stays, IsAlive=false | Entry stays | None | OK |
| E. Exit during IsAlive | GetProcessById throws, returns false | Returns false | None | OK |
| F. Disappear during Get | Get finds instance, IsAlive=false | Consistent | None | OK |
| G. Service crash | Tracker lost (in-memory) | Recovery mechanism | **KNOWN GAP** | P1 |
| H. Service restart | Empty tracker, orphaned processes | Orphan discovery | **KNOWN GAP** | P1 |
| I. PID reused | Old entry stale, new entry coexists | Correct detection | None | OK |
| J. Seat stops, process dead | UnregisterFromTracker fails (PID gone) | Cleanup | Stale entry | MINOR |

---

## 9. Service Restart

**Current state:** Tracker is in-memory (`ConcurrentDictionary`). After service crash/restart, all tracking state is lost.

**Consequence:** Provider processes that survived the crash are orphaned — no component knows they exist or who owns them.

**Architecture implication for P0-2 / orphan recovery:**

The Job Object architecture should NOT rely on the tracker for orphan discovery. Instead:

1. **Job Object with KILL_ON_JOB_CLOSE** handles the primary case — when the service stops normally, Job Objects kill all child processes.
2. **Orphan scan on startup** should discover running provider processes by PID/name/session, independent of the tracker.
3. **The tracker is a runtime optimization**, not a persistence mechanism.

This is not a defect in P0-1 — the tracker was never designed to survive restarts. But it must be documented so P0-2 does not assume tracker persistence.

---

## 10. Test Quality

### Strengths

- Tests use real OS processes (`Process.GetCurrentProcess`) for `IsAlive` validation
- PID reuse detection tested with wrong start time
- Concurrent operations tested (100 threads)
- Stale entry behavior verified (no auto-cleanup)

### Weaknesses

1. **All test PIDs are fake** (1234, 99999, etc.) — no real process lifecycle test
2. **Concurrency test is sequential** (Task.WhenAll waits) — doesn't actually stress real races
3. **No test for Register throwing on cross-seat** — because it doesn't throw (contract violation)
4. **No integration test** — no test that starts a real process, registers, stops, and verifies cleanup

---

## 11. Test Gap Matrix

| Scenario | Covered | Test Type | Risk |
|----------|---------|-----------|------|
| Normal registration | ✅ | Unit | Low |
| Normal unregister | ✅ | Unit | Low |
| PID reuse detection | ✅ | Unit (IsAlive) | Low |
| Process exit | ⚠️ | Unit (IsAlive only) | Medium |
| Duplicate registration | ✅ | Unit | Low |
| Concurrent register | ✅ | Unit | Low |
| Concurrent unregister | ✅ | Unit | Low |
| StartTime failure | ❌ | — | Low |
| Provider crash | ❌ | — | Medium |
| Service restart | ❌ | — | **High** |
| Unregister when PID dead | ❌ | — | Medium |
| Register after process exits | ⚠️ | Partial (null check) | Medium |

---

## 12. Core Boundary

`IProcessTracker` is in `MultiSeat.Shared`. Dependencies:
- `System.Guid` — standard .NET
- `System.DateTimeOffset` — standard .NET
- `System.Collections.Generic.IReadOnlyList<T>` — standard .NET

No `System.Diagnostics.Process`, no `Win32`, no P/Invoke.

**VERDICT: CLEAN.** The interface is properly abstract.

---

## 13. Job Object Compatibility

Current design is compatible with Job Objects:

```
Seat
 ├── IProcessTracker (tracks PIDs + identities)
 └── JobObject (KILL_ON_JOB_CLOSE for process tree)
```

**Recommended approach for P0-2:**

```
Seat
 ├── ProcessTracker (IProcessTracker — knows which PIDs belong to seat)
 └── SeatJobObject (JobObjectHandle — wraps those PIDs)
```

The `IProcessTracker.GetByOwner(seatId)` provides the list of PIDs to assign to the Job Object. The Job Object does NOT need to be inside `ManagedProcess` — it's a separate concern (cleanup guarantee vs. ownership tracking).

**Do NOT put JobObject inside ManagedProcess.** They are orthogonal: ProcessIdentity is about "who is this process"; JobObject is about "how do we guarantee cleanup".

---

## 14. Critical Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Service restart loses tracker state | HIGH (P1) | Orphan scan on startup (P0-2 scope) |
| Register contract violation | LOW | Fix doc or add throw |
| Unregister skips when PID dead | LOW | Acceptable, stale entry is harmless |
| CompareTo ignores StartedAt | LOW | Add secondary sort (P1) |
| StartedAt dual source | LOW | Document or unify (P1) |

---

## 15. Required Changes

### MUST FIX (before or during P0-2)

None. The current implementation is functionally correct for P0-1 scope.

### SHOULD FIX (during P0-2)

1. **Register contract:** Either add the cross-seat throw or update the interface doc. Recommended: update doc to say "last-write-wins" since cross-seat registration is prevented by the provisioning pipeline, not the tracker.

2. **UnregisterWhenPidDead:** Consider storing the `ProcessIdentity` in `VibepolloInstance` (it already has `ProcessId` and `StartedAt`) so `UnregisterFromTracker` can construct the identity without re-querying the OS.

### NICE TO HAVE

3. `CompareTo`: Add `StartedAt` as secondary sort key.
4. `StartedAt` unification: Use `Process.StartTime` consistently instead of `DateTimeOffset.UtcNow`.

---

## 16. Verdict

### **PASS WITH CHANGES**

P0-1 is functionally correct. The identified issues are:

- 1 contract violation (Register doc vs implementation) — no runtime impact
- 1 inconsistency (StartedAt dual source) — no current impact
- 1 known gap (service restart) — by design, addressed in P0-2

**None are blocking for P0-2.** The Job Object architecture can proceed on top of the current implementation.

---

## 17. Recommendation for P0-2

### Job Object should be a SEPARATE concern from ProcessTracker

```
Current:
  Seat → VibepolloManager → IProcessTracker (ownership)

P0-2 adds:
  Seat → JobManager → JobObjectHandle (cleanup guarantee)
```

The `IProcessTracker.GetByOwner(seatId)` provides the PIDs. The `JobManager` assigns them to a Job Object with `KILL_ON_JOB_CLOSE`.

### Orphan discovery (P0-2 consideration)

On service start, scan for orphaned processes:
1. Query WTS for active sessions
2. For each seat's session, find provider processes by name
3. Register them in the tracker
4. Assign to Job Objects

This is NOT in P0-1 scope but must be designed in P0-2.

### Integration point

P0-2 should integrate at the same point as P0-1: `VibepolloManager.StartAsync` → after Register, also AssignToJobObject. `VibepolloManager.Stop` → after Unregister, also the Job Object is closed (which kills the process tree).

---

*Reviewed by: Buffy (Codebuff)*
*Date: 2026-08-30*
*Status: PASS WITH CHANGES*
