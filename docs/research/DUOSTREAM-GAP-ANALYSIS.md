# MultiSeat-Extended: Анализ разрывов с DuoStream (Gap Analysis)

## Ответ на главный вопрос

> Что делает Duo, чего не делает MultiSeat-Extended?

---

## Session

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Concurrent RDP sessions | YES (TermWrap bundled) | YES (RDPWrap) | **EQUAL** — разные реализации, один результат |
| Session monitoring | Proprietary monitoring | WTS query + keepalive | Duo более интегрирован; MultiSeat足够 |
| Session reconnect | YES | YES (auto on sleep/wake) | **EQUAL** |
| Auto-start sessions | YES | YES (from presets) | **EQUAL** |

**Вывод**: В области session management разрыв минимальный. MultiSeat решает ту же задачу.

---

## Streaming

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Sunshine per user | YES (bundled) | YES (Vibepollo per seat) | **EQUAL** |
| Display auto-adjust | YES (seamless, Windows 11 23H2+) | YES (via reconnect) | ⚠️ Duo — seamless; MultiSeat — brief interruption |
| HDR streaming | YES (paid tier) | NO (probe only, no-op) | 🔴 **GAP** — HDR не работает |
| High refresh rate | YES (up to 500Hz, paid) | YES (up to configured fps) | ⚠️ Duo higher ceiling; MultiSeat足够 |
| Frame generation | UNKNOWN | NO (no NVIDIA Smooth Motion) | 🔴 **GAP** — no frame generation |
| Encoder selection | Auto-detect | Configurable per seat | **EQUAL** |

**Вывод**: Основной разрыв — HDR и frame generation. Duo提供更高级的streaming features.

---

## Display

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Custom WDDM driver | YES (proprietary) | NO (uses SudoVDA) | ⚠️ Different approach |
| Display isolation | YES | YES (SudoVDA primary + RDP shrunk) | **EQUAL** — MultiSeat独特approach |
| 500Hz support | YES (paid) | YES (up to fps limit) | ⚠️ Duo higher ceiling |
| Display restoration | YES | YES (late detection + health check) | **EQUAL** |

**Вывод**: Display management is comparable. Duo's custom driver is more integrated; MultiSeat's SudoVDA approach works.

---

## Audio

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Audio isolation | YES | YES (PerSession) | **EQUAL** |
| Microphone passthrough | UNKNOWN | NO | 🔴 **GAP** — no mic path |
| Host audio protection | YES | YES (mstsc muted) | **EQUAL** |

**Вывод**: Audio isolation is solved. Microphone is the gap.

---

## Input / Controller

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| KB/M isolation | YES (session ID filtering) | NO (InputHookManager is no-op) | 🔴 **GAP** — no KB/M isolation |
| Gamepad isolation | YES (custom UMDF driver) | YES (HidHide session jail, opt-in) | ⚠️ Duo mandatory; MultiSeat optional |
| Virtual controller | YES (custom, GameInput API) | YES (ViGEm, opt-in) | **EQUAL** — different implementations |
| Controller assignment | Auto | Auto + Manual API | **EQUAL** |
| Xbox Elite paddles | YES (GameInput API) | NO | ⚠️ Minor gap |

**Вывод**: KB/M isolation is the main gap. Gamepad isolation is solved differently.

---

## Game / Process

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Game launching | YES | YES (ProcessInjector) | **EQUAL** |
| Launch-on-connect | YES | YES (OnConnectAppLauncher) | **EQUAL** |
| Game mutex isolation | YES (compatibility layer) | NO | 🔴 **GAP** — no mutex isolation |
| Steam multi-instance | YES (patched) | NO | 🔴 **GAP** — no Steam multi-instance |
| Process patching | YES (proprietary) | NO | 🔴 **GAP** — no process compatibility |
| Shared game library | YES | YES (icacls) | **EQUAL** |
| Application Compatibility Layer | YES (native Windows DB) | NO | 🔴 **GAP** — no compat layer |

**Вывод**: Major gap in game/process isolation. Duo's compatibility layer is proprietary and sophisticated.

---

## Recovery

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Health checks | YES | YES (SessionHealthCheck, 5s) | **EQUAL** |
| Auto-restart | YES | YES (max 3 attempts) | **EQUAL** |
| Display restoration | YES | YES (late detection) | **EQUAL** |
| Orphan cleanup | YES | YES (WMI query) | **EQUAL** |

**Вывод**: Recovery is comparable. MultiSeat has robust health checking.

---

## Management

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Web UI | YES (port 38299) | YES (React, port 9550) | **EQUAL** |
| API | YES (REST + WebSocket) | YES (REST + WebSocket) | **EQUAL** |
| Authentication | Session-based | API key | ⚠️ Different approaches |
| HTTPS | YES | NO | 🔴 **GAP** — no HTTPS |
| Remote management | YES | YES (API + Dashboard) | **EQUAL** |
| Auto-start | YES | YES (presets) | **EQUAL** |

**Вывод**: Management is comparable. HTTPS is the gap.

---

## Security

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Seat privileges | Standard user | Standard user | **EQUAL** |
| Credential storage | Unknown | DPAPI (SYSTEM scope) | **EQUAL** (MultiSeat documented) |
| ACL hardening | Unknown | YES | **EQUAL** (MultiSeat documented) |
| API auth | Session-based | API key | **EQUAL** |
| Network isolation | YES | Partial (loopback option) | ⚠️ Duo more restrictive |

**Вывод**: Security is comparable. MultiSeat's approach is well-documented.

---

## Drivers

| Feature | Duo | MultiSeat-Extended | Gap |
|---------|-----|-------------------|-----|
| Custom WDDM driver | YES (proprietary) | NO (SudoVDA) | ⚠️ Different approach |
| UMDF input driver | YES (proprietary) | NO (HidHide) | ⚠️ Different approach |
| TermWrap | YES (bundled) | NO (uses RDPWrap) | ⚠️ Different approach |
| ViGEmBus | YES (legacy) | Optional | **EQUAL** |
| HidHide | NO | Optional | **EQUAL** (MultiSeat has it) |

**Вывод**: Duo uses more proprietary drivers; MultiSeat uses open-source alternatives.

---

## Summary: Critical Gaps

### 🔴 Critical (should address)

1. **HDR support** — Duo has it; MultiSeat has no-op probe
2. **KB/M session isolation** — Duo has filtering; MultiSeat is no-op
3. **Game mutex isolation** — Duo has compatibility layer; MultiSeat has nothing
4. **Steam multi-instance** — Duo has patching; MultiSeat has nothing
5. **HTTPS for API** — Duo has it; MultiSeat is plaintext

### ⚠️ Important (consider addressing)

6. **Microphone passthrough** — Duo has it; MultiSeat has no path
7. **Frame generation** — Duo/NVIDIA Smooth Motion; MultiSeat has nothing
8. **Seamless display adjustment** — Duo is seamless; MultiSeat requires reconnect
9. **Process compatibility layer** — Duo has it; MultiSeat has nothing

### ✅ Comparable (no gap)

10. Session management
11. Streaming (except HDR)
12. Display isolation
13. Audio isolation
14. Gamepad isolation
15. Health checks
16. Recovery
17. Management API
18. Security

---

## What MultiSeat-Extended Does Better

1. **Open source** (MIT vs proprietary)
2. **Display isolation** (SudoVDA primary + RDP shrunk — unique, reduces CPU)
3. **Per-session audio** (no VAC/VoiceMeeter needed)
4. **HidHide session jail** (undocumented but proven)
5. **Emulator netplay** (RetroArch per-seat ports)
6. **Shared game library** (icacls-based)
7. **Late display detection** (handles Vibepollo lazy creation)
8. **Orphan cleanup** (WMI-based, safe for standalone Vibepollo)
9. **Detailed diagnostics** (HidHideInspector, LogFilterInspector, etc.)
10. **Well-documented security posture** (CLAUDE.md, security-posture.md)
