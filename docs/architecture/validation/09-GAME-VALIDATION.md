# Game Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate game process architecture against actual source code.

---

## 1. GameDefinition

**Evidence**: apps.json

**Analysis**: Games defined in apps.json (Vibepollo format).

**VERDICT**: Games defined in configuration.

---

## 2. GameProcess

**Evidence**: SeatManager.cs
```csharp
await _processInjector.LaunchInSessionAsync(
    seat.SessionId, seat.AccountName,
    request.ExecutablePath, request.Arguments, request.WorkingDirectory, ct);
```

**Analysis**: Game launched via ProcessInjector.

**VERDICT**: Game process created.

---

## 3. PID Tracking

**Evidence**: Codebase search — absent for games.

**Analysis**: No game PID tracking implemented.

**VERDICT**: NOT implemented.

---

## 4. Launch

**Evidence**: SeatManager.cs
```csharp
public async Task LaunchAppInSeatAsync(Guid seatId, LaunchAppRequest request, CancellationToken ct)
{
    await _processInjector.LaunchInSessionAsync(
        seat.SessionId, seat.AccountName,
        request.ExecutablePath, request.Arguments, request.WorkingDirectory, ct);
}
```

**Analysis**: Game launched via ProcessInjector.

**VERDICT**: Implemented.

---

## 5. Stop

**Evidence**: SeatManager.cs (teardown)
```csharp
// Best-effort teardown
try { _vibepolloManager.Stop(seat); } catch { /* best effort */ }
```

**Analysis**: Games not explicitly killed on teardown.

**VERDICT**: NOT implemented (orphan possible).

---

## 6. Crash

**Evidence**: Codebase search — absent.

**Analysis**: No game crash detection.

**VERDICT**: NOT implemented.

---

## 7. Restart

**Evidence**: Codebase search — absent.

**Analysis**: No game restart.

**VERDICT**: NOT implemented.

---

## 8. Cleanup

**Evidence**: SeatManager.cs (teardown)
```csharp
// No explicit game cleanup
```

**Analysis**: Games not explicitly cleaned up.

**VERDICT**: NOT implemented (orphan possible).

---

## 9. Process → Seat Mapping

**Evidence**: Codebase search — absent.

**Analysis**: No PID → Seat mapping for games.

**VERDICT**: NOT implemented.

---

## Summary

| Aspect | Status | Evidence |
|--------|--------|----------|
| GameDefinition | Implemented | apps.json |
| GameProcess | Implemented | ProcessInjector |
| PID tracking | NOT implemented | Codebase search |
| Launch | Implemented | SeatManager.cs |
| Stop | NOT implemented | Codebase search |
| Crash detection | NOT implemented | Codebase search |
| Restart | NOT implemented | Codebase search |
| Cleanup | NOT implemented | Codebase search |
| PID → Seat mapping | NOT implemented | Codebase search |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Games defined in apps.json | VibepolloConfigBuilder | FACT |
| Games launched via ProcessInjector | SeatManager.cs | FACT |
| No game PID tracking | Codebase search | FACT (absent) |
| No game crash detection | Codebase search | FACT (absent) |
| No game cleanup | Codebase search | FACT (absent) |
