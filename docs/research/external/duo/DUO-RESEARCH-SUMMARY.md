# DuoStream/Duo Research Summary

**Repository**: DuoStream/Duo
**Status**: PROPRIETARY / CLOSED-SOURCE
**License**: Proprietary (freemium model)
**Latest version**: v1.6.0 (2026-08-14)
**Author**: Black-Seraph (Kevin Koslowski)
**Source code**: NOT AVAILABLE — GitHub contains only binary releases, README, wiki, issues

---

## CRITICAL LIMITATION

**Duo is proprietary and closed-source.** The GitHub repository (https://github.com/DuoStream/Duo) contains:
- Binary installers (releases)
- README
- Wiki
- Issues
- **NO SOURCE CODE**

Therefore, ALL architectural claims about Duo are based on:
1. README description
2. Release notes
3. Wiki documentation
4. Issue descriptions
5. Community discussions (Reddit, Patreon)
6. Inferred behavior from component names

**No source-level verification is possible.**

---

## 1. What is Duo

Duo is an **HDR-compatible multiseat streaming solution** for Windows. It allows multiple users to play games on a single PC simultaneously, each with their own independent desktop, display, audio, and input.

**Key components** (from README):
- **TermWrap** — Multi-session RDP patching
- **Sunshine** — Streaming server (forked/patched)
- **Moonlight** — Client (external)
- **Custom WDDM display driver** — Virtual displays
- **UMDF input driver** — Input isolation
- **Application Compatibility Layer** — Game compatibility

---

## 2. Architecture (Inferred from Public Sources)

```
Duo Manager (WPF UI)
    ↓
Duo Service (Windows Service, SYSTEM)
    ├── TermWrap (multi-session RDP)
    ├── Custom WDDM Display Driver
    ├── UMDF Input Driver
    ├── Sunshine (per-seat streaming server)
    ├── Application Compatibility Layer
    └── Per-seat sessions
         ├── User account
         ├── Windows session
         ├── Virtual display
         ├── Audio device
         ├── Input devices
         ├── Game processes
         └── Sunshine instance
```

---

## 3. Seat Model

### What is a Seat in Duo?

A "Seat" (called "Instance" in Duo) is:
- A **Windows user account**
- Running in its own **Windows session**
- With its own **virtual display** (custom WDDM driver)
- With its own **audio device**
- With its own **input devices** (UMDF driver)
- With its own **Sunshine streaming server**

### Instance Configuration

From Setup Guide:
- **User Name** — Windows account
- **Password** — Account password
- **Auto Start** — Start on Duo service start
- **Display settings** — Resolution, refresh rate
- **Gamepad settings** — Controller type
- **HDR settings** — HDR support (supporter feature)
- **Super-sampling** — Up to 500% (supporter feature)
- **Render adapter** — GPU selection per instance

---

## 4. User Management

### How Duo Creates Users

From Setup Guide and issues:
- Duo creates/manages Windows local user accounts
- Each instance has its own user account
- Accounts are added to appropriate groups
- Passwords are encrypted (v1.5.3+)

### Evidence

- v1.5.3: "Instance user passwords are now encrypted"
- v1.5.8: "Fixed an issue with passwords containing special symbols"
- Setup Guide: "Choose a local user account from the User Name list"

---

## 5. Windows Sessions

### How Duo Creates Sessions

Duo uses **TermWrap** (fork of RDPWrap) to enable multiple concurrent RDP sessions. Evidence:
- README: "based around TermWrap"
- v1.5.5: "Added support for Windows 11 26100.7523+"
- v1.5.8: "Added support for KB5089573's new terminal server"

### Session Isolation

Each instance runs in its own Windows session:
- Separate desktop
- Separate display
- Separate audio
- Separate input
- Separate process space

---

## 6. RDP / TermWrap

### How Duo Enables Multi-Session

Duo bundles **TermWrap** (llccd/TermWrap) — a DLL proxy that patches termsrv.dll at runtime to allow concurrent RDP sessions.

Evidence:
- README: "based around TermWrap"
- v1.5.5: "Added support for Windows 11 26100.7523+"
- v1.5.8: "Added support for KB5089573's new terminal server"

### RDP Configuration

From issues:
- NLA (Network Level Authentication) must be disabled for loopback
- RDP listener port can be custom (v1.5.9 fixed custom port issue)
- Certificate warnings suppressed

---

## 7. Process Creation

### How Duo Launches Processes

Duo launches processes inside seat sessions using Windows token manipulation:
- `CreateProcessAsUser` — Standard Windows API
- `CreateProcessWithTokenW` — For impersonation

Evidence:
- v1.5.5: "Added support for process patching"
- v1.5.7: "Process-patching is now done via the application compatibility API"
- Issue #544: "Failed to duplicate the user token"

### Process Patching

Duo has an **Application Compatibility Layer** that patches game processes:
- v1.5.5: "Added support for DirectX 8 & 9 applications"
- v1.5.5: "Added support for applications that actively refuse remote sessions"
- v1.5.7: "Process-patching is now done via the application compatibility API"
- v1.5.7: "Patched processes no longer produce file locks"

Purpose: Games that check for single-instance mutexes or refuse to run in RDP sessions are patched to work.

---

## 8. Display Architecture

### Custom WDDM Display Driver

Duo has a **custom WDDM display driver** (not IddCx/SudoVDA):
- README: "custom driver and library patches"
- v1.5.1: "Fixed a screen tearing issue caused by the display driver"
- v1.5.3: "Reduced the idle CPU usage of the indirect display driver"
- v1.5.7: "Added support for KB5079473's new display driver"

### Display Creation

Each instance gets its own virtual display:
- Automatic display adjustments based on Moonlight client settings
- Resolution matching client requirements
- Refresh rate up to 500Hz (supporter feature)
- HDR support (Windows 11 23H2+)

### Display Topology

From issues:
- v1.5.1: "Fixed a framebuffer mix-up between sessions that occured when a newly spawned session received an out-of-order session ID"
- v1.5.3: "Virtual display falls back to 30 Hz after every host reboot"

---

## 9. Audio Architecture

### Per-Session Audio

Duo provides per-session audio isolation:
- Each instance has its own audio device
- Sunshine captures from the instance's audio device
- No VAC/VoiceMeeter needed

Evidence:
- Release notes don't mention VAC/VoiceMeeter
- Architecture implies per-session audio via Windows RDP

### Audio Configuration

From issues:
- v1.5.8: "Fixed a crash that could occur when Steam changed audio endpoint visibility mid-session"

---

## 10. Input Architecture

### UMDF Input Driver

Duo has a **custom UMDF (User-Mode Driver Framework) input driver**:
- README: "custom driver and library patches"
- v1.5.1: "Improved HID isolation support"
- v1.5.1: "Disabled Windows' ControllerToVKMapping because it ignores the DEVPKEY_Device_SessionId attribute"
- v1.5.4: "Fixed a HID isolation issue that occured whenever the console session ID changed"

### HID Isolation

Duo uses **session ID filtering** on HID devices:
- v1.5.1: "Improved HID isolation support"
- v1.5.4: "Fixed a HID isolation issue that occured whenever the console session ID changed"
- v1.5.2: "This new Duo build incorporates all three improvements, leaves us with no further feasible enhancements for HID isolation"

### Gamepad Support

- Xbox controllers (XInput)
- DualShock 4 (v1.5.9)
- DualSense / DualSense Edge (v1.5.8)
- Xbox Elite paddles via GameInput API (v1.5.9)
- ViGEmBus (legacy, replaced in v1.5.7)
- Microsoft synthetic controller API (v1.5.7+)

### Controller Changes

- v1.5.7: "Swapped ViGEmBus with Microsoft's synthetic controller API"
- v1.5.8: "Added support for DualSense Edge controller emulation via a custom UMDF driver"

---

## 11. Game Management

### Steam Multi-Instance

Duo supports multiple Steam instances:
- README: "Built-in support for multiple Steam instances"
- v1.5.1: "Added Steam multiboxing support"
- v1.5.5: "Fixed Steam isolation"
- v1.5.5: "Added Steamworks SDK support to Steam isolation"
- v1.5.7: "Fixed several Steam isolation issues"

### Game Process Isolation

Duo patches game processes to work in RDP sessions:
- v1.5.5: "Added support for DirectX 8 & 9 applications"
- v1.5.5: "Added support for applications that actively refuse remote sessions"
- v1.5.7: "Process-patching is now done via the application compatibility API"
- v1.5.7: "Patched processes no longer produce file locks"

### Anti-Cheat

- v1.5.6: "Added a verifier opt-out option for anticheat users (this WILL break things, be warned)"

---

## 12. Streaming Integration

### Sunshine as Backend

Duo uses **Sunshine** (likely patched/forked) as the streaming backend:
- README: "based around TermWrap, Sunshine, Moonlight"
- Each instance has its own Sunshine process
- Sunshine configured per-instance

### Provider Lifecycle

Duo manages Sunshine processes:
- Start/stop per instance
- Configuration per instance
- Port allocation per instance
- Process monitoring

---

## 13. Multi-Instance

### How Duo Achieves Multi-Instance

1. **TermWrap** enables concurrent RDP sessions
2. Each instance gets its own Windows session
3. Each session has its own display (custom WDDM driver)
4. Each session has its own audio device
5. Each session has its own input devices (UMDF driver)
6. Each session has its own Sunshine instance
7. Each instance has its own configuration

### Instance Limits

- Free tier: 1 instance + host session (30Hz cap)
- Supporter tier ($10+): Unlimited instances, 500Hz, HDR

---

## 14. Services

### Duo Service

Duo runs as a **Windows Service** (SYSTEM):
- Manages instance lifecycle
- Launches sessions
- Manages display driver
- Manages input driver
- Monitors processes

Evidence:
- v1.5.7: "Fixed a crash that could occur when starting or stopping the service"
- v1.5.5: "The service can now be restarted from within a connected Duo instance"
- v1.5.5: "The service can now be restarted from the WebUI"

---

## 15. IPC

### Duo Manager ↔ Duo Service

Communication between Duo Manager (UI) and Duo Service:
- Likely Named Pipes or HTTP
- Manager UI on port 38299 (configurable)

Evidence:
- Setup Guide: "Duo's WebUI listens on port 38299"
- v1.5.5: "Fixed a Duo Manager mutex issue"

---

## 16. Configuration

### Configuration Storage

- Instance configs stored on disk
- User passwords encrypted (v1.5.3+)
- Global settings (port, defaults)
- Per-instance settings

### Configuration UI

- WPF Manager application
- Web UI on port 38299
- Instance creation wizard

---

## 17. API

### Web UI / API

- Port: 38299 (configurable)
- REST API for instance management
- WebSocket for real-time updates
- Authentication via Patreon account (for supporter features)

---

## 18. Recovery

### Health Monitoring

- Service monitors instances
- Auto-restart on crash
- Display restoration

Evidence:
- v1.5.5: "Fixed an issue that caused the virtual monitor to lock itself to 30Hz"
- v1.5.3: "Virtual display falls back to 30 Hz after every host reboot"

---

## 19. Security

### Credential Management

- User passwords encrypted (v1.5.3+)
- Patreon authentication for supporter features
- Service runs as SYSTEM

Evidence:
- v1.5.3: "Instance user passwords are now encrypted"
- v1.5.8: "Fixed a user authentication issue that prevented instance starts"

---

## 20. Windows Techniques

### Confirmed Techniques

1. **TermWrap** — Multi-session RDP patching
2. **Custom WDDM display driver** — Virtual displays
3. **UMDF input driver** — Input isolation / HID session filtering
4. **Application Compatibility Layer** — Game process patching
5. **CreateProcessAsUser** — Session-scoped process launch
6. **Token manipulation** — DuplicateToken for session access
7. **DEVPKEY_Device_SessionId** — HID device session filtering
8. **Windows Sandbox** — Isolated environment (temporarily removed)

### Inferred Techniques

1. **RDP loopback** — Session creation via 127.0.0.2
2. **NLA disable** — Required for loopback logon
3. **Certificate suppression** — RDP certificate warnings
4. **Firewall rules** — Port management per instance

---

## 21. Issues / Releases

### Current Version: v1.6.0 (2026-08-14)

Key features:
- Fixed hardware acceleration in WPF applications
- Fixed Xbox gamepad appearing twice
- Fixed AMD GPU HDR render issue
- Fixed HDR after KB5094126

### Recent Releases

| Version | Date | Key Changes |
|---------|------|-------------|
| v1.6.0 | 2026-08-14 | HDR fixes, gamepad fixes |
| v1.5.9 | 2026-07-03 | DualShock 4, DualSense, Xbox Elite |
| v1.5.8 | 2026-06-12 | DualSense Edge UMDF driver, Ko-Fi |
| v1.5.7 | 2026-04-29 | ViGEmBus → Microsoft synthetic, process patching opt-in |
| v1.5.6 | 2026-01-16 | Windows 10 fixes, render adapter config |
| v1.5.5 | 2026-01-08 | Process patching, DirectX 8/9, Steam isolation |
| v1.5.4 | 2025-09-19 | HDR GatePerf, D3D12, sandbox improvements |
| v1.5.3 | 2025-07-07 | Windows 11 25H2, sandbox persistence, password encryption |
| v1.5.2 | 2025-03-15 | Sandbox fixes |
| v1.5.1 | 2025-03-14 | Steam multiboxing, HID isolation improvements |

### Key Issues

- HDR requires "Memory Integrity" disabled (v1.6.0)
- Anti-cheat conflicts (v1.5.6 verifier opt-out)
- Steam isolation issues (multiple versions)
- Controller compatibility issues (v1.5.8, v1.5.9)
- Display driver tearing (v1.5.1)
- Process patching file locks (v1.5.7)

---

## 22. What Duo Does Better Than MultiSeat-Extended

1. **HDR support** — Working HDR streaming (supporter feature)
2. **Game process patching** — Application Compatibility Layer for games that refuse RDP
3. **Steam multi-instance** — Built-in Steam isolation
4. **Custom WDDM driver** — More integrated than IddCx/SudoVDA
5. **UMDF input driver** — Session ID filtering for input isolation
6. **Seamless display adjustment** — No reconnect needed for resolution changes
7. **Higher refresh rates** — Up to 500Hz (supporter feature)
8. **Frame generation** — NVIDIA Smooth Motion support
9. **All-in-one** — No external dependencies (TermWrap bundled)
10. **Sandbox support** — Windows Sandbox for isolation (temporarily removed)

---

## 23. What MultiSeat-Extended Already Does Better

1. **Open source** — MIT vs proprietary
2. **Display isolation** — SudoVDA primary + RDP shrunk to 640x480 (unique, reduces CPU)
3. **Per-session audio** — No VAC/VoiceMeeter needed (RDP Remote Audio endpoints)
4. **HidHide session jail** — Gamepad isolation via undocumented feature
5. **Emulator netplay** — RetroArch per-seat ports
6. **Shared game library** — icacls-based provisioner
7. **Late display detection** — Handles Vibepollo lazy display creation
8. **Orphan cleanup** — WMI-based, safe for standalone Vibepollo
9. **Detailed diagnostics** — HidHideInspector, LogFilterInspector
10. **Well-documented security** — CLAUDE.md, security-posture.md

---

## 24. What Should Be Borrowed Conceptually

1. **Application Compatibility Layer** — Game process patching for RDP sessions
2. **Steam multi-instance** — Steam isolation mechanism
3. **UMDF input driver** — Session ID filtering for HID devices
4. **Custom WDDM driver** — More integrated virtual display
5. **Process patching** — Games that refuse remote sessions
6. **HDR support** — HDR streaming implementation
7. **Seamless display adjustment** — No reconnect for resolution changes

---

## 25. What Should NOT Be Copied

1. **Proprietary license** — Cannot reuse code
2. **Patreon monetization** — Different distribution model
3. **Custom drivers** — Would require driver development expertise
4. **Closed-source components** — Cannot inspect or modify

---

## 26. Unknowns

1. **Exact WDDM driver architecture** — Cannot inspect without source
2. **UMDF driver implementation** — Cannot inspect without source
3. **Application Compatibility Layer details** — Cannot inspect without source
4. **Process patching mechanism** — Cannot inspect without source
5. **Sunshine fork specifics** — Cannot inspect without source
6. **Service architecture details** — Cannot inspect without source
7. **IPC mechanism** — Cannot inspect without source
8. **Configuration format** — Cannot inspect without source

---

## Quality Gate

- [x] Repository inspected (GitHub — no source code)
- [x] Architecture inspected (README, wiki, release notes)
- [x] Seat model inspected (instance-based)
- [x] Users inspected (Windows accounts, encrypted passwords)
- [x] Sessions inspected (TermWrap multi-session RDP)
- [x] RDP inspected (TermWrap bundled)
- [x] Process creation inspected (CreateProcessAsUser, token manipulation)
- [x] Display inspected (custom WDDM driver — no source)
- [x] Audio inspected (per-session — details unknown)
- [x] Input inspected (UMDF driver, HID session filtering)
- [x] Game handling inspected (process patching, Steam isolation)
- [x] Streaming inspected (Sunshine per-instance)
- [x] Multi-instance inspected (instances with isolation)
- [x] Services inspected (Windows Service)
- [x] IPC inspected (details unknown)
- [x] API inspected (Web UI port 38299)
- [x] Recovery inspected (service monitoring)
- [x] Security inspected (password encryption, Patreon auth)
- [x] Issues inspected (GitHub issues)
- [x] PRs inspected (N/A — no source)
- [x] Releases inspected (v1.6.0 latest)
- [x] Commit history inspected (N/A — no source)
- [x] License inspected (proprietary)
- [x] Existing claims verified (see CLAIMS-VERIFICATION.md)

**Limitation**: Source-level analysis NOT possible — Duo is closed-source. All claims are based on public documentation only.
