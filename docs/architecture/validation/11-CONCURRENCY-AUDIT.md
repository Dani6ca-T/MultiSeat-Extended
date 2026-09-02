# Concurrency Audit

**Date**: 2026-08-30
**Status**: AUDITED

---

## Purpose

Audit race conditions and synchronization.

---

## 1. Parallel Seat Start

**Evidence**: SeatManager.cs
```csharp
public async Task<SeatInfo> ProvisionSeatAsync(SeatRequest request, CancellationToken ct)
{
    if (ActiveSeatCount >= _options.MaxSeats)
        throw new InvalidOperationException(...);
    // ...
}
```

**Analysis**: No lock on provisioning. Multiple seats can provision concurrently.

**RISK**: Port allocation race, session creation race.

**MITIGATION**: PortAllocator uses ConcurrentDictionary.

---

## 2. Parallel Seat Stop

**Evidence**: SeatManager.cs
```csharp
public async Task TeardownSeatAsync(Guid seatId, CancellationToken ct)
{
    if (!_seats.TryRemove(seatId, out var seat))
        return;
    // ...
}
```

**Analysis**: TryRemove is atomic. No race on teardown.

**RISK**: Low.

---

## 3. Start + Stop Race

**Evidence**: SeatManager.cs

**Analysis**: Start and stop can race on same seat.

**RISK**: Medium. Seat state can be inconsistent.

**MITIGATION**: State machine guards.

---

## 4. Provider Crash During Start

**Evidence**: VibepolloManager.cs

**Analysis**: Provider can crash during provisioning.

**RISK**: Medium. Seat in inconsistent state.

**MITIGATION**: Best-effort teardown on error.

---

## 5. Game Crash During Stop

**Evidence**: SeatManager.cs

**Analysis**: Game can crash during teardown.

**RISK**: Low. Best-effort cleanup.

---

## 6. Session Disconnect During Provisioning

**Evidence**: SessionLauncher.cs

**Analysis**: Session can disconnect during provisioning.

**RISK**: Medium. Display detection fails.

**MITIGATION**: TryLateDisplayDetectionAsync.

---

## 7. Windows Reboot During Provisioning

**Evidence**: In-memory state

**Analysis**: Reboot loses all state.

**RISK**: High. All seats lost.

**MITIGATION**: State persistence needed.

---

## 8. Duplicate Start

**Evidence**: SeatManager.cs

**Analysis**: Duplicate start prevented by state check.

**RISK**: Low.

---

## 9. Duplicate Stop

**Evidence**: SeatManager.cs

**Analysis**: Duplicate stop prevented by TryRemove.

**RISK**: Low.

---

## 10. Restart While Stopping

**Evidence**: SeatManager.cs

**Analysis**: Restart during stop can race.

**RISK**: Medium.

**MITIGATION**: State machine guards.

---

## Locks/Semaphores

| Lock | Location | Purpose |
|------|----------|---------|
| _rdpFileGate | SessionLauncher | Serialize RDP file write |
| _commandLock | SpawnerWorker (Helios) | Serialize pipe commands |

---

## Summary

| Scenario | Risk | Mitigation |
|----------|------|------------|
| Parallel seat start | Medium | PortAllocator |
| Parallel seat stop | Low | TryRemove |
| Start + stop race | Medium | State machine |
| Provider crash during start | Medium | Best-effort teardown |
| Game crash during stop | Low | Best-effort cleanup |
| Session disconnect during provisioning | Medium | Late detection |
| Windows reboot during provisioning | High | State persistence |
| Duplicate start | Low | State check |
| Duplicate stop | Low | TryRemove |
| Restart while stopping | Medium | State machine |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| No lock on provisioning | SeatManager.cs | FACT |
| TryRemove is atomic | ConcurrentDictionary | FACT |
| State machine guards | SeatStatus enum | FACT |
| Best-effort teardown | TeardownSeatInternalAsync | FACT |
| In-memory state | ConcurrentDictionary | FACT |
