# Game Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define game management, process tracking, and lifecycle.

---

## Game Model

### GameDefinition

| Property | Type | Description |
|----------|------|-------------|
| Id | Guid | Unique identifier |
| Name | string | Display name |
| ExecutablePath | string | Path to game executable |
| Arguments | string? | Command-line arguments |
| WorkingDirectory | string? | Working directory |

**FACT**: Games defined in apps.json.

### GameInstance

| Property | Type | Description |
|----------|------|-------------|
| GameInstanceId | Guid | Unique identifier |
| GameDefinition | GameDefinition | Reference to definition |
| SeatId | Guid | Owning seat |
| ProcessId | int | OS process ID |
| State | GameProcessState | Current state |

---

## Game Process States

```
Starting → Running → Exited
                  → Crashed
                  → Unknown
```

### Transitions

| From | To | Event |
|------|-----|-------|
| Starting | Running | Process alive |
| Starting | Unknown | PID not found |
| Running | Exited | Exit code 0 |
| Running | Crashed | Exit code != 0 |
| Running | Unknown | Process not found |
| Unknown | Running | Process rediscovered |

---

## Game Lifecycle

### Launch

```
1. ProcessInjector.LaunchInSessionAsync
   └── CreateProcessAsUser into seat session
2. Track PID in ProcessTracker
3. Monitor process exit
```

### Monitoring

```
1. Check if process alive (PID check)
2. Check exit code (if exited)
3. Detect crash (exit code != 0)
```

### Stop

```
1. Graceful shutdown (WM_CLOSE)
2. Wait for timeout
3. Force terminate (TerminateProcess)
4. Remove from ProcessTracker
```

### Cleanup on Teardown

```
1. Kill all game processes for seat
2. Job Object ensures cleanup
3. Remove from tracking
```

---

## Process Tracking

### PID → Seat Mapping

```csharp
Dictionary<int, Guid> _pidToSeat;
Dictionary<Guid, HashSet<int>> _seatToPids;
```

### Tracking Operations

| Operation | Description |
|-----------|-------------|
| Track(seatId, pid) | Add PID to mapping |
| Untrack(pid) | Remove PID from mapping |
| GetSeatForPid(pid) | Get seat owning PID |
| GetPidsForSeat(seatId) | Get all PIDs for seat |
| GetSeatProcesses(seatId) | Get all game processes for seat |

**DECISION**: ProcessTracker is P0 priority.

---

## Game Cleanup

### On Game Exit

```
1. Process exit detected
2. Log exit code
3. Remove from ProcessTracker
4. Optionally restart (configurable)
```

### On Seat Teardown

```
1. Kill all game processes for seat
2. Job Object ensures cleanup
3. Clear ProcessTracker entries
```

---

## Future: Game Crash Detection

### Gap

No game crash detection exists.

### What's Needed

1. Process exit monitoring
2. Exit code checking
3. Optional auto-restart
4. Crash logging

**DECISION**: Game crash detection is P1 priority.

---

## Future: Game RDP Compatibility

### Gap

Some games refuse to run in RDP sessions.

### What's Needed

1. Application Compatibility Layer
2. RDP detection patching
3. DirectX 8/9 support

### Status

**OPEN QUESTION**: How does Duo's Application Compatibility Layer work?

**DECISION**: Game RDP compatibility is P4 (experimental).

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Games defined in apps.json | VibepolloConfigBuilder | FACT |
| ProcessInjector launches games | ProcessInjector.cs | FACT |
| No game crash detection | Codebase search | FACT (absent) |
| No game process tracking | Codebase search | FACT (absent) |
| Job Object ensures cleanup | Windows API | FACT |
