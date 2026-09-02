# Build / Buy / Delegate

**Date**: 2026-08-30
**Purpose**: Decide for each subsystem whether to build, use open source, adapt, delegate, or avoid

---

## Decisions

### Users

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Account management | BUILD | Simple Windows API calls |
| Credential storage | USE OPEN SOURCE | DPAPI (Windows built-in) |
| Group membership | BUILD | Simple net localgroup calls |

### Sessions

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| RDP loopback | BUILD | Simple mstsc wrapper |
| Session monitoring | BUILD | WTS APIs |
| TermWrap | USE OPEN SOURCE | MIT license, proven |

### Display

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Virtual display | USE OPEN SOURCE | SudoVDA (IddCx driver) |
| Display isolation | BUILD | SudoVDA primary + RDP shrunk |
| HDR enablement | ADAPT | VidPN rebuild (research needed) |
| GPU selection | BUILD | Adapter name configuration |

### Audio

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Audio isolation | DELEGATE | Windows RDP per-session |
| Audio capture | DELEGATE | Vibepollo WASAPI loopback |
| Microphone | DELEGATE | Wait for Vibepollo WebRTC mic |

### Input

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Gamepad forwarding | DELEGATE | Vibepollo native |
| Gamepad isolation | USE OPEN SOURCE | HidHide session jail (MIT) |
| K/M isolation | RESEARCH | Needs re-architecture |
| Controller routing | BUILD | Optional ViGEm routing |

### Games

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Game launch | BUILD | CreateProcessAsUser wrapper |
| Process tracking | BUILD | PID dictionary |
| Game crash detection | BUILD | Process exit monitoring |
| RDP compatibility | AVOID | Too complex, anti-cheat risk |
| Steam multi-instance | RESEARCH | Needs investigation |

### Streaming

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Streaming server | DELEGATE | Vibepollo (external process) |
| Provider abstraction | BUILD | IStreamingProvider interface |
| Provider config | BUILD | sunshine.conf generation |
| Provider health | BUILD | HTTP health check |

### Process

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Process launch | BUILD | CreateProcessAsUser |
| Token management | BUILD | Windows token APIs |
| Job Objects | BUILD | Standard Windows API |
| Residual adoption | ADAPT | Helios pattern |

### Recovery

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Crash detection | BUILD | Health check interval |
| Progressive backoff | ADAPT | Helios pattern |
| Auto-restart | BUILD | Restart with backoff |
| Full re-provision | BUILD | Re-provision pipeline |

### Security

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| Credential encryption | USE OPEN SOURCE | DPAPI (Windows built-in) |
| File permissions | USE OPEN SOURCE | Windows ACL |
| API authentication | BUILD | API key middleware |

### Management

| Subsystem | Decision | Rationale |
|-----------|----------|-----------|
| REST API | USE OPEN SOURCE | ASP.NET Core (MIT) |
| Web UI | USE OPEN SOURCE | React (MIT) |
| Configuration | BUILD | appsettings.json |
| Metrics | BUILD | Prometheus endpoint |

---

## Summary

| Decision | Count | Examples |
|----------|-------|---------|
| BUILD | 20 | Account management, process tracking, provider abstraction |
| USE OPEN SOURCE | 7 | SudoVDA, HidHide, TermWrap, ASP.NET Core, React |
| DELEGATE | 5 | Vibepollo (streaming, capture, encoding) |
| ADAPT | 3 | Helios patterns, HDR enablement |
| RESEARCH | 2 | K/M isolation, Steam multi-instance |
| AVOID | 1 | Game RDP compatibility |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SudoVDA is open source | GitHub | VERIFIED |
| HidHide is MIT | LICENSE file | VERIFIED |
| TermWrap is MIT | LICENSE file | VERIFIED |
| Vibepollo handles streaming | VibepolloManager | VERIFIED |
| DPAPI is Windows built-in | Windows API | VERIFIED |
| ASP.NET Core is MIT | LICENSE file | VERIFIED |
