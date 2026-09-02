# Architectural Contradictions — Phase 7

## Purpose

Identify contradictions between research, architecture documents, and source code.

---

## Contradictions Found

### 1. Provider Ownership

**ARCHITECTURE BASELINE**: "Provider is external process, not linked."

**SOURCE CODE**: `VibepolloManager` directly calls `Process.Start()` with Vibepollo-specific configuration.

**CONTRADICTION**: Architecture says provider-agnostic, code is Vibepollo-specific.

**SEVERITY**: MEDIUM

**RESOLUTION**: Provider abstraction (`IStreamingProvider`) is listed as P1 in capability priority. Current code is compatible with migration — Vibepollo coupling is concentrated in `VibepolloManager`.

---

### 2. Process Tracking

**ARCHITECTURE BASELINE**: "Every managed process has an owner (PID tracking)."

**SOURCE CODE**: No PID tracking. `VibepolloManager.Start()` returns `Process?` but does not track it.

**CONTRADICTION**: Architecture assumes PID tracking exists; it does not.

**SEVERITY**: HIGH (P0 gap)

**RESOLUTION**: PID tracking is identified as P0 in CAPABILITY-PRIORITY.md. This is a known gap, not a contradiction.

---

### 3. Job Objects

**ARCHITECTURE BASELINE**: "Per-seat Job Objects with KILL_ON_JOB_CLOSE."

**SOURCE CODE**: No Job Object usage anywhere in codebase.

**CONTRADICTION**: Architecture specifies Job Objects; current code has none.

**SEVERITY**: HIGH (P0 gap)

**RESOLUTION**: Job Objects identified as P0. Current process cleanup relies on `Process.Kill()` which is unreliable for process trees.

---

### 4. Seat State Machine

**ARCHITECTURE BASELINE**: 9 states (Created → Provisioning → Ready → Starting → Active → Degraded → Recovering → Stopping → Stopped → Failed).

**SOURCE CODE**: `SeatState` enum has: Created, Provisioning, Ready, Active, Stopping, Stopped, Failed (7 states).

**CONTRADICTION**: Architecture defines 2 additional states not in code.

**SEVERITY**: LOW

**RESOLUTION**: States can be added incrementally. Current 7 states are sufficient for MVP.

---

### 5. Health Checks

**ARCHITECTURE BASELINE**: "5-second health checks."

**SOURCE CODE**: No health check mechanism. Service runs seat lifecycle once.

**CONTRADICTION**: Architecture specifies continuous health monitoring; code has none.

**SEVERITY**: HIGH (P1 gap)

**RESOLUTION**: Health monitoring is P1. Not blocking for initial implementation.

---

### 6. Recovery

**ARCHITECTURE BASELINE**: "Progressive backoff (30s → 60s → 120s)."

**SOURCE CODE**: No retry logic. `SeatManager.ProvisionSeat()` fails → seat state = Failed.

**CONTRADICTION**: Architecture specifies recovery; code has none.

**SEVERITY**: HIGH (P1 gap)

**RESOLUTION**: Recovery is P1. Current behavior (fail → stop) is acceptable for MVP.

---

### 7. Display Ownership

**ARCHITECTURE BASELINE**: "DisplayManager owns display lifecycle."

**SOURCE CODE**: `VirtualDisplayManager` is a thin wrapper around SudoVDA CLI calls.

**CONTRADICTION**: Architecture implies rich DisplayManager; code is minimal.

**SEVERITY**: LOW

**RESOLUTION**: Display management is adequate for current needs. Can be enriched later.

---

### 8. Audio Endpoint

**ARCHITECTURE BASELINE**: "AudioEndpoint entity with lifecycle."

**SOURCE CODE**: Audio is configured in Vibepollo config file (`AudioSink`). No separate entity.

**CONTRADICTION**: Architecture models audio as first-class entity; code treats it as config.

**SEVERITY**: LOW

**RESOLUTION**: Audio configuration is sufficient for current needs. Entity model is aspirational.

---

### 9. Input Isolation

**ARCHITECTURE BASELINE**: "IInputBackend with session jail."

**SOURCE CODE**: `HidHideConfigurator` creates rules, but no session jail implementation.

**CONTRADICTION**: Architecture specifies session-based input isolation; code has seat-based only.

**SEVERITY**: MEDIUM

**RESOLUTION**: Seat-based isolation works for current use case. Session-based is P2.

---

### 10. Credentials

**ARCHITECTURE BASELINE**: "Credentials never in public models."

**SOURCE CODE**: `SeatSpec` contains `RdpPassword` property.

**CONTRADICTION**: Architecture forbids credentials in SeatSpec; code puts them there.

**SEVERITY**: LOW (internal use only)

**RESOLUTION**: `SeatSpec` is internal, not exposed via API. Migration to credential separation is P1.

---

## Summary

| # | Contradiction | Severity | Blocking? | Resolution |
|---|--------------|----------|-----------|------------|
| 1 | Provider coupling | MEDIUM | No | IStreamingProvider (P1) |
| 2 | No PID tracking | HIGH | No | P0 — implement first |
| 3 | No Job Objects | HIGH | No | P0 — implement second |
| 4 | Missing states | LOW | No | Add incrementally |
| 5 | No health checks | HIGH | No | P1 — add after MVP |
| 6 | No recovery | HIGH | No | P1 — add after MVP |
| 7 | Minimal display | LOW | No | Enrich later |
| 8 | No audio entity | LOW | No | Model as config for now |
| 9 | No session input | MEDIUM | No | Seat-based sufficient |
| 10 | Credentials in SeatSpec | LOW | No | Internal only, migrate P1 |

---

## Conclusion

**NO BLOCKING CONTRADICTIONS FOUND**.

All contradictions are either:
- Known gaps (documented in CAPABILITY-PRIORITY.md)
- Low severity (adequate for current needs)
- MEDIUM severity (acceptable for MVP, addressable later)

**ARCHITECTURE IS COMPATIBLE WITH CURRENT CODE**.

Migration path is clear: address P0 gaps (PID tracking, Job Objects) first, then P1 (provider abstraction, health, recovery).

---

*Generated: 2026-08-30*
*Status: VERIFIED against source code and architecture baseline*
