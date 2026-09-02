# ADR Candidates

**Date**: 2026-08-30
**Status**: CANDIDATES (not yet approved)

---

## Purpose

List architectural decisions requiring ADR approval.

---

## ADR Candidates

### ADR-001: Provider Abstraction

**Decision**: Create IStreamingProvider interface

**Status**: DECISION

**Evidence**: VibepolloManager is tightly coupled. Helios supports multiple providers.

**Alternatives**:
1. Keep VibepolloManager coupled
2. Create IStreamingProvider interface

**Consequences**:
- Enables provider switching (Apollo, Sunshine)
- Requires adapter pattern implementation
- Medium effort

---

### ADR-002: Process Ownership

**Decision**: Every managed process has an owner Seat

**Status**: DECISION

**Evidence**: Orphan processes are a known problem. Helios uses PID tracking.

**Alternatives**:
1. No process tracking (current)
2. PID → Seat mapping

**Consequences**:
- Enables guaranteed cleanup
- Enables orphan adoption
- Low effort

---

### ADR-003: Job Objects

**Decision**: Use Job Objects for process cleanup

**Status**: DECISION

**Evidence**: Standard Windows API. Helios uses CreateJobObject.

**Alternatives**:
1. Best-effort Kill (current)
2. Job Object isolation

**Consequences**:
- Guarantees process cleanup
- Low effort

---

### ADR-004: Display Backend

**Decision**: Keep SudoVDA as display backend

**Status**: DECISION

**Evidence**: Already integrated, proven, IddCx-based.

**Alternatives**:
1. Keep SudoVDA
2. Switch to Virtual-Display-Driver
3. Build custom IDD

**Consequences**:
- No changes needed
- License investigation needed

---

### ADR-005: Input Backend

**Decision**: Keep HidHide for gamepad isolation

**Status**: DECISION

**Evidence**: MIT license, session jail works.

**Alternatives**:
1. Keep HidHide
2. Switch to libvirtualhid
3. Build custom UMDF driver

**Consequences**:
- No changes needed
- Enable by default (target)

---

### ADR-006: Audio Backend

**Decision**: Keep PerSession (RDP Remote Audio)

**Status**: DECISION

**Evidence**: True isolation, no VAC needed.

**Alternatives**:
1. Keep PerSession
2. Revive SharedHost (VAC)
3. Build custom audio driver

**Consequences**:
- No microphone path (accepted limitation)
- No changes needed

---

### ADR-007: Session Lifecycle

**Decision**: RDP loopback via mstsc

**Status**: DECISION

**Evidence**: Already implemented, proven.

**Alternatives**:
1. RDP loopback (current)
2. Custom session creation
3. Windows Terminal Services API

**Consequences**:
- No changes needed
- mstsc dependency

---

### ADR-008: Credential Transport

**Decision**: DPAPI for credential encryption

**Status**: DECISION

**Evidence**: Windows built-in, already implemented.

**Alternatives**:
1. DPAPI (current)
2. Custom encryption
3. External vault

**Consequences**:
- No changes needed
- Machine-bound encryption

---

### ADR-009: Provider Bootstrap

**Decision**: MultiSeat generates provider configuration

**Status**: DECISION

**Evidence**: VibepolloConfigBuilder generates sunshine.conf.

**Alternatives**:
1. MultiSeat generates config (current)
2. Provider self-configures
3. User edits config

**Consequences**:
- Configuration correctness guaranteed
- Provider cannot drift

---

### ADR-010: Recovery Policy

**Decision**: Progressive backoff (30/60/120s)

**Status**: DECISION

**Evidence**: Helios ProcessManager pattern.

**Alternatives**:
1. MaxRestartAttempts only (current)
2. Progressive backoff

**Consequences**:
- Prevents restart loops
- Low effort

---

### ADR-011: Seat State Persistence

**Decision**: Persist seat state to disk

**Status**: DECISION

**Evidence**: In-memory state lost on restart.

**Alternatives**:
1. In-memory (current)
2. Disk persistence

**Consequences**:
- Survives service restart
- Medium effort

---

### ADR-012: Process Tracking

**Decision**: Add PID → Seat mapping

**Status**: DECISION

**Evidence**: Orphan processes are a problem.

**Alternatives**:
1. No tracking (current)
2. PID dictionary

**Consequences**:
- Enables orphan detection
- Low effort

---

## Approval Status

| ADR | Status | Approved? |
|-----|--------|-----------|
| ADR-001 | DECISION | Pending |
| ADR-002 | DECISION | Pending |
| ADR-003 | DECISION | Pending |
| ADR-004 | DECISION | Pending |
| ADR-005 | DECISION | Pending |
| ADR-006 | DECISION | Pending |
| ADR-007 | DECISION | Pending |
| ADR-008 | DECISION | Pending |
| ADR-009 | DECISION | Pending |
| ADR-010 | DECISION | Pending |
| ADR-011 | DECISION | Pending |
| ADR-012 | DECISION | Pending |
