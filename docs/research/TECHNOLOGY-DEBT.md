# Technology Debt

**Date**: 2026-08-30
**Purpose**: Categorize known technical debt by severity

---

## Critical Debt

### 1. In-Memory Seat State

**Impact**: All seats lost on service restart

**Evidence**: ConcurrentDictionary in SeatManager

**Mitigation**: Persist seat state to disk

**Effort**: MEDIUM

### 2. No Process Tracking

**Impact**: Orphan processes possible, no PID → Seat mapping

**Evidence**: Codebase search — absent

**Mitigation**: ProcessTracker with PID dictionary

**Effort**: LOW

---

## High Debt

### 3. InputHookManager is No-Op

**Impact**: Keyboard/Mouse isolation not working

**Evidence**: CLAUDE.md "Known Constraints" — runs from Session 0

**Mitigation**: Re-architect to run inside seat session

**Effort**: HIGH

### 4. EnableHdr is No-Op

**Impact**: HDR not supported

**Evidence**: MultiSeatOptions.cs comment

**Mitigation**: Implement VidPN source mode rebuild

**Effort**: HIGH

### 5. No Steam Multi-Instance

**Impact**: Steam games cannot run in multiple seats

**Evidence**: Codebase search — absent

**Mitigation**: Research --userdatadir or process patching

**Effort**: HIGH

### 6. No Game RDP Compatibility

**Impact**: Some games refuse to run in RDP sessions

**Evidence**: Codebase search — absent

**Mitigation**: Application Compatibility Layer research

**Effort**: VERY HIGH

### 7. No Job Object Isolation

**Impact**: Orphan processes on teardown

**Evidence**: Codebase search — absent

**Mitigation**: Add Job Object to process launch

**Effort**: LOW

---

## Medium Debt

### 8. No Progressive Crash Backoff

**Impact**: Rapid restart loops possible

**Evidence**: MaxRestartAttempts = 3, no progressive delay

**Mitigation**: Implement Helios-style backoff (30/60/120s)

**Effort**: LOW

### 9. No Residual Process Adoption

**Impact**: Orphaned Vibepollo processes not adopted

**Evidence**: Codebase search — absent

**Mitigation**: WMI scan + adoption logic

**Effort**: MEDIUM

### 10. VibepolloManager is Tightly Coupled

**Impact**: Cannot switch providers without code changes

**Evidence**: Vibepollo-specific log parsing, config paths

**Mitigation**: IStreamingProvider interface

**Effort**: MEDIUM

### 11. No Game Process Tracking

**Impact**: Cannot detect game crashes or map games to seats

**Evidence**: Codebase search — absent

**Mitigation**: ProcessTracker with GameProcess entities

**Effort**: LOW

### 12. No Seat Re-Provision on Full Failure

**Impact**: Seat stays in Error state after full failure

**Evidence**: SeatManager — Error state is terminal

**Mitigation**: Auto re-provision pipeline

**Effort**: MEDIUM

---

## Low Debt

### 13. HidHide Session Jail Default OFF

**Impact**: Gamepad isolation not enabled by default

**Evidence**: EnableHidHideCloaking = false

**Mitigation**: Enable by default with safety checks

**Effort**: LOW

### 14. ViGEm Controller Legacy Path

**Impact**: Confusing option (EnableViGEmController) when Vibepollo handles natively

**Evidence**: EnableViGEmController option

**Mitigation**: Deprecate or remove option

**Effort**: LOW

### 15. No Metrics Endpoint

**Impact**: No Prometheus/metrics for monitoring

**Evidence**: Codebase search — absent

**Mitigation**: Add /metrics endpoint

**Effort**: LOW

### 16. No Conflicting Service Detection

**Impact**: Standalone Sunshine may conflict with MultiSeat

**Evidence**: Codebase search — absent

**Mitigation**: Detect SunshineService, ApolloService

**Effort**: LOW

---

## Debt Summary

| Severity | Count | Total Effort |
|----------|-------|-------------|
| Critical | 2 | MEDIUM + LOW |
| High | 5 | HIGH + VERY HIGH |
| Medium | 5 | MEDIUM + LOW |
| Low | 4 | LOW |
| **Total** | **16** | |

---

## Prioritized Remediation

### Quick Wins (LOW effort, HIGH impact)

1. **Job Objects** — Add to process launch (effort: LOW)
2. **Process tracking** — PID dictionary (effort: LOW)
3. **Progressive backoff** — Adopt Helios pattern (effort: LOW)
4. **Game crash detection** — Process exit monitoring (effort: LOW)

### Medium Term

5. **IStreamingProvider** — Provider abstraction (effort: MEDIUM)
6. **Seat state persistence** — Disk serialization (effort: MEDIUM)
7. **Residual process adoption** — WMI scan (effort: MEDIUM)

### Long Term

8. **HDR enablement** — VidPN rebuild (effort: HIGH)
9. **Steam multi-instance** — Research (effort: HIGH)
10. **K/M isolation** — Re-architect hooks (effort: HIGH)

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Seat state is in-memory | ConcurrentDictionary | VERIFIED |
| InputHookManager is no-op | CLAUDE.md | VERIFIED |
| EnableHdr is no-op | MultiSeatOptions.cs | VERIFIED |
| No process tracking | Codebase search | VERIFIED (absent) |
| No Job Objects | Codebase search | VERIFIED (absent) |
| No progressive backoff | Constants.cs (MaxRestartAttempts only) | VERIFIED |
| No Steam isolation | Codebase search | VERIFIED (absent) |
| No metrics endpoint | Codebase search | VERIFIED (absent) |
