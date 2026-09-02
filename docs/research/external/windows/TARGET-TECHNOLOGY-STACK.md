# Target Technology Stack

**Date**: 2026-08-30
**Purpose**: Preliminary technology recommendations for MultiSeat-Extended

---

## Display

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Virtual display per seat | SudoVDA (already integrated) | Virtual-Display-Driver | Proven, works with Vibepollo | License unclear for SudoVDA | SudoVDA license terms |
| HDR support | SudoVDA (partial) + Vibepollo | Virtual-Display-Driver | HDR EDID metadata | Windows 11 23H2+ required | Exact encoding changes needed |
| High refresh rate | SudoVDA (up to 1000Hz) | Virtual-Display-Driver | Hardware dependent | Encoder must support | None |
| Display isolation | SudoVDA primary + RDP shrunk | N/A | Unique, reduces CPU | State doesn't survive disconnect | None |

## Input

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Gamepad isolation | HidHide session jail (already integrated) | libvirtualhid | Kernel-level, proven | Undocumented feature | HidHide update compatibility |
| Virtual controller | libvirtualhid (modern) | ViGEmBus (legacy) | UMDF2 + VHF, used by Sunshine | License required for Windows driver | libvirtualhid license terms |
| KB/M isolation | InputHookManager (currently no-op) | UMDF input driver | No good open-source option | Complex driver development | Duo's approach unknown |
| Controller assignment | InputRouter (already integrated) | N/A | Works well | None | None |

## Audio

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Per-seat audio | RDP Remote Audio (already used) | Virtual-Audio-Driver | Built into Windows, no VAC needed | None | None |
| Microphone | None (PerSession trade-off) | Vibepollo WebRTC mic | Wait for Vibepollo 1.19.x | Future feature | When will it stabilize? |
| Host audio protection | AudioMuteHelper (already integrated) | N/A | mstsc muted | None | None |

## RDP / Sessions

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Concurrent sessions | TermWrap (already integrated) | TermWrap Rust | MIT, proven, auto offset discovery | TermWrap Rust is newer | TermWrap Rust stability |
| Session creation | RDP loopback (already used) | N/A | Standard approach | None | None |
| Session monitoring | WTS APIs (already used) | N/A | Standard approach | None | None |
| Session reconnect | Auto on sleep/wake (already implemented) | N/A | Works well | None | None |

## Process / Token

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Service → session launch | CreateProcessAsUser (already used) | N/A | Standard Windows API | None | None |
| Token verification | EnsureTokenBelongsTo (already implemented) | N/A | Prevents wrong-session launches | None | None |
| Process isolation | None (best-effort kill) | Job Objects | Guarantee cleanup | None | None |
| Game process tracking | None | Process.GetProcessById | Simple, effective | None | None |

## Game Compatibility

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| RDP detection bypass | None | Application Compatibility Toolkit | Game-specific | Complex, maintenance burden | How does Duo do it? |
| Steam multi-instance | None | `--userdatadir` flag | Simple approach | Steam updates may break | Exact mechanism needed |
| Game mutex isolation | None | Process patching | Game-specific | Complex | Duo's approach unknown |
| DirectX 8/9 support | None | Application Compatibility Layer | Game-specific | Complex | Duo's approach unknown |

## Streaming

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Streaming server | Vibepollo (already integrated) | Apollo, Sunshine | Fork chain: Sunshine → Apollo → Vibepollo | GPLv3 copyleft if linked | None |
| Provider abstraction | None | IStreamingProvider interface | Enable multiple providers | Architecture change needed | None |
| Config generation | VibepolloConfigBuilder (already integrated) | N/A | Works well | None | None |
| Display discovery | ParseSudoVdaDisplayId (already implemented) | N/A | Log parsing approach | None | None |

## Security

| Problem | Best Existing Technology | Alternative | Why | Risks | Open Questions |
|---------|------------------------|-------------|-----|-------|----------------|
| Credential encryption | DPAPI (already used) | N/A | Standard Windows API | None | None |
| ACL hardening | SecureFile (already implemented) | N/A | System + Administrators only | None | None |
| API authentication | API key (already implemented) | Session-based auth | Simple, effective | None | None |
| Seat privileges | Standard users (already enforced) | N/A | Minimal privileges | None | None |

---

## Summary: Recommended Stack

### Already Working (No Changes Needed)
1. ✅ SudoVDA — Virtual displays
2. ✅ TermWrap — Concurrent sessions
3. ✅ RDP Remote Audio — Per-seat audio
4. ✅ HidHide — Gamepad isolation
5. ✅ Vibepollo — Streaming
6. ✅ DPAPI — Credential encryption
7. ✅ ACL hardening — File security
8. ✅ SessionHealthCheck — Health monitoring
9. ✅ VibepolloManager — Crash recovery
10. ✅ ASP.NET Core — API
11. ✅ React — Dashboard

### Recommended Additions
1. **Job Objects** — Process isolation on teardown (LOW effort)
2. **Game process tracking** — Track launched games (LOW effort)
3. **IStreamingProvider** — Provider abstraction (MEDIUM effort)
4. **Steam multi-instance** — Research `--userdatadir` (MEDIUM effort)

### Recommended Research
1. **HDR support** — Investigate encoding changes (HIGH effort)
2. **Game compatibility** — Research Application Compatibility Toolkit (HIGH effort)
3. **libvirtualhid license** — Understand terms (LOW effort)
4. **SudoVDA license** — Understand terms (LOW effort)

### Not Recommended (Too Complex / Windows Limitation)
1. ❌ Seamless display adjustment — Fundamental Windows RDP limitation
2. ❌ Custom UMDF input driver — Too complex, HidHide works
3. ❌ Game process patching — Too complex, game-specific

---

## Open Questions

1. What are the exact license terms for SudoVDA?
2. What are the exact license terms for libvirtualhid?
3. How does Duo implement game process patching?
4. How does Duo implement seamless display adjustment?
5. How does Duo implement Steam multi-instance?
6. When will Vibepollo WebRTC mic support stabilize?
7. What encoding changes are needed for HDR support?
