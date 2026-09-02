# Current vs Target State

**Date**: 2026-08-30
**Purpose**: Compare current MultiSeat-Extended implementation against target capabilities

---

## Status Legend

- **COMPLETE** — Fully implemented and working
- **GOOD** — Working well, minor improvements possible
- **PARTIAL** — Implemented but limited
- **WEAK** — Implementation exists but has significant gaps
- **MISSING** — Not implemented at all
- **EXTERNAL** — Delegated to external component
- **UNKNOWN** — Cannot verify

---

## Users

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Account creation | AccountManager | GOOD | Windows LSA | Same | None |
| Account deletion | AccountManager | GOOD | Windows LSA | Same | None |
| Group membership | ApplySeatGroupMembership | GOOD | Windows net localgroup | Same | None |
| Credential storage | DPAPI | COMPLETE | Windows DPAPI | Same | None |

## Sessions

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Session creation | SessionLauncher (RDP loopback) | COMPLETE | TermWrap, mstsc | Same | None |
| Session monitoring | SessionHealthCheck (5s) | COMPLETE | None | Same | None |
| Session disconnect | DisconnectSession | COMPLETE | None | Same | None |
| Session logoff | LogoffSession | COMPLETE | None | Same | None |
| Session reconnect | mstsc reconnect | PARTIAL | mstsc | Seamless reconnect | Gap: no automatic reconnect after sleep |

## RDP

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Concurrent sessions | TermWrap v0.6 | COMPLETE | TermWrap (MIT) | Same | None |
| RDP loopback | SessionLauncher | COMPLETE | None | Same | None |
| termsrv compatibility | Auto offset discovery | COMPLETE | TermWrap | Same | None |

## Seat

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Seat entity | SeatInfo model | COMPLETE | None | Same | None |
| Seat lifecycle | 9-step pipeline | COMPLETE | None | Same | None |
| Seat state | SeatStatus enum | COMPLETE | None | Same | None |
| Seat configuration | SeatRequest + SeatPreset | COMPLETE | None | Same | None |
| Seat persistence | In-memory ConcurrentDictionary | WEAK | None | Disk persistence | Gap: seats lost on service restart |
| Seat recovery | Vibepollo restart only | PARTIAL | None | Full re-provision | Gap: no session/display re-creation |
| Seat health | SessionHealthCheck | COMPLETE | None | Same | None |

## Display

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Virtual display | SudoVDA | COMPLETE | SudoVDA driver | Same | None |
| Display assignment | UUID from Vibepollo log | GOOD | Vibepollo IPC | Same | None |
| Display lifecycle | Create/Destroy in pipeline | COMPLETE | SudoVDA | Same | None |
| Resolution | RdpGeometry | COMPLETE | None | Same | None |
| Refresh rate | --set-display-hz | COMPLETE | None | Same | None |
| HDR | EnableHdr (no-op) | MISSING | SudoVDA HDR EDID | HDR streaming | Gap: no HDR enablement path |
| 10-bit | Not implemented | MISSING | SudoVDA | HDR 10-bit | Gap: no 10-bit support |
| Multi-monitor | Single display per seat | MISSING | SudoVDA | Multi-monitor | Gap: single display only |
| GPU selection | Not implemented | MISSING | None | Adapter name config | Gap: no GPU selection |

## Audio

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Playback isolation | PerSession RDP Remote Audio | COMPLETE | Windows RDP | Same | None |
| Microphone | Not implemented | MISSING | RDP limitation | Vibepollo WebRTC mic | Gap: no mic path (awaiting Vibepollo 1.19.x) |
| Audio device assignment | N/A (PerSession) | COMPLETE | Windows RDP | Same | None |

## Input

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Gamepad forwarding | Vibepollo native | COMPLETE | Vibepollo | Same | None |
| Gamepad isolation | HidHide session jail | PARTIAL | HidHide (MIT) | Reliable isolation | Gap: undocumented feature, default OFF |
| Keyboard/Mouse isolation | InputHookManager (no-op) | WEAK | MultiSeatInputHook.dll | Session-scoped hooks | Gap: hooks run from Session 0 |
| ViGEm controller | Optional | PARTIAL | ViGEmBus (MIT) | Deprecated path | Gap: Vibepollo handles natively now |

## Game

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Game launch | ProcessInjector | COMPLETE | None | Same | None |
| Process tracking | Not implemented | MISSING | None | PID → Seat mapping | Gap: no tracking |
| Game crash detection | Not implemented | MISSING | None | Crash detection | Gap: no detection |
| Game RDP compatibility | Not implemented | MISSING | None | App Compat Layer | Gap: games refuse RDP |
| Game cleanup on teardown | Best-effort Kill | WEAK | None | Job Object cleanup | Gap: orphan processes possible |

## Steam

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Steam library sharing | SharedGameLibrary (icacls) | COMPLETE | None | Same | None |
| Steam multi-instance | Not implemented | MISSING | None | Steam isolation | Gap: Steam mutex prevents multi-instance |
| Steam process isolation | Not implemented | MISSING | None | Per-seat Steam | Gap: shared Steam client |

## Streaming

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Streaming server | Vibepollo | COMPLETE | Vibepollo (GPLv3) | Same or alternative | None |
| Provider abstraction | VibepolloManager (coupled) | WEAK | None | IStreamingProvider | Gap: no abstraction |
| Provider lifecycle | Start/Stop/Restart | COMPLETE | None | Same | None |
| Provider configuration | VibepolloConfigBuilder | COMPLETE | None | Same | None |
| Provider health | VibepolloServerQuery | COMPLETE | None | Same | None |
| Port allocation | PortAllocator (30-port blocks) | COMPLETE | None | Same | None |
| Display targeting | UUID from log | COMPLETE | Vibepollo | Same | None |

## Process

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Process launch | ProcessInjector (CreateProcessAsUser) | COMPLETE | None | Same | None |
| Token management | SessionLauncher | COMPLETE | None | Same | None |
| Process tracking | Not implemented | MISSING | None | PID tracking | Gap: no tracking |
| Job Objects | Not implemented | MISSING | None | Job Object isolation | Gap: no process tree cleanup |
| Residual process adoption | Not implemented | MISSING | None | WMI adoption | Gap: orphan processes |

## Recovery

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| Crash detection | SessionHealthCheck | COMPLETE | None | Same | None |
| Vibepollo restart | Auto-restart | GOOD | None | Same | None |
| Progressive backoff | MaxRestartAttempts = 3 | PARTIAL | None | Progressive (30/60/120s) | Gap: no progressive backoff |
| Display re-detection | TryLateDisplayDetectionAsync | GOOD | None | Same | None |
| Full seat re-provision | Not implemented | MISSING | None | Re-provision pipeline | Gap: no full recovery |

## Security

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| DPAPI | Credential encryption | COMPLETE | Windows DPAPI | Same | None |
| ACL | File permissions | COMPLETE | Windows ACL | Same | None |
| API key | Authentication middleware | COMPLETE | None | Same | None |
| Seat administrator | GrantSeatAdministrator option | COMPLETE | None | Same (default OFF) | None |

## Management

| Capability | Current | Quality | External Dep | Target | Gap |
|------------|---------|---------|-------------|--------|-----|
| REST API | ASP.NET Core | COMPLETE | None | Same | None |
| Web UI | React dashboard | COMPLETE | None | Same | None |
| Configuration | appsettings.json + presets | COMPLETE | None | Same | None |
| Diagnostics | HidHideInspector, LogFilter | COMPLETE | None | Same | None |
| Metrics | Not implemented | MISSING | None | Prometheus endpoint | Gap: no metrics |

---

## Gap Summary

| Category | COMPLETE | GOOD | PARTIAL | WEAK | MISSING |
|----------|----------|------|---------|------|---------|
| Users | 4 | 0 | 0 | 0 | 0 |
| Sessions | 3 | 0 | 1 | 0 | 0 |
| RDP | 3 | 0 | 0 | 0 | 0 |
| Seat | 4 | 0 | 1 | 1 | 0 |
| Display | 4 | 1 | 0 | 0 | 3 |
| Audio | 2 | 0 | 0 | 0 | 1 |
| Input | 1 | 0 | 2 | 1 | 0 |
| Game | 1 | 0 | 0 | 1 | 3 |
| Steam | 1 | 0 | 0 | 0 | 2 |
| Streaming | 6 | 0 | 0 | 1 | 0 |
| Process | 2 | 0 | 0 | 0 | 3 |
| Recovery | 2 | 1 | 1 | 0 | 1 |
| Security | 3 | 0 | 0 | 0 | 0 |
| Management | 4 | 0 | 0 | 0 | 1 |
| **Total** | **40** | **2** | **5** | **4** | **14** |

**14 MISSING capabilities** represent the primary development roadmap.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| EnableHdr is no-op | MultiSeatOptions.cs comment | VERIFIED |
| InputHookManager is no-op | CLAUDE.md "Known Constraints" | VERIFIED |
| Seat persistence is in-memory | ConcurrentDictionary in SeatManager | VERIFIED |
| No game process tracking | Codebase search | VERIFIED (absent) |
| No Steam isolation | Codebase search | VERIFIED (absent) |
| No Job Objects | Codebase search | VERIFIED (absent) |
| MaxRestartAttempts = 3 | Constants.cs | VERIFIED |
| No progressive backoff | SeatManager code | VERIFIED |
| No metrics endpoint | Codebase search | VERIFIED (absent) |
