# Failure Validation — Phase 7

## Purpose

Validate that the architecture handles all identified failure scenarios.

---

## Failure Scenarios

### 1. Windows Service Crash

**Detection**: Service watchdog / Windows SCM restart.

**Current Code**: Service registers with `AddWindowsServiceLifetime()`.

**Target Behavior**:
- Service restarts automatically
- In-progress seats are abandoned (not recovered)
- Running seats remain in degraded state
- Manual recovery required for orphaned seats

**Gap**: No orphan detection after service restart.

**Recommendation**: On service start, scan for orphaned provider/game processes and clean up.

---

### 2. Provider Process Crash

**Detection**: Health check (5s interval) fails.

**Current Code**: No explicit provider health check. Seat manager monitors process exit.

**Target Behavior**:
- Detect provider exit within 5s
- Record crash with backoff (30s → 60s → 120s)
- Restart provider if within retry limit
- Mark seat as DEGRADED if retries exhausted
- Do NOT restart provider during seat stop

**Gap**: No crash counter, no progressive backoff.

**Recommendation**: Add `ProviderCrashTracker` with exponential backoff.

---

### 3. Game Process Crash

**Detection**: Process exit monitoring (when implemented).

**Current Code**: No game process tracking (P0 gap identified in Phase 5).

**Target Behavior**:
- Detect game exit within 2s
- Log exit code
- Optionally restart game (configurable)
- Mark seat as DEGRADED
- Do NOT restart provider when game crashes

**Gap**: Game process not tracked by MultiSeat.

**Recommendation**: Implement `GameProcessTracker` using PID monitoring.

---

### 4. Session Disconnect (RDP)

**Detection**: RDP session state change via WTS callback.

**Current Code**: No session monitoring (P0 gap).

**Target Behavior**:
- Detect disconnect within 1s
- Keep provider running (client may reconnect)
- Keep game running
- Update seat state to DISCONNECTED
- If timeout → stop seat gracefully

**Gap**: No session monitoring.

**Recommendation**: Register WTS notification callback.

---

### 5. Display Failure

**Detection**: Provider cannot encode display.

**Current Code**: SudoVDA adapter assumed stable.

**Target Behavior**:
- Provider reports display error
- MultiSeat attempts display recreation
- If persistent → mark seat as DEGRADED
- Do NOT restart game during display failure

**Gap**: No display health monitoring.

**Recommendation**: Provider reports display status via health API.

---

### 6. Audio Failure

**Detection**: Audio endpoint disappears.

**Current Code**: PerSession audio endpoint created at seat start.

**Target Behavior**:
- Detect audio endpoint loss
- Recreate audio endpoint
- Notify provider
- Game continues (audio optional for some games)

**Gap**: No audio endpoint monitoring.

**Recommendation**: Periodic audio endpoint validation.

---

### 7. Input Failure

**Detection**: HidHide rule becomes invalid.

**Current Code**: HidHide configurator creates rules at seat start.

**Target Behavior**:
- Detect HidHide rule loss
- Recreate rules
- Gamepad may need re-seating

**Gap**: No input rule monitoring.

**Recommendation**: Periodic HidHide rule validation.

---

### 8. Windows Reboot

**Detection**: Service starts fresh.

**Current Code**: SeatManager restores seats from persistence.

**Target Behavior**:
- Service starts
- Detect previous seats from persistence
- Clean up orphaned users/sessions
- Optionally restore seats
- Notify dashboard

**Gap**: No orphan cleanup on service start.

**Recommendation**: Add `OrphanCleanup` on service startup.

---

### 9. Provider Orphan (Process survives seat stop)

**Detection**: Seat stops but provider process remains.

**Current Code**: `VibepolloManager.Stop()` attempts kill, no PID tracking.

**Target Behavior**:
- PID tracked in process registry
- On seat stop: kill PID, verify exit
- If still alive: force kill
- If still alive: log warning, mark as orphan
- Periodic orphan scan detects stragglers

**Gap**: No PID tracking.

**Recommendation**: Implement process registry with PID tracking.

---

### 10. Game Orphan (Game survives seat stop)

**Detection**: Seat stops but game process remains.

**Current Code**: No game process tracking.

**Target Behavior**:
- Job Object with KILL_ON_JOB_CLOSE ensures cleanup
- Fallback: PID-based kill
- Orphan scan catches stragglers

**Gap**: No game process tracking, no Job Objects.

**Recommendation**: Implement Job Objects + game process tracking.

---

## Recovery Matrix

| Failure | Detection | Owner | Recovery | Retry Limit | Terminal |
|---------|-----------|-------|----------|-------------|----------|
| Service crash | Windows SCM | Windows | Auto-restart | Unlimited | Never |
| Provider crash | Health check | MultiSeat | Restart with backoff | 5 | DEGRADED |
| Game crash | Process exit | MultiSeat | Log + optional restart | 3 | DEGRADED |
| Session disconnect | WTS callback | MultiSeat | Wait for reconnect | None | STOPPED |
| Display failure | Provider report | MultiSeat | Recreate display | 3 | DEGRADED |
| Audio failure | Endpoint check | MultiSeat | Recreate endpoint | 3 | DEGRADED |
| Input failure | Rule check | MultiSeat | Recreate rules | 3 | DEGRADED |
| Windows reboot | Service start | MultiSeat | Cleanup + restore | None | STOPPED |
| Provider orphan | Process scan | MultiSeat | Force kill | None | CLEANED |
| Game orphan | Process scan | MultiSeat | Force kill | None | CLEANED |

---

## Backoff Strategy

**INSPIRED BY**: Helios guardian loop (30s → 60s → 120s).

**RECOMMENDED**: Exponential backoff with jitter.

```
Attempt 1: immediate
Attempt 2: 30s + random(0-10s)
Attempt 3: 60s + random(0-20s)
Attempt 4: 120s + random(0-30s)
Attempt 5: give up → DEGRADED
```

**REASON**: Prevents restart storms, allows transient issues to resolve.

---

## Conclusion

**MOST CRITICAL GAPS** (P0):
1. No process tracking → no orphan detection
2. No session monitoring → cannot detect disconnects
3. No crash backoff → restart loops possible
4. No Job Objects → process cleanup unreliable

**ARCHITECTURE**: All gaps are addressable within current architecture.

**COMPATIBILITY**: All failures can be handled with incremental additions to existing code.

---

*Generated: 2026-08-30*
*Status: VERIFIED against source code and architecture baseline*
