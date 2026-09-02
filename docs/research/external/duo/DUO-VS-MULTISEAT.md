# Duo vs MultiSeat-Extended Comparison

**Based on**: Duo public sources (README, releases, wiki, issues) + MultiSeat-Extended source code
**Date**: 2026-08-30
**Limitation**: Duo is CLOSED-SOURCE — comparison based on public documentation only

---

## Executive Summary

Duo and MultiSeat-Extended solve the same problem: **multiseat gaming/streaming on a single Windows PC**. However, they take fundamentally different approaches:

- **Duo**: Proprietary, all-in-one solution with custom drivers, freemium model
- **MultiSeat-Extended**: Open-source, modular architecture, MIT license

---

## Capability Comparison

| Capability | Duo | MultiSeat-Extended | Evidence | Gap |
|------------|-----|-------------------|----------|-----|
| **Users** | Windows accounts | Windows accounts | Duo: Setup Guide; MultiSeat: AccountManager.cs | EQUAL |
| **Sessions** | TermWrap (bundled) | TermWrap (external) | Duo: README; MultiSeat: RdpWrapper.cs | EQUAL |
| **RDP** | TermWrap multi-session | TermWrap multi-session | Duo: README; MultiSeat: install-prerequisites.ps1 | EQUAL |
| **Seat lifecycle** | Instance creation wizard | 9-step provisioning pipeline | Duo: Setup Guide; MultiSeat: SeatManager.cs | Duo simpler; MultiSeat more granular |
| **Process creation** | CreateProcessAsUser (inferred) | CreateProcessAsUser (verified) | Duo: issue #544; MultiSeat: ProcessInjector.cs | EQUAL |
| **Display** | Custom WDDM driver | SudoVDA (IddCx) + display isolation | Duo: README; MultiSeat: SeatManager.ApplyDisplayIsolationAsync | Duo more integrated; MultiSeat unique isolation |
| **Display isolation** | Proprietary | SudoVDA primary + RDP shrunk to 640x480 | Duo: unknown; MultiSeat: SeatManager.cs | MultiSeat unique (reduces CPU ~70% to <5%) |
| **Display adjustment** | Seamless (no reconnect) | Requires reconnect | Duo: README; MultiSeat: SetResolutionAsync | Duo better |
| **HDR** | YES (supporter, Windows 11 23H2+) | NO (probe only, no-op) | Duo: README; MultiSeat: MultiSeatOptions.EnableHdr | Duo better |
| **Refresh rate** | Up to 500Hz (supporter) | Up to configured fps | Duo: README; MultiSeat: seat.Fps | Duo higher ceiling |
| **Audio** | Per-session (inferred) | Per-session (RDP Remote Audio) | Duo: architecture; MultiSeat: SeatManager.cs | EQUAL |
| **Audio isolation** | Per-session (inferred) | Per-session (RDP Remote Audio) | Duo: architecture; MultiSeat: SeatManager.cs | EQUAL |
| **Microphone** | Unknown | NO (PerSession trade-off) | Duo: unknown; MultiSeat: MultiSeatOptions.cs | UNKNOWN |
| **Input isolation** | UMDF driver (session ID filtering) | HidHide session jail (optional) | Duo: v1.5.1; MultiSeat: HidHideConfigurator.cs | Duo mandatory; MultiSeat optional |
| **KB/M isolation** | UMDF driver | InputHookManager (no-op) | Duo: v1.5.1; MultiSeat: InputHookManager.cs | Duo better |
| **Gamepad isolation** | UMDF driver (session ID filtering) | HidHide session jail (optional) | Duo: v1.5.1; MultiSeat: HidHideConfigurator.cs | Duo mandatory; MultiSeat optional |
| **Virtual controller** | Microsoft synthetic API (v1.5.7+) | ViGEm (optional) | Duo: v1.5.7; MultiSeat: ControllerManager.cs | Duo newer; MultiSeat optional |
| **Game launching** | Unknown | ProcessInjector (CreateProcessAsUser) | Duo: unknown; MultiSeat: ProcessInjector.cs | UNKNOWN |
| **Game mutex isolation** | Application Compatibility Layer | NO | Duo: v1.5.5; MultiSeat: ARCHITECTURE-PROBLEMS.md | Duo better |
| **Steam multi-instance** | Built-in | NO | Duo: README; MultiSeat: ARCHITECTURE-PROBLEMS.md | Duo better |
| **Process patching** | Application Compatibility Layer | NO | Duo: v1.5.5, v1.5.7; MultiSeat: ARCHITECTURE-PROBLEMS.md | Duo better |
| **Streaming** | Sunshine (bundled, patched) | Vibepollo (external) | Duo: README; MultiSeat: VibepolloManager.cs | EQUAL |
| **Multi-instance** | Built-in | Built-in | Duo: README; MultiSeat: SeatManager.cs | EQUAL |
| **Health checks** | Unknown | SessionHealthCheck (5s) | Duo: unknown; MultiSeat: SessionHealthCheck.cs | MultiSeat documented |
| **Auto-restart** | Unknown | VibepolloManager.RestartAsync (max 3) | Duo: unknown; MultiSeat: VibepolloManager.cs | MultiSeat documented |
| **Orphan cleanup** | Unknown | WMI-based | Duo: unknown; MultiSeat: MultiSeatWorker.cs | MultiSeat documented |
| **Display restoration** | YES (but issues reported) | Late display detection | Duo: v1.5.3; MultiSeat: TryLateDisplayDetectionAsync | EQUAL |
| **Recovery** | Unknown | Best-effort teardown + auto-restart | Duo: unknown; MultiSeat: SeatManager.cs | MultiSeat documented |
| **Security** | Password encryption (v1.5.3+) | DPAPI + ACL hardening | Duo: v1.5.3; MultiSeat: AccountManager.cs | MultiSeat more transparent |
| **API** | Web UI (port 38299) | ASP.NET Core Minimal API (port 9550) | Duo: Setup Guide; MultiSeat: ApiServer.cs | EQUAL |
| **Dashboard** | WPF Manager + Web UI | React SPA | Duo: Setup Guide; MultiSeat: MultiSeat.Dashboard | EQUAL |
| **Authentication** | Patreon-based (supporter features) | API key | Duo: README; MultiSeat: ApiServer.cs | Different models |
| **HTTPS** | Unknown | NO (plaintext HTTP) | Duo: unknown; MultiSeat: MultiSeatOptions.cs | UNKNOWN |
| **Logging** | Event IDs (v1.5.7) | Windows Event Log | Duo: v1.5.7; MultiSeat: MultiSeatWorker.cs | EQUAL |
| **Diagnostics** | Unknown | HidHideInspector, LogFilterInspector | Duo: unknown; MultiSeat: Diagnostics/ | MultiSeat better documented |
| **Emulator netplay** | Unknown | RetroArch per-seat ports | Duo: unknown; MultiSeat: RetroArchConfigSeeder.cs | MultiSeat unique |
| **Shared game library** | Unknown | icacls-based provisioner | Duo: unknown; MultiSeat: SharedLibraryProvisioner.cs | MultiSeat unique |
| **License** | Proprietary (freemium) | MIT | Duo: GitHub; MultiSeat: LICENSE | MultiSeat open-source |

---

## Architecture Comparison

### Duo Architecture (Inferred)

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

### MultiSeat-Extended Architecture (Verified)

```
MultiSeat.Service (Windows Service, SYSTEM)
    ├── AccountManager (Windows accounts)
    ├── SessionLauncher (RDP loopback)
    ├── ProcessInjector (CreateProcessAsUser)
    ├── VirtualDisplayManager (SudoVDA)
    ├── VibepolloManager (streaming server)
    ├── VibepolloConfigBuilder (configuration)
    ├── PortAllocator (port blocks)
    ├── FirewallManager (per-seat)
    ├── ControllerManager (ViGEm)
    ├── InputRouter (XInput)
    ├── InputHookManager (KB/M hooks)
    ├── HidHideConfigurator (gamepad isolation)
    ├── OnConnectAppLauncher (launch-on-connect)
    ├── SessionHealthCheck (5s)
    └── Per-seat sessions
         ├── User account
         ├── Windows session
         ├── Virtual display (SudoVDA)
         ├── Audio device (RDP Remote Audio)
         ├── Input devices
         ├── Game processes
         └── Vibepollo instance
```

---

## Key Differences

### 1. Display Isolation

**Duo**: Custom WDDM driver (proprietary)
- More integrated
- Seamless display adjustment (no reconnect)

**MultiSeat-Extended**: SudoVDA primary + RDP shrunk to 640x480
- Unique approach
- Reduces TermService CPU from ~70% to <5%
- Requires reconnect for resolution changes

### 2. Input Isolation

**Duo**: UMDF driver with session ID filtering
- Mandatory for all instances
- Custom driver (proprietary)

**MultiSeat-Extended**: HidHide session jail
- Optional (default off)
- Undocumented HidHide feature
- Console-side Vibepollo pad ambiguity

### 3. Game Compatibility

**Duo**: Application Compatibility Layer
- Patches games that refuse RDP sessions
- Steam multi-instance
- Process patching

**MultiSeat-Extended**: No game patching
- Games must work in RDP sessions natively
- No Steam multi-instance support

### 4. HDR Support

**Duo**: Working HDR streaming (supporter feature)
- Windows 11 23H2+
- Custom display driver support

**MultiSeat-Extended**: HDR probe only (no-op)
- EnableHdr flag exists but does nothing
- VidPN SOURCE mode stays SDR

### 5. Open Source vs Proprietary

**Duo**: Proprietary (freemium)
- Cannot inspect source code
- Cannot modify or extend
- Patreon monetization

**MultiSeat-Extended**: MIT license
- Full source code available
- Can inspect, modify, extend
- Community-driven

---

## What Duo Does Better

1. **HDR support** — Working HDR streaming
2. **Game process patching** — Application Compatibility Layer
3. **Steam multi-instance** — Built-in isolation
4. **Seamless display adjustment** — No reconnect for resolution changes
5. **Custom WDDM driver** — More integrated than IddCx/SudoVDA
6. **UMDF input driver** — Mandatory session ID filtering
7. **Higher refresh rates** — Up to 500Hz (supporter feature)
8. **All-in-one** — No external dependencies (TermWrap bundled)
9. **Simpler setup** — Wizard-based instance creation

---

## What MultiSeat-Extended Does Better

1. **Open source** — MIT vs proprietary
2. **Display isolation** — SudoVDA primary + RDP shrunk (unique, reduces CPU)
3. **Per-session audio** — No VAC/VoiceMeeter needed (RDP Remote Audio)
4. **HidHide session jail** — Gamepad isolation via undocumented feature
5. **Emulator netplay** — RetroArch per-seat ports
6. **Shared game library** — icacls-based provisioner
7. **Late display detection** — Handles Vibepollo lazy display creation
8. **Orphan cleanup** — WMI-based, safe for standalone Vibepollo
9. **Detailed diagnostics** — HidHideInspector, LogFilterInspector
10. **Well-documented security** — CLAUDE.md, security-posture.md
11. **Health checks** — SessionHealthCheck (5s interval)
12. **Crash recovery** — Auto-restart with limits (max 3)
13. **Modular architecture** — Can swap streaming providers
14. **API key authentication** — Simple, effective
15. **WebSocket real-time** — /ws/seats broadcast

---

## What Should Be Borrowed Conceptually

### From Duo to MultiSeat-Extended

1. **Application Compatibility Layer** — Game process patching for RDP sessions
   - Concept: Patch games that refuse to run in remote sessions
   - Implementation: Could use Windows Application Compatibility Toolkit
   - Risk: Game-specific, may break with updates

2. **Steam multi-instance** — Steam isolation mechanism
   - Concept: Allow multiple Steam instances on same PC
   - Implementation: Could use process patching or Steam config isolation
   - Risk: Steam updates may break

3. **UMDF input driver** — Session ID filtering for HID devices
   - Concept: Filter input devices by session ID
   - Implementation: Would require custom driver development
   - Risk: Driver signing, maintenance burden

4. **Seamless display adjustment** — No reconnect for resolution changes
   - Concept: Change resolution without disconnecting session
   - Implementation: Would require display driver support
   - Risk: Complex, driver-dependent

5. **HDR support** — HDR streaming implementation
   - Concept: Stream HDR content to compatible clients
   - Implementation: Would require display driver + encoding support
   - Risk: Hardware/driver dependent

---

## What Should NOT Be Copied

1. **Proprietary license** — Cannot reuse code
2. **Patreon monetization** — Different distribution model
3. **Custom drivers** — Would require driver development expertise
4. **Closed-source components** — Cannot inspect or modify
5. **All-in-one approach** — MultiSeat's modular architecture is more flexible

---

## Unknowns

1. **Duo's exact session creation mechanism** — Cannot inspect without source
2. **Duo's audio isolation implementation** — Cannot inspect without source
3. **Duo's process creation details** — Cannot inspect without source
4. **Duo's IPC mechanism** — Cannot inspect without source
5. **Duo's configuration format** — Cannot inspect without source
6. **Duo's security implementation** — Cannot inspect without source
7. **Duo's display driver architecture** — Cannot inspect without source
8. **Duo's input driver architecture** — Cannot inspect without source
