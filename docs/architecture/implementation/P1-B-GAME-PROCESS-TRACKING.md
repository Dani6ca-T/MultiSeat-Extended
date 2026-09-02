# P1-B — Game Process Tracking

## Date

2026-08-31

## Summary

Game processes are now first-class tracked entities using the existing P0-1/P0-2/P0-3 infrastructure. No new process-management infrastructure was introduced.

## Changed Files

| File | Change |
|------|--------|
| `SeatManager.cs` | Game tracking in `LaunchAppInSeatAsync`, teardown cleanup, `OnGameExited` handler |
| `OnConnectAppLauncher.cs` | Game tracking on on-connect launch, untracking on disconnect |
| `GameProcessTrackingTests.cs` | NEW — 19 deterministic tests |

## Architecture

```
Seat
 ├── Provider → tracked by ProcessTracker + ProcessMonitor + JobObject ✅
 ├── Game A   → tracked by ProcessTracker + ProcessMonitor + JobObject ✅  (NEW)
 └── Game B   → tracked by ProcessTracker + ProcessMonitor + JobObject ✅  (NEW)
```

Both launch paths (API + On-Connect) now do:

```
ProcessInjector → PID → ResolveProcessIdentity(pid)
  → _processTracker.Register(identity, seatId, Game)
  → _processMonitor.StartMonitoring(identity, seatId, Game)
  → _processGroupManager.GetOrCreateForSeat → group.AssignProcess(pid)
```

## Key Decisions

- **ProcessIdentity reuse:** Game uses same `PID + StartedAt` as provider — PID reuse protected.
- **Observational only:** Game exit events are logged, no automatic restart. Game crash recovery is P1 scope.
- **Shared Job Object:** Games assigned to seat's existing per-seat Job Object. Best-effort (ERROR_ACCESS_DENIED handled).
- **Teardown order:** Stop monitoring → Unregister all → Stop provider → Dispose Job Object (safety net last).

## Tests

```
Before: 323 (309 pass, 14 skip)
After:  342 (328 pass, 14 skip)
New:     19 (all pass)
```

19 tests cover: registration, multiple games, cross-seat rejection, PID reuse, exit events, expected/unexpected exits, seat isolation, provider-game independence, concurrency, monitor cleanup.

## Remaining Limitations

1. No game crash recovery (observational only — future P1)
2. No game state machine
3. Game processes launched via Vibepollo apps.json are PID-unknown to MultiSeat (outside scope)

## Verdict

**PASS** — all acceptance criteria met, 342 tests green.
