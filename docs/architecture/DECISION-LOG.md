# Decision Log

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Record architectural decisions that are sufficiently proven by research.

---

## Decisions

### D001: Seat is Aggregate Root

**Decision**: Seat is the aggregate root for the multi-seat domain.

**Status**: DECISION

**Evidence**: SeatManager orchestrates all subsystems. Seat owns lifecycle.

**Alternatives**:
1. Seat as aggregate root (chosen)
2. Session as aggregate root
3. Provider as aggregate root

**Consequences**:
- All operations go through SeatManager
- Seat owns all resources
- Clear consistency boundary

---

### D002: Provider is External Process

**Decision**: Streaming providers run as external processes, not embedded.

**Status**: DECISION

**Evidence**: GPLv3 cannot embed in MIT. VibepolloManager launches external process.

**Alternatives**:
1. External process (chosen)
2. Embedded library
3. In-process

**Consequences**:
- License compatibility maintained
- Process isolation
- IPC overhead (acceptable)

---

### D003: SudoVDA for Display

**Decision**: Use SudoVDA for virtual display.

**Status**: DECISION

**Evidence**: Already integrated, proven, IddCx-based.

**Alternatives**:
1. SudoVDA (chosen)
2. Virtual-Display-Driver
3. Custom IDD

**Consequences**:
- No changes needed
- License investigation needed

---

### D004: HidHide for Input Isolation

**Decision**: Use HidHide session jail for gamepad isolation.

**Status**: DECISION

**Evidence**: MIT license, session jail works.

**Alternatives**:
1. HidHide (chosen)
2. libvirtualhid
3. Custom UMDF driver

**Consequences**:
- No changes needed
- Enable by default (target)

---

### D005: PerSession Audio

**Decision**: Use Windows RDP per-session audio.

**Status**: DECISION

**Evidence**: True isolation, no VAC needed.

**Alternatives**:
1. PerSession (chosen)
2. SharedHost (VAC)
3. Custom audio driver

**Consequences**:
- No microphone path (accepted)
- No changes needed

---

### D006: 5s Health Check

**Decision**: Health checks run every 5 seconds.

**Status**: DECISION

**Evidence**: Proven in Helios and MultiSeat.

**Alternatives**:
1. 5s interval (chosen)
2. 1s interval
3. 10s interval

**Consequences**:
- Fast detection
- Acceptable overhead

---

### D007: Progressive Backoff

**Decision**: Crash recovery uses progressive backoff (30/60/120s).

**Status**: DECISION

**Evidence**: Helios ProcessManager pattern.

**Alternatives**:
1. Progressive backoff (chosen)
2. Fixed interval
3. Exponential backoff

**Consequences**:
- Prevents restart loops
- Predictable behavior

---

### D008: Job Objects for Cleanup

**Decision**: Use Job Objects for guaranteed process cleanup.

**Status**: DECISION

**Evidence**: Standard Windows API.

**Alternatives**:
1. Job Objects (chosen)
2. Best-effort Kill
3. Process tree enumeration

**Consequences**:
- Guaranteed cleanup
- Low effort

---

### D009: Configuration Generated

**Decision**: MultiSeat generates provider configuration.

**Status**: DECISION

**Evidence**: VibepolloConfigBuilder generates sunshine.conf.

**Alternatives**:
1. Generated (chosen)
2. User-edited
3. Provider self-configured

**Consequences**:
- Configuration correctness
- Provider cannot drift

---

### D010: Credentials Protected

**Decision**: Credentials never cross public models.

**Status**: DECISION

**Evidence**: Security architecture.

**Alternatives**:
1. Protected (chosen)
2. In config files
3. In API wire

**Consequences**:
- No credential exposure
- DPAPI for storage

---

## Decision Summary

| Decision | Status | Evidence |
|----------|--------|----------|
| Seat is aggregate root | DECISION | SeatManager.cs |
| Provider is external process | DECISION | GPLv3 analysis |
| SudoVDA for display | DECISION | VirtualDisplayManager |
| HidHide for input | DECISION | HidHideConfigurator |
| PerSession audio | DECISION | MultiSeatOptions.cs |
| 5s health check | DECISION | SessionHealthCheck |
| Progressive backoff | DECISION | Helios pattern |
| Job Objects | DECISION | Windows API |
| Configuration generated | DECISION | VibepolloConfigBuilder |
| Credentials protected | DECISION | Security architecture |
