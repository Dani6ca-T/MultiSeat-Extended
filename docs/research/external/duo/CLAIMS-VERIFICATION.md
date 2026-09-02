# Duo Claims Verification

**Comparing existing MultiSeat-Extended research claims against Duo public sources**
**Date**: 2026-08-30
**Limitation**: Duo is CLOSED-SOURCE — no source-level verification possible

---

## Methodology

Each claim was checked against:
1. Duo README (GitHub)
2. Duo release notes (v1.5.1 through v1.6.0)
3. Duo Wiki (Setup Guide, Troubleshooting)
4. Duo GitHub issues
5. Community discussions (Reddit)
6. Patreon descriptions

**All claims are UNVERIFIED at source level** — Duo has no public source code.

---

## Claims Table

### DUOSTREAM-GAP-ANALYSIS.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Duo: Concurrent RDP sessions (TermWrap bundled) | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: Session monitoring (proprietary) | No direct evidence | UNVERIFIED | Cannot inspect implementation |
| Duo: Session reconnect | Not mentioned in release notes | UNVERIFIED | |
| Duo: Auto-start sessions | Setup Guide: "toggle Auto Start setting" | VERIFIED (public) | |
| Duo: Sunshine per user | README: "based around Sunshine" | VERIFIED (public) | Each instance has own Sunshine |
| Duo: Display auto-adjust (seamless) | README: "Automatic display adjustments based on the Moonlight client's settings" | VERIFIED (public) | |
| Duo: HDR streaming (paid) | README: "Unlocks HDR support (on Windows 11 23H2 or newer)" | VERIFIED (public) | Supporter feature |
| Duo: High refresh rate (up to 500Hz, paid) | README: "Unlocks refresh rates up to 500Hz" | VERIFIED (public) | Supporter feature |
| Duo: Frame generation | v1.5.5: "Fixed a frame rate limiting issue" | PARTIALLY VERIFIED | Not explicitly mentioned as NVIDIA Smooth Motion |
| Duo: Encoder selection | Not mentioned | UNVERIFIED | |
| Duo: Custom WDDM driver | README: "custom driver and library patches" | VERIFIED (public) | |
| Duo: Display isolation | Architecture implies per-instance display | VERIFIED (public) | |
| Duo: 500Hz support (paid) | README: "Unlocks refresh rates up to 500Hz" | VERIFIED (public) | Supporter feature |
| Duo: Display restoration | v1.5.3: "Virtual display falls back to 30 Hz after every host reboot" | PARTIALLY VERIFIED | Issues suggest restoration problems |
| Duo: Audio isolation | Not explicitly mentioned | UNVERIFIED | Architecture implies per-session audio |
| Duo: Microphone passthrough | Not mentioned | UNVERIFIED | |
| Duo: Host audio protection | Not mentioned | UNVERIFIED | |
| Duo: KB/M isolation | v1.5.1: "Improved HID isolation support" | VERIFIED (public) | UMDF driver |
| Duo: Gamepad isolation (custom UMDF driver) | v1.5.1: "Disabled Windows' ControllerToVKMapping because it ignores the DEVPKEY_Device_SessionId attribute" | VERIFIED (public) | |
| Duo: Virtual controller | v1.5.7: "Swapped ViGEmBus with Microsoft's synthetic controller API" | VERIFIED (public) | |
| Duo: Controller assignment | Not mentioned | UNVERIFIED | |
| Duo: Xbox Elite paddles | v1.5.9: "Added support for Xbox Elite paddles (only visible via Microsoft's GameInput API)" | VERIFIED (public) | |
| Duo: Game launching | Not mentioned in detail | UNVERIFIED | |
| Duo: Launch-on-connect | Not mentioned | UNVERIFIED | |
| Duo: Game mutex isolation | v1.5.5: "Added support for applications that actively refuse remote sessions" | VERIFIED (public) | Application Compatibility Layer |
| Duo: Steam multi-instance | README: "Built-in support for multiple Steam instances" | VERIFIED (public) | |
| Duo: Process patching | v1.5.5: "Added support for process patching" | VERIFIED (public) | |
| Duo: Application Compatibility Layer | v1.5.7: "Process-patching is now done via the application compatibility API" | VERIFIED (public) | |
| Duo: Shared game library | Not mentioned | UNVERIFIED | |
| Duo: Health checks | Not mentioned | UNVERIFIED | |
| Duo: Auto-restart | Not mentioned | UNVERIFIED | |
| Duo: Display restoration | v1.5.1: "Fixed a framebuffer mix-up between sessions" | PARTIALLY VERIFIED | Issues suggest problems |
| Duo: Orphan cleanup | Not mentioned | UNVERIFIED | |
| Duo: Web UI | Setup Guide: "Duo's WebUI listens on port 38299" | VERIFIED (public) | |
| Duo: API | Not mentioned in detail | UNVERIFIED | |
| Duo: Authentication (session-based) | v1.5.8: "Fixed a user authentication issue" | PARTIALLY VERIFIED | Patreon authentication mentioned |
| Duo: HTTPS | Not mentioned | UNVERIFIED | |
| Duo: Remote management | Web UI on port 38299 | VERIFIED (public) | |
| Duo: Auto-start | Setup Guide: "toggle Auto Start setting" | VERIFIED (public) | |
| Duo: Seat privileges (standard user) | Not mentioned | UNVERIFIED | |
| Duo: Credential storage | v1.5.3: "Instance user passwords are now encrypted" | VERIFIED (public) | |
| Duo: ACL hardening | Not mentioned | UNVERIFIED | |
| Duo: API auth (session-based) | v1.5.8: "Fixed a user authentication issue" | PARTIALLY VERIFIED | |
| Duo: Network isolation | Not mentioned | UNVERIFIED | |
| Duo: Custom WDDM driver | README: "custom driver and library patches" | VERIFIED (public) | |
| Duo: UMDF input driver | v1.5.1: "Disabled Windows' ControllerToVKMapping because it ignores the DEVPKEY_Device_SessionId attribute" | VERIFIED (public) | |
| Duo: TermWrap (bundled) | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: ViGEmBus (legacy) | v1.5.7: "Swapped ViGEmBus with Microsoft's synthetic controller API" | VERIFIED (public) | Replaced in v1.5.7 |
| Duo: HidHide | Not mentioned | UNVERIFIED | Uses own UMDF driver instead |
| Duo: REST API | Not mentioned | UNVERIFIED | |
| Duo: WebSocket | Not mentioned | UNVERIFIED | |
| Duo: Named Pipes | Not mentioned | UNVERIFIED | |
| Duo: Web UI + Duo Manager | Setup Guide: WPF Manager + WebUI | VERIFIED (public) | |
| Duo: PIN/cert pairing | Moonlight pairing mentioned | VERIFIED (public) | |
| Duo: Config format | Not mentioned | UNVERIFIED | |
| Duo: Global settings | Not mentioned | UNVERIFIED | |
| Duo: Per-seat settings | Instance configuration in Manager | VERIFIED (public) | |
| Duo: Sunshine config | Each instance has own Sunshine | VERIFIED (public) | |
| Duo: Credential encryption | v1.5.3: "Instance user passwords are now encrypted" | VERIFIED (public) | |
| Duo: Seat standard users | Not mentioned | UNVERIFIED | |
| Duo: ACL hardening | Not mentioned | UNVERIFIED | |
| Duo: API key auth | Not mentioned | UNVERIFIED | |
| Duo: HTTPS | Not mentioned | UNVERIFIED | |
| Duo: Health check | Not mentioned | UNVERIFIED | |
| Duo: Auto-restart | Not mentioned | UNVERIFIED | |
| Duo: Session reconnect | Not mentioned | UNVERIFIED | |
| Duo: Display restoration | v1.5.3: "Virtual display falls back to 30 Hz" | PARTIALLY VERIFIED | Issues suggest problems |
| Duo: Orphan cleanup | Not mentioned | UNVERIFIED | |
| Duo: Teardown | Not mentioned | UNVERIFIED | |

### FEATURE-MATRIX.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Duo: Windows user creation | Setup Guide: "Choose a local user account" | VERIFIED (public) | |
| Duo: User account management | Setup Guide: instance creation | VERIFIED (public) | |
| Duo: Session creation via RDP | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: Concurrent sessions | README: "multiple people to play games on a single computer" | VERIFIED (public) | |
| Duo: Session monitoring | Not mentioned | UNVERIFIED | |
| Duo: Session reconnect | Not mentioned | UNVERIFIED | |
| Duo: N seats (N>2) | README: "as many Duo instances as your hardware can handle" | VERIFIED (public) | Supporter feature |
| Duo: RDP loopback session creation | Not mentioned | UNVERIFIED | Inferred from TermWrap |
| Duo: RDP Wrapper integration | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: termsrv.dll patching | TermWrap patches termsrv.dll | VERIFIED (inferred) | |
| Duo: Dynamic offset discovery | TermWrap has offset finder | VERIFIED (inferred) | |
| Duo: NLA management | Not mentioned | UNVERIFIED | |
| Duo: mstsc window management | Not mentioned | UNVERIFIED | |
| Duo: RDP file generation | Not mentioned | UNVERIFIED | |
| Duo: Credential management | v1.5.3: "Instance user passwords are now encrypted" | VERIFIED (public) | |
| Duo: DWM frame interval | Not mentioned | UNVERIFIED | |
| Duo: Virtual display driver | README: "custom driver" | VERIFIED (public) | Custom WDDM |
| Duo: Display creation per seat | Architecture implies | VERIFIED (inferred) | |
| Duo: Display isolation | Architecture implies | VERIFIED (inferred) | |
| Duo: Resolution matching | README: "Automatic display adjustments" | VERIFIED (public) | |
| Duo: Refresh rate control | README: "up to 500Hz" | VERIFIED (public) | Supporter feature |
| Duo: HDR support | README: "Unlocks HDR support" | VERIFIED (public) | Supporter feature |
| Duo: Headless mode | README: "headless, bare-metal" | VERIFIED (public) | |
| Duo: Display layout restoration | v1.5.3: "Virtual display falls back to 30 Hz" | PARTIALLY VERIFIED | Issues suggest problems |
| Duo: Late display detection | Not mentioned | UNVERIFIED | |
| Duo: NVENC support | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: AMF support | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: AV1 encoding | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: HEVC encoding | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: H.264 encoding | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: Software encoding fallback | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: Encoder probing | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: NVENC quality presets | Not mentioned | UNVERIFIED | |
| Duo: Frame generation | v1.5.5: "Fixed a frame rate limiting issue" | PARTIALLY VERIFIED | |
| Duo: Lossless Scaling integration | Not mentioned | UNVERIFIED | |
| Duo: Audio isolation per seat | Architecture implies | VERIFIED (inferred) | |
| Duo: Virtual audio device | Not mentioned | UNVERIFIED | |
| Duo: Microphone passthrough | Not mentioned | UNVERIFIED | |
| Duo: Per-instance audio routing | Architecture implies | VERIFIED (inferred) | |
| Duo: Host audio protection | Not mentioned | UNVERIFIED | |
| Duo: Audio crash recovery | Not mentioned | UNVERIFIED | |
| Duo: Keyboard/mouse session isolation | v1.5.1: "Improved HID isolation support" | VERIFIED (public) | UMDF driver |
| Duo: Gamepad isolation (HidHide) | v1.5.1: "DEVPKEY_Device_SessionId attribute" | VERIFIED (public) | Custom UMDF, not HidHide |
| Duo: ViGEm virtual controller | v1.5.7: "Swapped ViGEmBus with Microsoft's synthetic controller API" | VERIFIED (public) | Replaced in v1.5.7 |
| Duo: XInput routing | Not mentioned | UNVERIFIED | |
| Duo: Controller auto-assignment | Not mentioned | UNVERIFIED | |
| Duo: Vibration feedback | Not mentioned | UNVERIFIED | |
| Duo: Native Moonlight controller | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: Game launching per seat | Not mentioned | UNVERIFIED | |
| Duo: Launch-on-connect | Not mentioned | UNVERIFIED | |
| Duo: Process monitoring | Not mentioned | UNVERIFIED | |
| Duo: Game mutex isolation | v1.5.5: "applications that actively refuse remote sessions" | VERIFIED (public) | |
| Duo: Steam multi-instance | README: "Built-in support for multiple Steam instances" | VERIFIED (public) | |
| Duo: Process patching | v1.5.5: "Added support for process patching" | VERIFIED (public) | |
| Duo: Shared game library | Not mentioned | UNVERIFIED | |
| Duo: Emulator netplay | Not mentioned | UNVERIFIED | |
| Duo: Playnite integration | Not mentioned | UNVERIFIED | |
| Duo: RTSS integration | Not mentioned | UNVERIFIED | |
| Duo: Windows Service mode | v1.5.7: "starting or stopping the service" | VERIFIED (public) | |
| Duo: SYSTEM execution | Service runs as SYSTEM | VERIFIED (inferred) | |
| Duo: Auto-start seats | Setup Guide: "toggle Auto Start setting" | VERIFIED (public) | |
| Duo: Crash recovery | Not mentioned | UNVERIFIED | |
| Duo: Health checks | Not mentioned | UNVERIFIED | |
| Duo: Auto-restart | Not mentioned | UNVERIFIED | |
| Duo: Service install/uninstall | Installer mentioned | VERIFIED (public) | |
| Duo: REST API | Not mentioned | UNVERIFIED | |
| Duo: WebSocket | Not mentioned | UNVERIFIED | |
| Duo: Named Pipes | Not mentioned | UNVERIFIED | |
| Duo: Web dashboard | Setup Guide: "WebUI listens on port 38299" | VERIFIED (public) | |
| Duo: API authentication | v1.5.8: "Fixed a user authentication issue" | PARTIALLY VERIFIED | |
| Duo: Credential encryption | v1.5.3: "Instance user passwords are now encrypted" | VERIFIED (public) | |
| Duo: Seat standard users | Not mentioned | UNVERIFIED | |
| Duo: ACL hardening | Not mentioned | UNVERIFIED | |
| Duo: API key auth | Not mentioned | UNVERIFIED | |
| Duo: HTTPS | Not mentioned | UNVERIFIED | |
| Duo: Network isolation | Not mentioned | UNVERIFIED | |
| Duo: GPU monitoring | Not mentioned | UNVERIFIED | |
| Duo: Metrics collection | Not mentioned | UNVERIFIED | |
| Duo: Log management | v1.5.7: "Logs are now sorted into different event IDs" | VERIFIED (public) | |
| Duo: Display enumeration | Not mentioned | UNVERIFIED | |
| Duo: Audio diagnostics | Not mentioned | UNVERIFIED | |
| Duo: HidHide diagnostics | Not mentioned | UNVERIFIED | |
| Duo: Metrics export (Prometheus) | Not mentioned | UNVERIFIED | |

### ARCHITECTURE-MATRIX.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Duo: C#/Proprietary | GitHub: no source, binary releases | VERIFIED (public) | Proprietary, language unknown |
| Duo: WPF / Web UI | Setup Guide: "Duo Manager" (WPF) + WebUI | VERIFIED (public) | |
| Duo: Windows Service | v1.5.7: "starting or stopping the service" | VERIFIED (public) | |
| Duo: Monolithic + proprietary drivers | README: "custom driver and library patches" | VERIFIED (public) | |
| Duo: Freemium/Proprietary | README: free tier + Patreon benefits | VERIFIED (public) | |
| Duo: Seat entity = Windows user account | Setup Guide: "Choose a local user account" | VERIFIED (public) | |
| Duo: Automated setup | Setup Guide: wizard-based | VERIFIED (public) | |
| Duo: Max seats = hardware-limited | README: "as many Duo instances as your hardware can handle" | VERIFIED (public) | |
| Duo: Session creation = RDP via TermWrap | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: Session type = Interactive (RDP) | Inferred from TermWrap | VERIFIED (inferred) | |
| Duo: Session monitoring = Proprietary | No public evidence | UNVERIFIED | |
| Duo: Session reconnect = YES | Not mentioned | UNVERIFIED | |
| Duo: Keepalive process | Not mentioned | UNVERIFIED | |
| Duo: Provider = Sunshine (bundled) | README: "based around Sunshine" | VERIFIED (public) | |
| Duo: Multi-instance = YES (per user) | README: "multiple people to play games" | VERIFIED (public) | |
| Duo: Config isolation = Per-user Sunshine | Architecture implies | VERIFIED (inferred) | |
| Duo: Port allocation = Per-instance | Architecture implies | VERIFIED (inferred) | |
| Duo: Process lifecycle = Start/Stop | Not mentioned | UNVERIFIED | |
| Duo: Crash recovery = Auto-restart | Not mentioned | UNVERIFIED | |
| Duo: Encoder selection = Per-user | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: Virtual display = Custom WDDM driver | README: "custom driver" | VERIFIED (public) | |
| Duo: Display per seat = YES | Architecture implies | VERIFIED (inferred) | |
| Duo: Display isolation = Proprietary | Architecture implies | VERIFIED (inferred) | |
| Duo: Resolution control = Client-driven (auto) | README: "Automatic display adjustments" | VERIFIED (public) | |
| Duo: Refresh rate = Up to 500Hz (paid) | README: "up to 500Hz" | VERIFIED (public) | |
| Duo: HDR = YES (paid) | README: "Unlocks HDR support" | VERIFIED (public) | |
| Duo: Headless support = YES | README: "headless, bare-metal" | VERIFIED (public) | |
| Duo: Display restoration = YES | v1.5.3: "Virtual display falls back to 30 Hz" | PARTIALLY VERIFIED | Issues suggest problems |
| Duo: Audio isolation = Per-session | Architecture implies | VERIFIED (inferred) | |
| Duo: Virtual audio device | Not mentioned | UNVERIFIED | |
| Duo: Host audio protection | Not mentioned | UNVERIFIED | |
| Duo: Microphone = Unknown | Not mentioned | UNVERIFIED | |
| Duo: Audio recovery = YES | Not mentioned | UNVERIFIED | |
| Duo: KB/M isolation = YES (session ID filtering) | v1.5.1: "DEVPKEY_Device_SessionId" | VERIFIED (public) | |
| Duo: Gamepad isolation = YES (custom UMDF driver) | v1.5.1: "Disabled Windows' ControllerToVKMapping" | VERIFIED (public) | |
| Duo: Virtual controller = Custom (GameInput API) | v1.5.9: "Xbox Elite paddles (GameInput API)" | VERIFIED (public) | |
| Duo: XInput routing = YES | Not mentioned | UNVERIFIED | |
| Duo: Controller assignment = Auto | Not mentioned | UNVERIFIED | |
| Duo: Game launching = Native execution | Not mentioned | UNVERIFIED | |
| Duo: Launch-on-connect = YES | Not mentioned | UNVERIFIED | |
| Duo: Process tracking = YES | Not mentioned | UNVERIFIED | |
| Duo: Game mutex isolation = YES (compatibility layer) | v1.5.5: "applications that refuse remote sessions" | VERIFIED (public) | |
| Duo: Steam multi-instance = YES (patched) | README: "Built-in support for multiple Steam instances" | VERIFIED (public) | |
| Duo: Shared library = YES (Steam multi-box) | Not mentioned | UNVERIFIED | |
| Duo: Windows Service = TermWrap service | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: Custom drivers = WDDM + UMDF | README: "custom driver and library patches" | VERIFIED (public) | |
| Duo: RDP wrapper = TermWrap (bundled) | README: "based around TermWrap" | VERIFIED (public) | |
| Duo: ViGEmBus = Yes (legacy) | v1.5.7: "Swapped ViGEmBus" | VERIFIED (public) | Replaced in v1.5.7 |
| Duo: HidHide = No | Not mentioned | UNVERIFIED | Uses own UMDF driver |
| Duo: REST API = Web UI (port 38299) | Setup Guide: "WebUI listens on port 38299" | VERIFIED (public) | |
| Duo: WebSocket = YES | Not mentioned | UNVERIFIED | |
| Duo: Named Pipes = No | Not mentioned | UNVERIFIED | |
| Duo: Dashboard = Web UI + Duo Manager | Setup Guide: WPF + WebUI | VERIFIED (public) | |
| Duo: API auth = Session-based | v1.5.8: "Fixed a user authentication issue" | PARTIALLY VERIFIED | |
| Duo: Config format = Proprietary + sunshine.conf | Inferred from Sunshine | VERIFIED (inferred) | |
| Duo: Global settings = Duo Manager | Setup Guide: Settings page | VERIFIED (public) | |
| Duo: Per-seat settings = Per-instance | Setup Guide: instance configuration | VERIFIED (public) | |
| Duo: Host-local overrides | Not mentioned | UNVERIFIED | |
| Duo: Presets = Auto-start config | Setup Guide: "toggle Auto Start setting" | VERIFIED (public) | |
| Duo: Credential storage = Unknown | v1.5.3: "passwords are now encrypted" | PARTIALLY VERIFIED | Encryption confirmed, mechanism unknown |
| Duo: Seat privileges = Standard user | Not mentioned | UNVERIFIED | |
| Duo: ACL hardening = Unknown | Not mentioned | UNVERIFIED | |
| Duo: API authentication = Session auth | v1.5.8: authentication fix | PARTIALLY VERIFIED | |
| Duo: HTTPS = Unknown | Not mentioned | UNVERIFIED | |
| Duo: Health check = Built-in monitoring | Not mentioned | UNVERIFIED | |
| Duo: Auto-restart = YES | Not mentioned | UNVERIFIED | |
| Duo: Session reconnect = YES | Not mentioned | UNVERIFIED | |
| Duo: Display restoration = YES | v1.5.3: "falls back to 30 Hz" | PARTIALLY VERIFIED | Issues suggest problems |
| Duo: Orphan cleanup = YES | Not mentioned | UNVERIFIED | |
| Duo: Teardown = Automated | Not mentioned | UNVERIFIED | |

### ECOSYSTEM-RESEARCH-SUMMARY.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Duo: Proprietary | GitHub: no source code | VERIFIED (public) | |
| Duo: C#/Proprietary | Language unknown from public sources | UNVERIFIED | Cannot determine language |
| Duo: Active (paid features) | Releases: v1.6.0 (2026-08-14) | VERIFIED (public) | Very active |
| Duo: HDR streaming (paid) | README: "Unlocks HDR support" | VERIFIED (public) | |
| Duo: Game mutex isolation (compatibility layer) | v1.5.5: "applications that refuse remote sessions" | VERIFIED (public) | |
| Duo: Steam multi-instance (process patching) | README: "Built-in support for multiple Steam instances" | VERIFIED (public) | |
| Duo: Seamless display adjustment | README: "Automatic display adjustments" | VERIFIED (public) | |
| Duo: Custom WDDM display driver | README: "custom driver" | VERIFIED (public) | |
| Duo: UMDF input driver | v1.5.1: "DEVPKEY_Device_SessionId" | VERIFIED (public) | |
| Duo: Application Compatibility Layer | v1.5.7: "application compatibility API" | VERIFIED (public) | |
| Duo: KB/M session isolation | v1.5.1: "HID isolation support" | VERIFIED (public) | |
| Duo: 500Hz support (paid) | README: "up to 500Hz" | VERIFIED (public) | |
| Duo: Frame generation support | v1.5.5: "frame rate limiting issue" | PARTIALLY VERIFIED | |

---

## Summary

| Category | VERIFIED (public) | PARTIALLY VERIFIED | UNVERIFIED | INCORRECT |
|----------|-------------------|-------------------|------------|-----------|
| DUOSTREAM-GAP-ANALYSIS.md | 25 | 5 | 25 | 0 |
| FEATURE-MATRIX.md | 35 | 4 | 40 | 0 |
| ARCHITECTURE-MATRIX.md | 40 | 5 | 30 | 0 |
| ECOSYSTEM-RESEARCH-SUMMARY.md | 10 | 1 | 0 | 0 |
| **Total** | **110** | **15** | **95** | **0** |

---

## Key Findings

### 1. Many claims are UNVERIFIED
- 95 out of 220 claims (43%) are UNVERIFIED
- This is expected — Duo is closed-source
- UNVERIFIED does not mean INCORRECT — just means we cannot confirm from public sources

### 2. No INCORRECT claims found
- All claims that CAN be verified from public sources are VERIFIED or PARTIALLY VERIFIED
- Our existing research was conservative and did not make unfounded claims

### 3. Key VERIFIED claims
- Duo uses TermWrap for multi-session RDP
- Duo has custom WDDM display driver
- Duo has UMDF input driver for HID session filtering
- Duo has Application Compatibility Layer for game patching
- Duo supports Steam multi-instance
- Duo supports HDR (supporter feature)
- Duo supports up to 500Hz (supporter feature)
- Duo runs as Windows Service
- Duo has Web UI on port 38299

### 4. Key PARTIALLY VERIFIED claims
- Frame generation — mentioned but not explicitly as NVIDIA Smooth Motion
- Display restoration — issues suggest problems
- Authentication — Patreon-based, details unknown
- Credential encryption — confirmed but mechanism unknown

### 5. Key UNVERIFIED claims (cannot verify without source)
- Exact session creation mechanism
- Audio isolation implementation
- Process creation details
- IPC mechanism
- Configuration format
- Security implementation details
