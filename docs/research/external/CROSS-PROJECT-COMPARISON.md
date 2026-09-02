# Cross-Project Comparison

**Date**: 2026-08-30
**Purpose**: Comprehensive comparison of all researched projects

---

## Projects Compared

| Project | Type | License | Language | Status |
|---------|------|---------|----------|--------|
| MultiSeat-Extended | Multiseat platform | MIT | C# (.NET 9) | Active |
| Vibepollo | Streaming server | GPLv3 | C++ | Very active |
| Apollo | Streaming server | GPLv3 | C++ | Active |
| Helios | Instance manager | GPLv3 | C# (.NET 8) | Active |
| Duo | Multiseat platform | Proprietary | Unknown | Active |
| TermWrap | RDP patching | MIT | C++ | Active |
| neo_multiseat | PowerShell scripts | MIT | PowerShell | Active |
| LuaTools | Steam tool | Unknown | C# (.NET 8) | Active |

---

## Capability Matrix

| Capability | MultiSeat-Extended | Vibepollo | Apollo | Helios | Duo | TermWrap | neo_multiseat |
|------------|-------------------|-----------|--------|--------|-----|----------|---------------|
| **Users** | ✅ Windows accounts | ❌ Single | ❌ Single | ❌ Single | ✅ Custom | ❌ | ✅ Script |
| **Sessions** | ✅ RDP loopback | ❌ Single | ❌ Single | ❌ Single | ✅ TermWrap | ✅ Patching | ✅ RDPWrap |
| **Seat lifecycle** | ✅ 9-step pipeline | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **RDP** | ✅ TermWrap | ❌ | ❌ | ❌ | ✅ TermWrap | ✅ Patching | ✅ RDPWrap |
| **Display** | ✅ SudoVDA primary | ✅ Own driver | ✅ SudoVDA | ❌ | ✅ Custom WDDM | ❌ | ❌ |
| **HDR** | ❌ (no-op) | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ |
| **High Hz** | ✅ (SudoVDA) | ✅ | ✅ | ❌ | ✅ (500Hz) | ❌ | ❌ |
| **Audio** | ✅ RDP Remote Audio | ✅ WASAPI | ✅ WASAPI | ✅ Per-instance | ✅ Per-session | ❌ | ❌ |
| **Input** | ✅ HidHide session jail | ❌ | ❌ | ❌ | ✅ UMDF driver | ❌ | ❌ |
| **Gamepad isolation** | ✅ HidHide | ❌ | ❌ | ❌ | ✅ UMDF | ❌ | ❌ |
| **Game launch** | ✅ SeatManager | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Game isolation** | ❌ | ❌ | ❌ | ❌ | ✅ App Compat Layer | ❌ | ❌ |
| **Process tracking** | ❌ | ❌ | ❌ | ✅ PID tracking | ✅ | ❌ | ❌ |
| **Streaming** | ✅ Vibepollo | ✅ Moonlight | ✅ Moonlight | ❌ | ✅ Sunshine | ❌ | ❌ |
| **Provider abstraction** | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Multi-instance** | ✅ Automated | ❌ Single | ❌ Single | ✅ Manual | ✅ | ❌ | ❌ |
| **Health checks** | ✅ SessionHealthCheck | ✅ | ✅ | ✅ Guardian loop | ✅ | ❌ | ❌ |
| **Crash recovery** | ✅ Auto-restart | ✅ | ✅ | ✅ Backoff | ✅ | ❌ | ❌ |
| **API** | ✅ ASP.NET Core | ✅ REST | ✅ REST | ✅ Named Pipe | ✅ Web UI | ❌ | ❌ |
| **Web UI** | ✅ React | ✅ | ✅ | ✅ WPF | ✅ | ❌ | ❌ |
| **Security** | ✅ DPAPI + ACL | ✅ | ✅ | ✅ SYSTEM | ✅ | ⚠️ Registry | ❌ |
| **Steam multi-instance** | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Game process patching** | ❌ | ❌ | ❌ | ❌ | ✅ App Compat Layer | ❌ | ❌ |

---

## Detailed Comparison

### 1. User Management

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | Windows accounts (AccountManager) | AccountManager.cs |
| Vibepollo | Single user | Single-user daemon |
| Apollo | Single user | Single-user daemon |
| Helios | Single user | Single-user daemon |
| Duo | Custom accounts | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | PowerShell scripts | User creation scripts |

### 2. Session Management

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | RDP loopback (SessionLauncher) | SessionLauncher.cs |
| Vibepollo | Single session | Single-session daemon |
| Apollo | Single session | Single-session daemon |
| Helios | Single session | Single-session daemon |
| Duo | TermWrap (bundled) | Unknown (proprietary) |
| TermWrap | termsrv.dll patching | TermWrap.dll |
| neo_multiseat | RDPWrap | PowerShell scripts |

### 3. Display Isolation

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | SudoVDA primary + RDP shrunk | SudoVDA integration |
| Vibepollo | Own bundled driver | Own driver |
| Apollo | SudoVDA (built-in) | README |
| Helios | N/A | No display management |
| Duo | Custom WDDM driver | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No display management |

### 4. Audio Isolation

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | RDP Remote Audio (per-session) | PerSession audio |
| Vibepollo | WASAPI loopback | WASAPI capture |
| Apollo | WASAPI loopback | WASAPI capture |
| Helios | Per-instance audio routing | AudioDevice config |
| Duo | Per-session audio | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No audio management |

### 5. Input Isolation

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | HidHide session jail | HidHideConfigurator |
| Vibepollo | N/A | No input management |
| Apollo | N/A | No input management |
| Helios | N/A | No input management |
| Duo | UMDF input driver | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No input management |

### 6. Streaming

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | Vibepollo (external process) | VibepolloManager |
| Vibepollo | Moonlight protocol | RTSP + WebRTC |
| Apollo | Moonlight protocol | RTSP + WebRTC |
| Helios | N/A | Instance manager only |
| Duo | Sunshine (patched) | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No streaming |

### 7. Multi-Instance

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | Automated (SeatManager) | 9-step pipeline |
| Vibepollo | Manual (single instance) | Single-user daemon |
| Apollo | Manual (single instance) | Single-user daemon |
| Helios | Manual (WPF UI) | Instance management |
| Duo | Automated | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | Manual (scripts) | PowerShell automation |

### 8. Health Monitoring

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | SessionHealthCheck (5s) | SessionHealthCheck.cs |
| Vibepollo | Built-in monitoring | Unknown |
| Apollo | Built-in monitoring | Unknown |
| Helios | Guardian loop (5s) | ProcessManager.cs |
| Duo | Unknown | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No health monitoring |

### 9. Crash Recovery

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | Auto-restart with limits | MaxRestartAttempts |
| Vibepollo | Auto-restart | Unknown |
| Apollo | Auto-restart | Unknown |
| Helios | Guardian loop with backoff | ProcessManager.cs |
| Duo | Unknown | Unknown (proprietary) |
| TermWrap | N/A | RDP patching only |
| neo_multiseat | N/A | No crash recovery |

### 10. Security

| Project | Approach | Evidence |
|---------|----------|----------|
| MultiSeat-Extended | DPAPI + ACL + API key | Security audit |
| Vibepollo | Basic security | Unknown |
| Apollo | Basic security | Unknown |
| Helios | SYSTEM token | ProcessLauncher.cs |
| Duo | Unknown | Unknown (proprietary) |
| TermWrap | Registry modification | DLL proxy |
| neo_multiseat | N/A | No security |

---

## Strengths and Weaknesses

### MultiSeat-Extended

**Strengths**:
- Open source (MIT)
- Automated 9-step pipeline
- Display isolation (SudoVDA primary + RDP shrunk)
- Per-session audio (RDP Remote Audio)
- Gamepad isolation (HidHide session jail)
- Health checks + crash recovery
- Well-documented security

**Weaknesses**:
- No HDR support (EnableHdr is no-op)
- No game process patching
- No Steam multi-instance
- InputHookManager is no-op
- No provider abstraction
- No process tracking

### Vibepollo

**Strengths**:
- Active development (multiple releases per week)
- Own virtual display driver
- RTSS integration
- Lossless Scaling
- NVIDIA Smooth Motion
- HDR support

**Weaknesses**:
- Single-user only
- GPLv3 license
- 99% AI-generated code
- No multi-seat support

### Apollo

**Strengths**:
- Larger community (10.7k stars)
- Built-in SudoVDA
- Per-client fixed identity
- Permission management
- HDR support

**Weaknesses**:
- Single-user only
- GPLv3 license
- Slower development
- Virtual display conflicts (Issue #874)

### Helios

**Strengths**:
- Named Pipe IPC pattern
- Per-instance config isolation
- Guardian loop with backoff
- Residual process adoption
- SYSTEM token launch

**Weaknesses**:
- No session management
- No display isolation
- No input isolation
- GPLv3 license
- WPF dependency

### Duo

**Strengths**:
- HDR support
- Game process patching
- Steam multi-instance
- Seamless display adjustment
- Custom WDDM driver
- UMDF input driver

**Weaknesses**:
- Proprietary
- Closed-source
- Freemium model
- Cannot inspect or modify

### TermWrap

**Strengths**:
- MIT license
- Auto offset discovery
- Survives Windows updates
- User-mode only
- Camera/USB/audio features

**Weaknesses**:
- Single purpose (RDP patching only)
- PDB dependency
- Registry modification
- DLL proxying

### neo_multiseat

**Strengths**:
- Simple (PowerShell scripts)
- Transparent
- Automated RDPWrap recovery

**Weaknesses**:
- No streaming
- No display isolation
- No input isolation
- No health monitoring

---

## Evidence Summary

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| MultiSeat-Extended MIT license | LICENSE file | MIT text | VERIFIED |
| Vibepollo GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| Apollo GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| Helios GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| TermWrap MIT license | LICENSE file | MIT text | VERIFIED |
| neo_multiseat MIT license | LICENSE file | MIT text | VERIFIED |
| Duo proprietary | README | No source code | VERIFIED |
| MultiSeat 9-step pipeline | SeatManager.cs | ProvisionSeatAsync | VERIFIED |
| MultiSeat SudoVDA display | SudoVDA integration | Virtual display | VERIFIED |
| MultiSeat RDP Remote Audio | PerSession audio | Audio isolation | VERIFIED |
| MultiSeat HidHide session jail | HidHideConfigurator | Gamepad isolation | VERIFIED |
| MultiSeat health checks | SessionHealthCheck.cs | 5s interval | VERIFIED |
| MultiSeat crash recovery | MaxRestartAttempts | Auto-restart | VERIFIED |
| Vibepollo active development | Releases | Multiple per week | VERIFIED |
| Vibepollo own driver | README | Own bundled driver | VERIFIED |
| Apollo built-in SudoVDA | README | "Apollo uses SudoVDA" | VERIFIED |
| Apollo permission system | README | "FIRST client gets FULL permissions" | VERIFIED |
| Helios Named Pipe IPC | SpawnerWorker.cs | JSON protocol | VERIFIED |
| Helios SYSTEM token launch | ProcessLauncher.cs | CreateProcessAsUser | VERIFIED |
| TermWrap auto offset | README | "Integrated RDPWrapOffsetFinder" | VERIFIED |
| TermWrap survives updates | README | "patch offsets are automatically searched" | VERIFIED |
