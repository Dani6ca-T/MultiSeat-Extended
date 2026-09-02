# Duo Parity Matrix

**Date**: 2026-08-30
**Purpose**: Compare MultiSeat-Extended against Duo's publicly advertised capabilities

---

## IMPORTANT CONSTRAINT

Duo is **proprietary and closed-source**. All Duo claims are based on:
- README
- Release notes
- Wiki
- GitHub issues
- Patreon posts

**No source-level verification possible.** Claims about Duo's internal implementation are labeled as "Duo publicly advertises" not "Duo implements."

---

## Capability Comparison

| Capability | MultiSeat | Duo public evidence | Gap | Difficulty | Driver required? |
|------------|-----------|-------------------|-----|------------|-----------------|
| **Users** | | | | | |
| Windows accounts | ✅ AccountManager | ✅ "User Name" in setup | None | - | No |
| Encrypted passwords | ✅ DPAPI | ✅ v1.5.3: "passwords encrypted" | None | - | No |
| **Sessions** | | | | | |
| Concurrent RDP sessions | ✅ TermWrap | ✅ "based around TermWrap" | None | - | No |
| Session creation | ✅ RDP loopback | ✅ Inferred from architecture | None | - | No |
| **Display** | | | | | |
| Virtual display | ✅ SudoVDA | ✅ "custom WDDM display driver" | None | - | Yes (SudoVDA vs custom) |
| HDR | ❌ EnableHdr no-op | ✅ "HDR support" (supporter) | **HDR** | HIGH | Yes |
| High refresh rate | ✅ Via SudoVDA | ✅ "up to 500Hz" (supporter) | None | - | No |
| Seamless display adjust | ❌ Requires reconnect | ✅ Inferred from features | **Seamless resize** | HIGH | Yes (custom WDDM) |
| Multi-monitor per seat | ❌ Single display | ❓ Unknown | Unknown | UNKNOWN | Unknown |
| GPU selection | ❌ Not implemented | ✅ "Render adapter" setting | **GPU selection** | LOW | No |
| **Audio** | | | | | |
| Per-session audio | ✅ RDP Remote Audio | ✅ Inferred from architecture | None | - | No |
| Microphone | ❌ No mic path | ❓ Unknown | **Mic path** | MEDIUM | No |
| **Input** | | | | | |
| Gamepad forwarding | ✅ Vibepollo native | ✅ Gamepad settings | None | - | No |
| Gamepad isolation | ⚠️ HidHide session jail | ✅ "HID isolation" (UMDF) | **UMDF input driver** | VERY HIGH | Yes (UMDF) |
| Keyboard/Mouse isolation | ❌ InputHookManager no-op | ✅ Inferred from architecture | **K/M isolation** | HIGH | Yes (UMDF) |
| DualSense Edge | ❌ Via Vibepollo | ✅ v1.5.8: custom UMDF driver | **DualSense UMDF** | HIGH | Yes (UMDF) |
| Xbox Elite paddles | ❌ Via Vibepollo | ✅ v1.5.9: GameInput API | None (Vibepollo handles) | - | No |
| **Game** | | | | | |
| Game launch | ✅ ProcessInjector | ✅ Inferred | None | - | No |
| RDP compatibility patching | ❌ Not implemented | ✅ "Application Compatibility Layer" | **App Compat Layer** | VERY HIGH | No (API hooking) |
| DirectX 8/9 support | ❌ Not implemented | ✅ v1.5.5: "DirectX 8 & 9" | **DX8/9 patching** | HIGH | No |
| Process patching opt-in | ❌ Not implemented | ✅ v1.5.6: "verifier opt-out" | **Patch verifier** | MEDIUM | No |
| **Steam** | | | | | |
| Steam multi-instance | ❌ Not implemented | ✅ v1.5.1: "Steam multiboxing" | **Steam isolation** | HIGH | No |
| Steamworks SDK support | ❌ Not implemented | ✅ v1.5.5: "Steamworks SDK" | **Steamworks** | HIGH | No |
| **Streaming** | | | | | |
| Streaming server | ✅ Vibepollo | ✅ Sunshine (forked) | None | - | No |
| Provider abstraction | ❌ VibepolloManager coupled | ✅ Inferred (per-instance) | **Provider abstraction** | MEDIUM | No |
| **Process** | | | | | |
| Process creation | ✅ CreateProcessAsUser | ✅ Inferred from architecture | None | - | No |
| Process tracking | ❌ Not implemented | ❓ Unknown | **Process tracking** | LOW | No |
| Job Objects | ❌ Not implemented | ❓ Unknown | **Job Objects** | LOW | No |
| **Security** | | | | | |
| Password encryption | ✅ DPAPI | ✅ v1.5.3: "passwords encrypted" | None | - | No |
| API authentication | ✅ API key | ✅ Patreon auth (supporter) | None | - | No |
| Service privileges | ✅ SYSTEM | ✅ Windows Service | None | - | No |
| **Management** | | | | | |
| Web UI | ✅ React | ✅ Port 38299 | None | - | No |
| REST API | ✅ ASP.NET Core | ✅ Inferred | None | - | No |
| **Recovery** | | | | | |
| Health monitoring | ✅ SessionHealthCheck | ✅ Service monitoring | None | - | No |
| Auto-restart | ✅ MaxRestartAttempts | ✅ Inferred | None | - | No |
| Display restoration | ✅ TryLateDisplayDetection | ✅ v1.5.3: "display falls back to 30Hz" | None | - | No |
| **Advanced** | | | | | |
| NVIDIA Smooth Motion | ❌ Not implemented | ✅ Supporter feature | **Frame generation** | HIGH | No |
| Super-sampling | ❌ Not implemented | ✅ "up to 500%" (supporter) | **Super-sampling** | MEDIUM | No |
| Windows Sandbox | ❌ Not implemented | ✅ Temporarily removed | **Sandbox** | MEDIUM | No |

---

## Gap Summary (What Duo Has, MultiSeat Doesn't)

| Gap | Difficulty | Driver Required | Priority |
|-----|------------|----------------|----------|
| HDR support | HIGH | Yes (SudoVDA HDR EDID) | P1 |
| Application Compatibility Layer | VERY HIGH | No | P1 |
| Steam multi-instance | HIGH | No | P1 |
| Seamless display adjustment | HIGH | Yes (custom WDDM) | P2 |
| UMDF input driver | VERY HIGH | Yes (UMDF) | P2 |
| GPU selection | LOW | No | P3 |
| Keyboard/Mouse isolation | HIGH | Yes (UMDF) | P3 |
| Process tracking | LOW | No | P3 |
| Job Objects | LOW | No | P3 |
| NVIDIA Smooth Motion | HIGH | No | P4 |
| Super-sampling | MEDIUM | No | P4 |
| Microphone path | MEDIUM | No | P3 |

---

## What MultiSeat Does Better Than Duo

| Advantage | Evidence |
|-----------|----------|
| Open source (MIT) | LICENSE file |
| Display isolation (SudoVDA primary + RDP shrunk) | SeatManager.ApplyDisplayIsolationAsync |
| Per-session audio (no VAC needed) | PerSession audio mode |
| HidHide session jail (undocumented) | HidHideConfigurator |
| Emulator netplay | RetroArch per-seat ports |
| Shared game library | icacls-based SharedGameLibrary |
| Late display detection | TryLateDisplayDetectionAsync |
| Orphan cleanup | Best-effort Kill on teardown |
| Detailed diagnostics | HidHideInspector, LogFilterInspector |
| Well-documented security | CLAUDE.md, security-posture.md |
| Provider flexibility | Can use any Sunshine fork |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Duo uses TermWrap | README | VERIFIED (public) |
| Duo has custom WDDM driver | README | VERIFIED (public) |
| Duo has UMDF input driver | README | VERIFIED (public) |
| Duo has Application Compatibility Layer | Release notes v1.5.5 | VERIFIED (public) |
| Duo has HDR support | README | VERIFIED (public) |
| Duo has Steam multi-instance | Release notes v1.5.1 | VERIFIED (public) |
| Duo has 500Hz support | Patreon | VERIFIED (public) |
| Duo has NVIDIA Smooth Motion | Features | VERIFIED (public) |
| Duo is proprietary | GitHub (no source) | VERIFIED |
| MultiSeat EnableHdr is no-op | MultiSeatOptions.cs | VERIFIED |
| MultiSeat InputHookManager is no-op | CLAUDE.md | VERIFIED |
| MultiSeat has no game process tracking | Codebase search | VERIFIED (absent) |
| MultiSeat has no Steam isolation | Codebase search | VERIFIED (absent) |
