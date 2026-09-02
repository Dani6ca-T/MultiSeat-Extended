# Architecture Freeze

**Date**: 2026-08-30
**Status**: FROZEN — Implementation may proceed after ADR approval

---

## 1. System Purpose

**MultiSeat-Extended** is a **Windows-first multi-seat orchestration platform** for isolated interactive sessions with pluggable streaming providers.

### Technical Definition

MultiSeat-Extended enables multiple simultaneous Windows user sessions on a single host, each with:
- Independent virtual display
- Isolated audio endpoint
- Optional input device assignment
- Dedicated streaming server instance
- Managed game process lifecycle

### What It Is

- **Orchestration platform** — Coordinates Windows sessions, providers, and resources
- **Seat-centric** — Everything revolves around the Seat entity
- **Provider-agnostic** — Streaming providers are pluggable
- **Windows-native** — Uses standard Windows APIs (RDP, Terminal Services, DPAPI)

### What It Is NOT

- **Not a streaming server** — Vibepollo/Apollo/Sunshine handle streaming
- **Not a display driver** — SudoVDA handles virtual displays
- **Not an input driver** — HidHide handles input isolation
- **Not a game launcher** — Users launch games; MultiSeat manages processes
- **Not a Duo clone** — Different architecture (open-source, provider-agnostic)

---

## 2. Architectural Principles

### P1: Clean Architecture

Domain/Core must not depend on Windows implementation.

```
Core (Domain)
  ↓
Application (Use Cases)
  ↓
Infrastructure (Windows, Drivers, Providers)
```

### P2: Provider Independence

Streaming is a provider. MultiSeat must not be Vibepollo-specific.

### P3: Windows Isolation

Windows APIs behind abstraction boundaries. Core never touches Win32.

### P4: Credential Boundary

Credentials never cross public models, API wire, or logs.

### P5: Seat is Orchestration

Seat owns lifecycle of User, Session, Display, Audio, Input, Provider, Game.

### P6: Best-Effort Teardown

Teardown is best-effort per component, but Job Objects guarantee process cleanup.

### P7: Health Checks are Fast

5-second interval health checks with progressive backoff.

### P8: State Persists

Seat state persists to disk. Service restart does not lose seats.

### P9: Configuration is Generated

MultiSeat generates provider configuration. Users don't edit sunshine.conf.

### P10: Isolation is Default

Seats isolated by default (display, audio, input, process).

---

## 3. Frozen Decisions

The following decisions are FROZEN and require ADR to change:

| Decision | Rationale | ADR Required? |
|----------|-----------|---------------|
| Seat is aggregate root | Orchestration entity owns all resources | Yes |
| Provider is external process | GPLv3 cannot embed in MIT | Yes |
| SudoVDA for display | Proven, IddCx-based, MIT-compatible | Yes |
| HidHide for gamepad isolation | MIT, session jail works | Yes |
| TermWrap for RDP | MIT, auto offset discovery | Yes |
| PerSession audio | Windows RDP, no VAC needed | Yes |
| 5s health check interval | Proven in Helios and MultiSeat | Yes |
| Progressive backoff (30/60/120s) | Helios pattern | Yes |
| Job Objects for process cleanup | Standard Windows API | Yes |
| No custom drivers | Use existing OSS drivers | Yes |

---

## 4. Frozen Scope

### In Scope (MUST implement)

- Seat lifecycle (create, provision, start, stop, teardown)
- User management (Windows accounts)
- Session management (RDP loopback)
- Display management (SudoVDA)
- Audio management (PerSession RDP Remote Audio)
- Provider management (Vibepollo/Apollo)
- Process tracking (PID → Seat)
- Health monitoring (5s interval)
- Crash recovery (progressive backoff)
- API (ASP.NET Core)
- Dashboard (React)
- Security (DPAPI, ACL, API key)

### Out of Scope (MUST NOT implement)

- Custom display driver
- Custom input driver
- Custom audio driver
- Game RDP compatibility patching
- Steam multi-instance
- Anti-cheat bypass
- DRM bypass
- Custom streaming protocol
- Custom video codec

### Deferred (May implement later)

- HDR enablement (requires SudoVDA v0.5+ investigation)
- Microphone path (awaiting Vibepollo WebRTC mic)
- K/M session isolation (requires re-architecture)
- GPU selection (adapter name config)
- Metrics endpoint (Prometheus)
- Full seat re-provision on failure

---

## 5. Evidence Classification

| Classification | Meaning |
|----------------|---------|
| FACT | Verified in source code or public documentation |
| INFERENCE | Logical conclusion from multiple facts |
| DECISION | Architectural choice requiring ADR |
| OPEN QUESTION | Insufficient information to decide |

---

## 6. Research Foundation

This architecture is based on:

| Research Area | Documents |
|---------------|-----------|
| MultiSeat-Extended audit | 20+ research docs in docs/research/ |
| Vibepollo deep research | docs/research/external/vibepollo/ |
| Duo public research | docs/research/external/duo/ |
| Helios source analysis | docs/research/external/helios/ |
| TermWrap research | docs/research/external/termwrap/ |
| Windows ecosystem research | docs/research/external/windows/ |
| Capability matrices | docs/research/MASTER-CAPABILITY-MATRIX.md |
| Failure analysis | docs/research/FAILURE-MATRIX.md |
| Gap analysis | docs/research/FINAL-GAP-REPORT.md |

---

## 7. Freeze Qualities

This architecture is FROZEN when:

1. [x] All capabilities categorized (COMPLETE/PARTIAL/MISSING)
2. [x] All dependencies analyzed (license, maintenance, replacement)
3. [x] All failure modes documented
4. [x] All architectural invariants defined
5. [x] All ADR candidates identified
6. [x] All risks documented
7. [x] Implementation boundaries defined
8. [x] No production code changes

---

## 8. Next Steps

After this freeze:

1. **ADR approval** — Review and approve each ADR candidate
2. **Implementation roadmap** — Prioritize features
3. **Phase 1 implementation** — Quick wins (Job Objects, process tracking, backoff)
4. **Phase 2 implementation** — Core improvements (persistence, provider abstraction)
5. **Phase 3 implementation** — Features (HDR, Steam, full re-provision)

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| System purpose is well-defined | Research + analysis | FACT |
| Principles are derived from research | Research findings | FACT |
| Frozen decisions are evidence-based | Source code + public docs | FACT |
| Scope is realistic | Capability matrix | FACT |
| Research foundation is comprehensive | 50+ documents | FACT |
