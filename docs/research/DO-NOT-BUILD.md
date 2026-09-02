# Do Not Build List

**Date**: 2026-08-30
**Purpose**: Define what should NOT be built by MultiSeat-Extended

---

## Delegate to Existing Components

| Component | Why Delegate | Alternative |
|-----------|-------------|-------------|
| Video encoder | Complex, GPU-specific | Vibepollo (NVENC, AMF, FFmpeg) |
| Streaming protocol | Complex, protocol-specific | Vibepollo (RTSP, WebRTC, Moonlight) |
| Client pairing | Complex, certificate management | Vibepollo (PIN, certificates) |
| Video codec | Complex, hardware-specific | Vibepollo (H.264, HEVC, AV1) |
| Audio codec | Complex, standard | Vibepollo (Opus, AAC) |
| Input network protocol | Complex, protocol-specific | Vibepollo (Moonlight input) |
| Desktop capture | Complex, GPU-specific | Vibepollo (DDA, WGC, DXGI) |
| Audio capture | Complex, WASAPI-specific | Vibepollo (WASAPI loopback) |
| Gamepad forwarding | Complex, protocol-specific | Vibepollo (Moonlight controller) |
| Virtual display driver | Complex, kernel-mode | SudoVDA (IddCx driver) |
| RDP patching | Complex, termsrv-specific | TermWrap (DLL proxy) |
| Gamepad isolation | Complex, kernel-mode | HidHide (session jail) |

---

## Do NOT Build Custom

| Component | Why Not | Risk |
|-----------|---------|------|
| Custom video encoder | GPU-specific, complex | High maintenance |
| Custom streaming protocol | Protocol-specific, complex | Interoperability |
| Custom virtual display driver | Kernel-mode, complex | System stability |
| Custom input driver | Kernel-mode, complex | System stability |
| Custom audio driver | Kernel-mode, complex | System stability |
| Custom RDP wrapper | termsrv-specific, complex | Windows updates |
| Anti-cheat bypass | Ban risk, ethical issues | Account ban |
| Game-specific patches | Labor-intensive, fragile | Maintenance burden |
| Steam client patching | TOS violation, ban risk | Account ban |
| DRM bypass | Legal issues | Legal liability |

---

## Do NOT Duplicate

| Component | Why Not | Existing Solution |
|-----------|---------|-------------------|
| Streaming server | Vibepollo exists | Vibepollo |
| Capture engine | Vibepollo exists | Vibepollo |
| Encoder engine | Vibepollo exists | Vibepollo |
| Client protocol | Vibepollo exists | Vibepollo |
| Pairing system | Vibepollo exists | Vibepollo |
| Web UI for provider | Vibepollo exists | Vibepollo Web UI |
| Gamepad forwarding | Vibepollo exists | Vibepollo native |

---

## Do NOT Integrate (License Issues)

| Component | Why Not | License |
|-----------|---------|---------|
| Helios code | GPLv3 (cannot embed in MIT) | GPLv3 |
| Apollo code | GPLv3 (cannot embed in MIT) | GPLv3 |
| Vibepollo code | GPLv3 (cannot embed in MIT) | GPLv3 |
| Sunshine code | GPLv3 (cannot embed in MIT) | GPLv3 |
| rdpwrap code | GPL-2.0 (cannot embed in MIT) | GPL-2.0 |

---

## Do NOT Over-Engineer

| Component | Why Not | Right Size |
|-----------|---------|-----------|
| Microservices architecture | Single-process is simpler | Monolith with modules |
| gRPC between components | Direct method calls are simpler | In-process calls |
| Database for state | File-based config is simpler | JSON files |
| Message queue | Direct calls are simpler | Event bus |
| Container orchestration | Windows Service is simpler | SCM-managed |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Vibepollo handles streaming | VibepolloManager | VERIFIED |
| SudoVDA handles virtual display | VirtualDisplayManager | VERIFIED |
| TermWrap handles RDP patching | install-prerequisites.ps1 | VERIFIED |
| HidHide handles gamepad isolation | HidHideConfigurator | VERIFIED |
| GPLv3 cannot embed in MIT | License analysis | VERIFIED |
| Custom drivers are risky | Driver development expertise | VERIFIED (INFERENCE) |
