# Capability Priority

**Date**: 2026-08-30
**Purpose**: Prioritize missing capabilities by user value, technical difficulty, and impact

---

## Priority Levels

- **P0** — Foundational (must have for basic operation)
- **P1** — Major capability (significant user value)
- **P2** — Important (noticeable improvement)
- **P3** — Enhancement (nice to have)
- **P4** — Experimental (research needed)

---

## P0 — Foundational

### 1. Process Tracking

| Aspect | Details |
|--------|---------|
| User value | HIGH (crash detection, cleanup) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

### 2. Job Object Isolation

| Aspect | Details |
|--------|---------|
| User value | HIGH (guaranteed cleanup) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

### 3. Progressive Crash Backoff

| Aspect | Details |
|--------|---------|
| User value | HIGH (prevents restart loops) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

---

## P1 — Major Capability

### 4. Seat State Persistence

| Aspect | Details |
|--------|---------|
| User value | HIGH (survives service restart) |
| Technical difficulty | MEDIUM |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | MEDIUM |

### 5. IStreamingProvider Abstraction

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (provider flexibility) |
| Technical difficulty | MEDIUM |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | MEDIUM |

### 6. Residual Process Adoption

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (orphan recovery) |
| Technical difficulty | MEDIUM |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

### 7. Game Crash Detection

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (game reliability) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

---

## P2 — Important

### 8. HDR Enablement

| Aspect | Details |
|--------|---------|
| User value | HIGH (visual quality) |
| Technical difficulty | HIGH |
| Windows risk | MEDIUM |
| Driver requirement | SudoVDA v0.5+ |
| External dependency | SudoVDA HDR EDID |
| Maintenance cost | MEDIUM |
| Architecture impact | LOW |

### 9. Full Seat Re-Provision

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (recovery from full failure) |
| Technical difficulty | MEDIUM |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

### 10. GPU Selection

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (multi-GPU systems) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

---

## P3 — Enhancement

### 11. Keyboard/Mouse Isolation

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (input isolation) |
| Technical difficulty | HIGH |
| Windows risk | MEDIUM |
| Driver requirement | Potentially UMDF |
| External dependency | HidHide or custom driver |
| Maintenance cost | HIGH |
| Architecture impact | MEDIUM |

### 12. Steam Multi-Instance

| Aspect | Details |
|--------|---------|
| User value | HIGH (Steam games in multiple seats) |
| Technical difficulty | HIGH |
| Windows risk | MEDIUM |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | HIGH |
| Architecture impact | MEDIUM |

### 13. Microphone Path

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (voice chat) |
| Technical difficulty | MEDIUM |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | Vibepollo WebRTC mic |
| Maintenance cost | LOW |
| Architecture impact | LOW |

### 14. Metrics Endpoint

| Aspect | Details |
|--------|---------|
| User value | LOW (monitoring) |
| Technical difficulty | LOW |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | LOW |
| Architecture impact | LOW |

---

## P4 — Experimental

### 15. Game RDP Compatibility

| Aspect | Details |
|--------|---------|
| User value | HIGH (game compatibility) |
| Technical difficulty | VERY HIGH |
| Windows risk | HIGH |
| Driver requirement | No |
| External dependency | No |
| Maintenance cost | VERY HIGH |
| Architecture impact | HIGH |

### 16. UMDF Input Driver

| Aspect | Details |
|--------|---------|
| User value | HIGH (input isolation) |
| Technical difficulty | VERY HIGH |
| Windows risk | HIGH |
| Driver requirement | Yes (UMDF) |
| External dependency | No |
| Maintenance cost | VERY HIGH |
| Architecture impact | HIGH |

### 17. NVIDIA Smooth Motion

| Aspect | Details |
|--------|---------|
| User value | MEDIUM (frame generation) |
| Technical difficulty | HIGH |
| Windows risk | LOW |
| Driver requirement | No |
| External dependency | Vibepollo feature |
| Maintenance cost | LOW |
| Architecture impact | LOW |

---

## Summary

| Priority | Count | Total Effort |
|----------|-------|-------------|
| P0 | 3 | LOW |
| P1 | 4 | MEDIUM |
| P2 | 3 | MEDIUM-HIGH |
| P3 | 4 | MEDIUM-HIGH |
| P4 | 3 | VERY HIGH |
| **Total** | **17** | |

---

## Recommended Implementation Order

### Phase 1: Quick Wins (P0)

1. Process tracking (PID dictionary)
2. Job Object isolation
3. Progressive crash backoff

### Phase 2: Core Improvements (P1)

4. Seat state persistence
5. IStreamingProvider abstraction
6. Residual process adoption
7. Game crash detection

### Phase 3: Features (P2)

8. HDR enablement
9. Full seat re-provision
10. GPU selection

### Phase 4: Advanced (P3)

11. K/M isolation (if needed)
12. Steam multi-instance (if feasible)
13. Microphone path (wait for Vibepollo)
14. Metrics endpoint

### Phase 5: Research (P4)

15. Game RDP compatibility
16. UMDF input driver
17. NVIDIA Smooth Motion

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Process tracking is LOW difficulty | Standard Windows API | VERIFIED |
| Job Objects are LOW difficulty | Standard Windows API | VERIFIED |
| HDR requires HIGH effort | VidPN rebuild complexity | VERIFIED |
| Steam multi-instance is HIGH difficulty | Mutex + IPC + userdata | VERIFIED |
| UMDF driver is VERY HIGH difficulty | Driver development | VERIFIED |
