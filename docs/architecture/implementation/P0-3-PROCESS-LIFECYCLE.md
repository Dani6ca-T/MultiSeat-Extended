# P0-3 Process Lifecycle & Recovery

**Date**: 2026-08-31
**Status**: IMPLEMENTED
**Build**: 299 tests, 285 passed, 14 skipped, 0 failed

---

## Problem

After P0-1 (Process Ownership) and P0-2 (Job Objects), the codebase had:
- Centralized process tracking via `IProcessTracker` (PID + start time)
- Guaranteed cleanup via `IProcessGroup` (Job Object with KILL_ON_JOB_CLOSE)
- Polling-based crash detection via `SessionHealthCheck` (5s interval)

Missing:
- Event-driven process exit detection (immediate, no polling overhead)
- PID reuse safety during exit events
- Expected vs unexpected exit classification
- Startup orphan detection for diagnostic visibility
- Clean monitoring lifecycle tied to provider start/stop

---

## Architecture

```
Seat
  ├── IProcessTracker     (ownership — "who owns what?")
  ├── IProcessGroup       (cleanup — "guaranteed termination?")
  └── IProcessMonitor     (lifecycle — "when does it exit?")
```

### Three Separate Concerns

| Concern | Interface | Responsibility |
|---------|-----------|----------------|
| Ownership | `IProcessTracker` | PID → SeatId mapping |
| Cleanup | `IProcessGroup` | KILL_ON_JOB_CLOSE safety net |
| Lifecycle | `IProcessMonitor` | Event-driven exit detection |

---

## ProcessIdentity (PID Reuse Protection)

```csharp
ProcessIdentity = PID + StartedAt
```

When a process exits, the monitor verifies the identity before raising `ProcessExited`. If the PID was reused (different StartedAt), the exit event is silently suppressed.

**Invariant**: An exit event for PID X can only affect the process that was started at time T, not a new process that happens to reuse PID X.

---

## ProcessMonitor Integration

### StartAsync (Provider Launch)

```
ProcessInjector.LaunchVibepolloInSessionAsync
  → Process created
  → ProcessIdentity resolved (PID + StartTime)
  → IProcessTracker.Register
  → IProcessMonitor.StartMonitoring  ← NEW
  → IProcessGroup.AssignProcess
```

### Stop (Intentional Kill)

```
VibepolloManager.Stop
  → MarkExpectedExit  ← NEW (prevents crash recovery)
  → StopMonitoring     ← NEW (releases OS handles)
  → UnregisterFromTracker
  → Process.Kill
```

### KillForReconnect (Sleep/Wake)

```
VibepolloManager.KillForReconnect
  → MarkExpectedExit  ← NEW
  → StopMonitoring     ← NEW
  → UnregisterFromTracker
  → Process.Kill
  → Reset RestartCount (sleep ≠ crash)
```

### RestartAsync (Crash Recovery)

```
VibepolloManager.RestartAsync
  → MarkAndStopMonitoring(old)  ← NEW
  → UnregisterFromTracker(old)
  → ProcessInjector.LaunchVibepolloInSessionAsync
  → ProcessIdentity resolved
  → ProcessTracker.Register(new)
  → ProcessMonitor.StartMonitoring(new)  ← NEW
  → ProcessGroup.AssignProcess(new)
```

---

## Expected vs Unexpected Exit

| Scenario | WasExpected | Recovery Triggered? |
|----------|-------------|---------------------|
| Seat.Stop → Provider.Stop | true | No |
| Sleep → KillForReconnect | true | No |
| RestartAsync → old process | true | No |
| Provider crash | false | Yes (SessionHealthCheck) |
| Windows kill | false | Yes (SessionHealthCheck) |

The `MarkExpectedExit()` call sets a flag BEFORE the kill. When `Process.Exited` fires, the handler checks this flag and includes it in `ProcessExitInfo.WasExpected`.

---

## Startup Orphan Detection

Runs once at service startup after the existing `KillOrphanedVibepolloProcesses()`:

1. Scan for processes matching Vibepollo executable name
2. Read command line via WMI
3. Extract config directory
4. Correlate to known seats by account name
5. Log findings (informational, no killing)

The aggressive cleanup (`KillOrphanedVibepolloProcesses`) already kills managed orphans. The detector provides visibility into non-managed leftovers.

---

## Thread Safety

- `ConcurrentDictionary<ProcessIdentity, MonitoringEntry>` for state
- `volatile bool MarkedExpected` for cross-thread flag
- `Process.Exited` fires on thread-pool thread
- All operations are thread-safe

---

## Tests

```
Before: 283 tests (269 pass, 14 skip)
After:  299 tests (285 pass, 14 skip)
New:     16 tests (all pass)
```

### New Test Coverage

| Test | What it verifies |
|------|-----------------|
| `MonitoredCount_InitiallyZero` | Clean state |
| `StartMonitoring_WithNegativePid_ThrowsOnIdentity` | Identity validation |
| `StartMonitoring_WithZeroPid_ThrowsOnIdentity` | Identity validation |
| `StopMonitoring_NonExistentEntry_IsNoOp` | Idempotent stop |
| `StopMonitoringAll_NonExistentSeat_IsNoOp` | Idempotent bulk stop |
| `MarkExpectedExit_NonExistentEntry_IsNoOp` | Idempotent mark |
| `Dispose_CleansUpAllEntries` | Resource cleanup |
| `ProcessExited_Event_HasNoSubscribersByDefault` | Event initialization |
| `StartMonitoring_StoppedProcess_DoesNotMonitor` | Already-exited process |
| `StartMonitoring_ThenStopMonitoring_ReleasesResources` | Handle lifecycle |
| `StopMonitoringAll_RemovesOnlyTargetSeatEntries` | Seat-scoped stop |
| `Dispose_PreventsFurtherEvents` | Post-dispose safety |
| `Concurrent_StartStop_DoesNotThrow` | Thread safety |
| `ProcessExitInfo_CarriesFullIdentity` | Value object correctness |
| `ProcessExitInfo_ExpectedExit_Flagged` | Expected flag semantics |
| `StartMonitoring_DuplicatePid_DifferentStartTime_ReplaceStale` | PID reuse handling |

---

## Known Limitations

1. **No game process monitoring yet** — only provider (Vibepollo) processes are monitored. Game process tracking is P1.
2. **No backoff implementation** — P0-3 provides the foundation (unexpected exit detection) but progressive backoff is P1.
3. **Orphan detection is diagnostic only** — does not auto-kill or auto-adopt. Aggressive cleanup exists separately in `KillOrphanedVibepolloProcesses`.
4. **Process.Exited may fire late** — if the process handle is not yet obtained when the process exits, the event may not fire. Mitigated by `HasExited` check at registration time.

---

## Files Changed

| File | Change |
|------|--------|
| `MultiSeat.Shared/Models/ProcessExitInfo.cs` | NEW — exit event value object |
| `MultiSeat.Shared/IProcessMonitor.cs` | NEW — lifecycle monitoring interface |
| `MultiSeat.Service/ProcessTracking/WindowsProcessMonitor.cs` | NEW — Windows implementation |
| `MultiSeat.Service/ProcessTracking/StartupOrphanDetector.cs` | NEW — startup orphan scan |
| `MultiSeat.Service/Streaming/VibepolloManager.cs` | MODIFIED — integrate monitoring |
| `MultiSeat.Service/Program.cs` | MODIFIED — DI registration |
| `MultiSeat.Service/MultiSeatWorker.cs` | MODIFIED — startup orphan scan |
| `MultiSeat.Tests/ProcessTracking/ProcessMonitorTests.cs` | NEW — 16 unit tests |

---

## Next Step

**P1: Game Process Tracking + Provider Abstraction**

P0-3 provides the foundation for process lifecycle management. The next phase should:

1. Extend `IProcessMonitor` usage to game processes (track game launch, exit, crash)
2. Introduce `IStreamingProvider` abstraction (decouple from Vibepollo-specific code)
3. Implement progressive backoff for provider crash recovery
4. Add health check integration with `IProcessMonitor.ProcessExited` event
