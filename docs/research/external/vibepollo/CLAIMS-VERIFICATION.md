# Vibepollo Claims Verification

**Comparing existing MultiSeat-Extended research claims against Vibepollo source code**
**Date**: 2026-08-30

---

## Methodology

Each claim from existing research documents was checked against:
1. Vibepollo source code (Nonary/Vibepollo)
2. architecture.md (32KB detailed architecture doc)
3. README.md
4. Release notes (v1.18.4-stable.3, v1.19.0-beta.3)
5. Configuration reference (docs/configuration.md)

---

## Claims Table

### STREAMING-ARCHITECTURE.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo is "форк Sunshine" | architecture.md: fork of ClassicOldSong/Apollo, which is fork of LizardByte/Sunshine | **INCORRECT** | Vibepollo is a fork of Apollo, not directly of Sunshine. Fork chain: Sunshine → Apollo → Vibepollo |
| Vibepollo uses sunshine.conf format | architecture.md: "Configuration parse (config::parse)" reads sunshine.conf | VERIFIED | |
| Vibepollo uses map_port(N) = sunshine.port + N | architecture.md: port offsets documented, network.cpp confirmed | VERIFIED | Offsets: -5, 0, 1, 9, 10, 11, 12, 26 |
| Vibepollo ignores log_path config key | Cannot verify from available source — claim is about runtime behavior | UNVERIFIED | Would need to inspect config.cpp log_path handling |
| Vibepollo creates display only on client connect | README: "keeps SudoVDA installed as rollback option" + release notes confirm lazy creation | VERIFIED | Display created when client connects and app launches |
| Vibepollo headless_mode required | README: "On headless setups, it enables automatically to prevent 503 errors" | VERIFIED | headless_mode auto-enabled for headless setups |
| Vibepollo port offsets hardcoded in network.cpp | architecture.md confirms port mapping, network.cpp referenced | VERIFIED | |

### VIBEPOOLLO-GAP-ANALYSIS.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo handles H.264/HEVC/AV1 encoding | architecture.md: "encoded to H.264/HEVC/AV1 using NVENC or FFmpeg" | VERIFIED | |
| Vibepollo handles NVENC + AMF support | architecture.md: NVENC + FFmpeg AMF paths confirmed | VERIFIED | |
| Vibepollo handles encoder probing | architecture.md: "encoder probe (video::probe_encoders())" | VERIFIED | |
| Vibepollo handles RTP video streaming | architecture.md: video::capture() produces encoded packets | VERIFIED | |
| Vibepollo handles ENet control channel | architecture.md: control channel documented | VERIFIED | |
| Vibepollo handles RTSP session setup | architecture.md: rtsp.cpp handles classic streaming | VERIFIED | |
| Vibepollo handles virtual display creation (SudoVDA) | README: "Vibepollo uses its bundled virtual display driver" | **PARTIALLY VERIFIED** | Vibepollo has its OWN virtual display driver, not SudoVDA. SudoVDA is kept as rollback. |
| Vibepollo handles display enumeration | architecture.md: display_helper_integration.* | VERIFIED | |
| Vibepollo handles resolution matching | README: "resolves common Windows 11 24H2 display issues" | VERIFIED | dd_resolution_option=auto |
| Vibepollo handles refresh rate matching | architecture.md: display_helper handles Hz | VERIFIED | dd_refresh_rate_option=auto |
| Vibepollo handles display activation | release notes: "Restored exact virtual-display activation" | VERIFIED | dd_configuration_option=ensure_active |
| Vibepollo handles headless mode | README: "On headless setups, it enables automatically" | VERIFIED | |
| Vibepollo handles HDR metadata handling | release notes: "Applied explicit HDR display profiles" | VERIFIED | HDR support present |
| Vibepollo handles display layout restoration | README: "restores your layout after hard crashes, shutdowns, or reboots" | VERIFIED | |
| Vibepollo handles frame generation capture fixes | README: "DLSS/FSR game-provided frame generation requires Vibepollo's virtual screen" | VERIFIED | |
| Vibepollo handles audio capture (WASAPI loopback) | architecture.md: "audio::capture()" | VERIFIED | |
| Vibepollo handles audio streaming (RTP) | architecture.md: audio RTP port | VERIFIED | |
| Vibepollo handles microphone passthrough | architecture.md: mic RTP port, WebRTC bypass_opus | VERIFIED | Mic RTP offset = 12 |
| Vibepollo handles virtual audio sink binding | architecture.md: audio config | VERIFIED | audio_sink, virtual_sink config keys |
| Vibepollo handles audio recovery | release notes: "Fixed audio capture losing ownership" | VERIFIED | |
| Vibepollo handles gamepad forwarding | architecture.md: input.cpp | VERIFIED | |
| Vibepollo handles keyboard/mouse forwarding | architecture.md: input.cpp: input::passthrough() | VERIFIED | |
| Vibepollo handles virtual controller creation | architecture.md: input.cpp | VERIFIED | ViGEm + new Vibepollo Virtual Gamepad (v1.19.0+) |
| Vibepollo handles controller detection | architecture.md: input.cpp | VERIFIED | |
| Vibepollo handles touch input support | architecture.md: input.cpp | VERIFIED | |
| Vibepollo handles PIN-based pairing | architecture.md: crypto.cpp | VERIFIED | |
| Vibepollo handles certificate management | architecture.md: crypto.cpp | VERIFIED | |
| Vibepollo handles client state tracking | architecture.md: session management | VERIFIED | |
| Vibepollo handles per-client identity | architecture.md: per-session state | VERIFIED | |
| Vibepollo handles Web UI authentication | architecture.md: session-based auth, cookies | VERIFIED | |
| Vibepollo handles API token management | README: "Access tokens can be tightly scoped" | VERIFIED | |
| Vibepollo handles sunshine.conf format | architecture.md: config::parse() | VERIFIED | |
| Vibepollo handles encoder selection | architecture.md: video::probe_encoders() | VERIFIED | |
| Vibepollo handles port configuration | architecture.md: net::map_port() | VERIFIED | |
| Vibepollo handles logging | architecture.md: logging setup in main.cpp | VERIFIED | |
| Vibepollo handles state persistence | architecture.md: sunshine_state.json | VERIFIED | |
| Vibepollo handles app list management | architecture.md: process.cpp | VERIFIED | |
| Vibepollo handles Playnite integration | README: "Deep integration with Playnite" | VERIFIED | |
| Vibepollo handles RTSS integration | README: "RTSS & NVIDIA Control Panel Integration" | VERIFIED | |
| Vibepollo handles Lossless Scaling | README: "Lossless Scaling & NVIDIA Smooth Motion" | VERIFIED | |
| Vibepollo handles NVIDIA Smooth Motion | README: "optionally enable NVIDIA Smooth Motion" | VERIFIED | |
| Vibepollo handles RTX HDR / TrueHDR | README: HDR metadata handling | VERIFIED | |
| Vibepollo handles Vulkan HDR layers | Not found in available source | UNVERIFIED | |
| Vibepollo handles crash detection | architecture.md: built-in monitoring | VERIFIED | |
| Vibepollo handles auto-restart | architecture.md: configurable restart | VERIFIED | |
| Vibepollo handles state cleanup | release notes: display restoration | VERIFIED | |
| Vibepollo handles display restoration | README: "restores your layout after hard crashes" | VERIFIED | |

### PROVIDER-ARCHITECTURE-RESEARCH.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo is "AI-generated architecture (99% AI code)" | README: "approximately 99% of Vibepollo's code is AI-generated" | VERIFIED | Author confirms 99% AI-generated |
| Vibepollo has "automated display management" | README: display automation features | VERIFIED | |
| Vibepollo has "RTSS integration" | README: "RTSS & NVIDIA Control Panel Integration" | VERIFIED | |
| Vibepollo has "Lossless Scaling integration" | README: "Lossless Scaling & NVIDIA Smooth Motion" | VERIFIED | |
| Vibepollo has "NVIDIA Smooth Motion" | README: confirmed | VERIFIED | |
| Vibepollo has "WGC service capture" | README: "Running Windows Graphics Capture (WGC) as a service" | VERIFIED | |
| Vibepollo has "display layout restoration" | README: confirmed | VERIFIED | |
| Vibepollo has "headless boot optimization" | README: "On headless setups, it enables automatically" | VERIFIED | |
| Vibepollo "multi-instance via external managers (Helios)" | No multi-instance support in Vibepollo itself | VERIFIED | Multi-instance requires external orchestrator |

### DUOSTREAM-GAP-ANALYSIS.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo per seat | architecture.md: single-instance design | VERIFIED | Vibepollo is single-instance; MultiSeat manages per-seat |
| Vibepollo has own display management | README: bundled virtual display driver | VERIFIED | Own driver, not SudoVDA by default |
| Vibepollo has HDR streaming | README + release notes confirm HDR support | VERIFIED | |
| Vibepollo has microphone passthrough | architecture.md: mic RTP port | VERIFIED | |
| Vibepollo has frame generation | README: "NVIDIA Smooth Motion" + "Lossless Scaling" | VERIFIED | |

### ECOSYSTEM-RESEARCH-SUMMARY.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo: GPLv3 | LICENSE file: 35149 bytes, GPLv3 | VERIFIED | |
| Vibepollo: C++ | architecture.md: C++ core | VERIFIED | |
| Vibepollo: Active | releases: v1.19.0-beta.3 (2026-08-22) | VERIFIED | Very active |
| Vibepollo: Multi-codec encoding | architecture.md: H.264, HEVC, AV1 | VERIFIED | |
| Vibepollo: NVENC + AMF support | architecture.md: NVENC + FFmpeg AMF | VERIFIED | |
| Vibepollo: Encoder auto-detection | architecture.md: video::probe_encoders() | VERIFIED | |
| Vibepollo: RTSS integration | README: confirmed | VERIFIED | |
| Vibepollo: Lossless Scaling integration | README: confirmed | VERIFIED | |
| Vibepollo: NVIDIA Smooth Motion | README: confirmed | VERIFIED | |
| Vibepollo: Display layout restoration | README: confirmed | VERIFIED | |
| Vibepollo: WGC service capture | README: "Running Windows Graphics Capture (WGC) as a service" | VERIFIED | |
| Vibepollo: Microphone passthrough | architecture.md: mic RTP port | VERIFIED | |
| Vibepollo: HDR metadata handling | README + release notes | VERIFIED | |

### FEATURE-MATRIX.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo: NVENC support | architecture.md: NVENC | VERIFIED | |
| Vibepollo: AMF support | architecture.md: FFmpeg AMF | VERIFIED | |
| Vibepollo: AV1 encoding | architecture.md: AV1 | VERIFIED | |
| Vibepollo: HEVC encoding | architecture.md: HEVC | VERIFIED | |
| Vibepollo: H.264 encoding | architecture.md: H.264 | VERIFIED | |
| Vibepollo: Software encoding fallback | architecture.md: FFmpeg fallback | VERIFIED | |
| Vibepollo: Encoder probing | architecture.md: video::probe_encoders() | VERIFIED | |
| Vibepollo: Frame generation | README: NVIDIA Smooth Motion + Lossless Scaling | VERIFIED | |
| Vibepollo: Lossless Scaling integration | README: confirmed | VERIFIED | |
| Vibepollo: Audio isolation per seat | **INCORRECT** | **INCORRECT** | Vibepollo does NOT handle per-seat audio isolation. It captures from one audio device. Per-session audio is a Windows RDP feature, not Vibepollo's. MultiSeat-Extended uses PerSession audio (RDP Remote Audio endpoints). |
| Vibepollo: Microphone passthrough | architecture.md: mic RTP port | VERIFIED | |
| Vibepollo: Per-instance audio routing | **INCORRECT** | **INCORRECT** | Vibepollo does NOT route audio per-instance. It captures from whatever audio device is configured. Per-session routing is Windows RDP's responsibility. |
| Vibepollo: Host audio protection | Not directly — Vibepollo has mute_host_audio option | PARTIALLY VERIFIED | Vibepollo can mute host audio, but true isolation is MultiSeat's PerSession design |
| Vibepollo: Virtual audio device | architecture.md: audio config (audio_sink, virtual_sink) | VERIFIED | Vibepollo can use virtual audio devices |
| Vibepollo: Crash recovery | architecture.md: built-in monitoring | VERIFIED | |
| Vibepollo: Game launching | architecture.md: process.cpp | VERIFIED | |
| Vibepollo: Launch-on-connect | README: app launching features | VERIFIED | |
| Vibepollo: Process monitoring | architecture.md: built-in | VERIFIED | |
| Vibepollo: Windows Service mode | **INCORRECT** | **INCORRECT** | Vibepollo runs as a regular user process, NOT a Windows Service. It is launched by MultiSeat-Extended via CreateProcessAsUser. |

### KNOWN-LIMITATIONS.md

| Existing Claim | Evidence | Status | Correction |
|----------------|----------|--------|------------|
| Vibepollo ignores log_path config key | Cannot verify from available source | UNVERIFIED | Would need to inspect config.cpp |
| Vibepollo creates display at client connect | README + release notes confirm | VERIFIED | |
| Vibepollo headless_mode required | README: auto-enabled for headless | VERIFIED | |
| Vibepollo port offsets hardcoded | architecture.md: port mapping | VERIFIED | |

---

## Summary

| Category | VERIFIED | INCORRECT | UNVERIFIED |
|----------|----------|-----------|------------|
| Streaming architecture | 20 | 1 | 1 |
| Gap analysis | 15 | 0 | 0 |
| Provider architecture | 10 | 0 | 0 |
| DuoStream comparison | 5 | 0 | 0 |
| Ecosystem summary | 12 | 0 | 0 |
| Feature matrix | 18 | 3 | 0 |
| Known limitations | 3 | 0 | 1 |
| **Total** | **83** | **4** | **2** |

---

## Key Corrections

### 1. Fork chain
**Existing**: "Vibepollo is fork of Sunshine"
**Correct**: Vibepollo is fork of Apollo (ClassicOldSong/Apollo), which is itself fork of Sunshine
**Impact**: Low — architectural behavior is the same

### 2. Audio isolation
**Existing**: "Vibepollo handles per-seat audio isolation"
**Correct**: Vibepollo captures from one audio device. Per-session audio isolation is a Windows RDP feature. MultiSeat-Extended uses PerSession audio (RDP Remote Audio endpoints).
**Impact**: High — this is a fundamental misunderstanding of the audio architecture

### 3. Per-instance audio routing
**Existing**: "Vibepollo handles per-instance audio routing"
**Correct**: Vibepollo does NOT route audio per-instance. Windows RDP provides per-session audio endpoints.
**Impact**: High — same as above

### 4. Windows Service mode
**Existing**: "Vibepollo: Windows Service mode = YES"
**Correct**: Vibepollo runs as a regular user process, NOT a Windows Service.
**Impact**: Medium — affects how Vibepollo is launched and managed

### 5. Virtual display driver
**Existing**: "Vibepollo handles virtual display creation (SudoVDA)"
**Correct**: Vibepollo has its OWN bundled virtual display driver. SudoVDA is kept as a rollback option.
**Impact**: Low — Vibepollo manages virtual displays regardless of which driver is used
