# Vibepollo Research Summary

**Repository**: Nonary/Vibepollo
**Based on**: Source-level analysis of architecture.md, source code, releases, README
**Date**: 2026-08-30

---

## 1. What is Vibepollo

Vibepollo is an **AI-enhanced fork of Apollo** (ClassicOldSong/Apollo), which is itself a fork of Sunshine (LizardByte/Sunshine). It is a **single-user streaming server** for Windows that captures, encodes, and streams game/desktop content to Moonlight clients.

**Key facts**:
- **License**: GPLv3
- **Language**: C++ (core), Vue.js/TypeScript (Web UI)
- **99% AI-generated code** (confirmed by author)
- **Current versions**: v1.18.4-stable.3, v1.19.0-beta.3
- **Active development**: Very active (multiple releases per week)

---

## 2. Architecture

### Single-Process Daemon
Vibepollo runs as a **single process** with multiple threads:
- HTTP servers (GameStream, Web UI, REST API)
- Capture threads (video, audio)
- Encode threads (NVENC, AMF, FFmpeg)
- Media threads (RTSP, WebRTC)
- Input injection threads

### Thread Model
```
main.cpp
├── httpThread (nvhttp::start) — GameStream HTTP
├── configThread (confighttp::start) — Web UI + REST + WebRTC signaling
├── rtspThread (rtsp_stream::start) — Classic streaming
├── video::capture thread — Display capture + encode
├── audio::capture thread — Audio capture
├── media_thread_main — WebRTC media push
├── feedback_thread_main — Gamepad feedback
└── per-session capture/encode threads
```

### Cross-Thread Coordination
Uses `safe::mail_raw_t` mailbox abstraction for typed events/queues.

---

## 3. Processes

| Process | Purpose | User | Session | Privileges |
|---------|---------|------|---------|------------|
| sunshine.exe | Main daemon | Current user | Current session | User-level |
| (child encoders) | Video encoding | Same | Same | User-level |
| (Web UI) | Browser | Current user | Current session | User-level |

**Note**: Vibepollo does NOT run as a Windows Service. It is a regular user process.

---

## 4. Streaming

### Two Streaming Stacks

**1. Classic RTSP/GameStream** (src/stream.cpp, src/rtsp.cpp)
- Compatible with Moonlight clients
- RTSP session setup
- RTP video/audio/control
- Single-session (mutual exclusion with WebRTC)

**2. WebRTC** (src/webrtc_stream.cpp)
- Browser-based streaming
- WebRTC signaling via REST API
- Data channel for input
- Single-session (mutual exclusion with RTSP)

**Key constraint**: Classic RTSP and WebRTC are mutually exclusive — only one can be active at a time.

### Streaming Pipeline
```
Client (Moonlight/Browser)
    ↓
Network (RTSP/RTP or WebRTC)
    ↓
Sunshine daemon (single process)
    ↓
Capture (DDA, WGC, DXGI)
    ↓
Encode (NVENC, AMF, FFmpeg)
    ↓
GPU (hardware encoding)
    ↓
Display (virtual or physical)
```

---

## 5. Capture

### Video Capture Methods
- **Desktop Duplication API (DDA)**: Primary method on Windows
- **Windows Graphics Capture (WGC)**: Can run as service for better performance
- **DXGI**: Fallback method

### Display Selection
- User selects display from Web UI dropdown
- `output_name` config key points to specific display
- Vibepollo enumerates displays at startup
- Display UUID tracked via `sunshine_state.json`

### HDR Support
- HDR metadata handling confirmed
- Virtual display supports HDR profiles
- Falls back to SDR when needed

---

## 6. Encoder

### NVIDIA NVENC
- Primary encoder for NVIDIA GPUs
- H.264, HEVC, AV1 support
- Hardware-accelerated encoding
- Probe at startup: `video::probe_encoders()`

### AMD AMF
- FFmpeg AMF path (default on AMD)
- Native AMF as experimental option
- H.264, HEVC support

### Software Fallback
- FFmpeg software encoding
- Used when hardware encoding unavailable

---

## 7. Virtual Display

### Bundled Driver
- Vibepollo has its **OWN virtual display driver** (not SudoVDA by default)
- SudoVDA kept as rollback option
- Driver is signed (SignPath.io sponsorship)

### Display Management
- `src/platform/windows/virtual_display.*` — Driver management
- `src/platform/windows/display_helper_integration.*` — Display configuration
- Can capture from any GPU (including hybrid laptops)
- Auto-enables on headless setups

### Display Lifecycle
1. Vibepollo creates virtual display when client connects
2. Display UUID discovered from startup log
3. `output_name` config key set to UUID
4. Display restored after crashes/reboots

---

## 8. Audio

### WASAPI Loopback Capture
- `src/audio.cpp`: `audio::capture()`
- Captures from configured audio device
- Supports host audio muting (`mute_host_audio`)

### Microphone Passthrough
- Mic RTP port (offset +12)
- WebRTC: `bypass_opus=true` for raw PCM
- Standard Moonlight: mic RTP packets

### Audio Configuration
- `audio_sink`: Device to capture from
- `virtual_sink`: Virtual audio device
- `keep_sink_default`: Don't change system default
- `auto_capture_sink`: Auto-select capture device

**Important**: Vibepollo does NOT provide per-session audio isolation. It captures from ONE audio device. Per-session audio is a Windows RDP feature that MultiSeat-Extended uses.

---

## 9. Input

### Keyboard/Mouse
- `src/input.cpp`: `input::passthrough()`
- Moonlight-format input packets
- SendInput() injection

### Gamepad
- XInput, DirectInput, GameInput
- ViGEm virtual controllers
- **Vibepollo Virtual Gamepad** (v1.19.0+): Own driver, no ViGEmBus dependency
  - Xbox Series/One, DualSense, DualShock 4, Switch Pro
  - Automatic profile selection per client

### Touch Input
- Supported via Moonlight protocol

### Input Injection
- All input goes through `input::passthrough()`
- WebRTC: data channel JSON → Moonlight packets → injection
- Classic: Moonlight packets → injection

---

## 10. Sessions

### Single-User Design
Vibepollo is designed for **ONE user session**. It:
- Runs in the current user's session
- Captures from the current session's display
- Captures from the current session's audio
- Injects into the current session

### Session Management
- No built-in session creation
- No built-in user management
- Assumes session already exists
- MultiSeat-Extended creates sessions via RDP loopback

---

## 11. Multi-Instance

### Not Built-In
Vibepollo does NOT support multi-instance natively. Evidence:
- Single process design
- Single config file
- Single port block
- Single display target
- No session/user management

### Required for Multi-Instance
1. **Per-seat config directory** — MultiSeat generates
2. **Per-seat port allocation** — MultiSeat allocates 30-port blocks
3. **Per-seat display** — Each seat has own SudoVDA virtual display
4. **Per-seat audio** — Windows RDP per-session Remote Audio
5. **Per-seat state** — Each seat has own sunshine_state.json

---

## 12. Configuration

### Config File: sunshine.conf
- Standard Sunshine config format
- Parsed by `config::parse()`
- Keys include: `sunshine_name`, `port`, `output_name`, `encoder`, etc.

### State File: sunshine_state.json
- UUID, pairing state, certificates
- Per-instance (must be isolated for multi-instance)

### App List: apps.json
- Game/application definitions
- Per-instance or shared

### Credentials: credentials.json
- Web UI login
- Can be shared across instances

---

## 13. Ports

### Default Port Block (Sunshine protocol)
```
Base port (configurable, default 47984)
  -5  GFE HTTPS (Moonlight pairing)
   0  GFE HTTP (config 'port' key)
   1  Web UI HTTPS
   9  Video RTP
  10  Control ENet
  11  Audio RTP
  12  Mic RTP
  26  RTSP
```

### Configurable
- Base port configurable via `port` in sunshine.conf
- All offsets are fixed relative to base

### Multi-Instance
- Each instance needs unique base port
- MultiSeat-Extended: 30-port blocks (48100, 48130, 48160, ...)

---

## 14. Authentication

### PIN-Based Pairing
- Moonlight clients pair via PIN
- Certificates stored in sunshine_state.json

### Session-Based Auth (Web UI)
- HttpOnly session cookies
- "Remember me" option
- Password manager support

### API Tokens
- Scoped access tokens
- Can be restricted to specific methods

---

## 15. API

### REST API (src/confighttp.cpp)
- HTTPS server on configured port
- `/api/**` endpoints
- Session management, config, apps, clients

### WebRTC Signaling
- POST `/api/webrtc/sessions` — create session
- POST `/api/webrtc/sessions/:id/offer` — SDP offer
- POST `/api/webrtc/sessions/:id/ice` — ICE candidates
- SSE `/api/webrtc/sessions/:id/ice/stream` — Server ICE candidates

### Authentication
- Every endpoint uses `authenticate()`
- Authorization header or session cookie

---

## 16. IPC

### Mailbox Abstraction
- `safe::mail_raw_t` for cross-thread coordination
- Typed events/queues (shutdown, IDR request, gamepad feedback)

### No External IPC
- No Named Pipes
- No shared memory
- No gRPC
- Purely internal thread communication

---

## 17. Security

### User-Level Process
- Runs as current user (not SYSTEM)
- No privilege escalation
- Limited to user's session

### Credentials
- Pairing certificates in sunshine_state.json
- Web UI credentials in credentials.json
- API tokens with scoped access

### Network
- HTTPS for Web UI + API
- TLS certificates managed internally

### Code Signing
- SignPath.io sponsorship
- Windows releases signed

---

## 18. Recovery

### Crash Detection
- Built-in process monitoring
- Configurable restart behavior

### Display Restoration
- Restores layout after crashes/shutdowns/reboots
- Does NOT restore during user logout

### Audio Recovery
- Fixed in v1.19.0-beta.1: "Fixed audio capture losing ownership"

---

## 19. External Dependencies

| Dependency | Purpose | License | Required |
|------------|---------|---------|----------|
| FFmpeg | Video encoding (AMF, software) | LGPL/GPL | Yes |
| NVENC SDK | NVIDIA hardware encoding | Proprietary | Optional (NVIDIA) |
| Moonlight-common-c | Protocol, input formats | MIT | Yes |
| libwebrtc | WebRTC streaming | BSD | Optional |
| nlohmann_json | JSON handling | MIT | Yes |
| Boost | Various utilities | BSL-1.0 | Yes |
| OpenSSL | TLS | Apache-2.0 | Yes |
| Virtual display driver | Display creation | GPLv3 | Optional |
| Virtual gamepad driver | Controller emulation | GPLv3 | Optional (v1.19.0+) |

---

## 20. Issues / PR / Releases

### Current Versions
- **v1.18.4-stable.3** (2026-08-19) — Latest stable
- **v1.19.0-beta.3** (2026-08-22) — Latest beta

### Recent Key Changes
- v1.19.0-beta.1: Vibepollo Virtual Gamepad driver (no ViGEmBus dependency)
- v1.19.0-alpha.2: Remote Monitor sessions (up to 4 Moonlight clients)
- v1.18.4: Virtual display activation fixes, HDR improvements
- v1.18.4-beta.3: Temporary virtual display for encoder probing

### Release Cadence
- Very active: multiple releases per week
- Beta → Stable promotion pattern

---

## 21. Strengths

1. **Feature-rich**: HDR, WGC, virtual display, gamepad, Playnite, RTSS, Lossless Scaling
2. **Active development**: Rapid iteration, frequent releases
3. **Code signing**: Professional release process
4. **WebRTC support**: Browser-based streaming
5. **Virtual gamepad**: Own driver (v1.19.0+), no ViGEmBus dependency
6. **Display management**: Robust virtual display with HDR support
7. **Configuration**: Comprehensive config options
8. **Documentation**: architecture.md (32KB), configuration.md (120KB)

---

## 22. Weaknesses

1. **Single-instance only**: No built-in multi-instance support
2. **Single-user only**: No session/user management
3. **Mutual exclusion**: RTSP and WebRTC cannot run simultaneously
4. **No Windows Service**: Runs as user process, not service
5. **AI-generated code**: "99% AI-generated" may concern some users
6. **Complex architecture**: Many components, steep learning curve

---

## 23. What MultiSeat-Extended Should Reuse

1. **Streaming protocols** — RTSP, WebRTC, Moonlight
2. **Encoding** — NVENC, AMF, FFmpeg
3. **Capture** — DDA, WGC, DXGI
4. **Virtual display** — Bundled driver or SudoVDA
5. **Audio capture** — WASAPI loopback
6. **Input injection** — Keyboard, mouse, gamepad, touch
7. **Configuration format** — sunshine.conf
8. **Pairing/authentication** — Certificates, PIN
9. **Web UI** — Full configuration interface

---

## 24. What MultiSeat-Extended Should NOT Duplicate

1. **Session creation** — MultiSeat uses RDP loopback
2. **User management** — MultiSeat manages Windows accounts
3. **Port allocation** — MultiSeat allocates 30-port blocks
4. **Display isolation** — MultiSeat: SudoVDA primary + RDP shrunk
5. **Audio isolation** — MultiSeat: PerSession RDP Remote Audio
6. **Health monitoring** — MultiSeat: SessionHealthCheck (5s)
7. **Crash recovery** — MultiSeat: auto-restart with limits
8. **API/Dashboard** — MultiSeat: ASP.NET Core + React
9. **Security** — MultiSeat: DPAPI, ACLs, API key

---

## 25. Unknowns

1. **Vibepollo log_path handling** — Does it actually ignore the config key?
2. **Vibepollo WebRTC mic support** — When will it stabilize?
3. **Vibepollo virtual display driver details** — Exact driver architecture
4. **Vibepollo multi-GPU support** — How does it select GPU?
5. **Vibepollo performance limits** — Max concurrent streams per GPU

---

## Quality Gate

- [x] Source code inspected (architecture.md, source structure)
- [x] Repository structure inspected (API tree, README)
- [x] Configuration inspected (docs/configuration.md)
- [x] Processes inspected (single-process daemon)
- [x] Multi-instance inspected (NOT built-in, requires external orchestrator)
- [x] Display inspected (bundled driver + SudoVDA rollback)
- [x] Audio inspected (WASAPI loopback, mic passthrough)
- [x] Input inspected (keyboard, mouse, gamepad, touch)
- [x] Session inspected (single-user design)
- [x] API inspected (REST + WebRTC signaling)
- [x] IPC inspected (internal mailbox only)
- [x] Security inspected (user-level, code signing)
- [x] Issues inspected (via releases)
- [x] PRs inspected (via releases)
- [x] Releases inspected (v1.18.4-stable.3, v1.19.0-beta.3)
- [x] Commit history inspected (via releases)
- [x] License checked (GPLv3)
- [x] Existing claims verified (83 VERIFIED, 4 INCORRECT, 2 UNVERIFIED)
- [x] No production code changed
