# Vibepollo Repository Structure

**Repository**: Nonary/Vibepollo
**License**: GPLv3 (LICENSE file, 35149 bytes)
**Current version**: v1.19.0-beta.3 (2026-08-22), v1.18.4-stable.3 (2026-08-19)
**Fork chain**: LizardByte/Sunshine → ClassicOldSong/Apollo → Nonary/Vibepollo
**Language**: C++ (primary), Vue.js/TypeScript (Web UI), Python (scripts)
**Build system**: CMake

---

## Repository Layout

```
Vibepollo/
├── src/                              # C++ source code (core daemon)
│   ├── main.cpp                      # Process entrypoint, subsystem init, thread start
│   ├── confighttp.cpp                # HTTPS Web UI + REST API + WebRTC signaling
│   ├── nvhttp.cpp                    # NVIDIA GameStream-compatible HTTP control
│   ├── stream.cpp                    # Classic RTSP session management + media/control
│   ├── rtsp.cpp                      # RTSP session setup
│   ├── video.cpp                     # Display capture, colorspace, encode (NVENC/FFmpeg)
│   ├── audio.cpp                     # Audio capture, Opus encode (or PCM bypass for WebRTC)
│   ├── input.cpp                     # Input injection (mouse/keyboard/gamepad/touch)
│   ├── webrtc_stream.cpp/.h          # WebRTC session tracking, signaling, media handoff
│   ├── process.cpp                   # App/process launching and lifecycle
│   ├── network.cpp                   # Network utilities, port mapping
│   ├── config.cpp                    # Configuration parsing
│   ├── crypto.cpp                    # Certificate management
│   ├── entry.cpp                     # CLI entry points
│   ├── platform/                     # OS-specific backends
│   │   ├── windows/                  # Windows capture, display, audio, input
│   │   │   ├── display_helper_integration.*  # Virtual display management
│   │   │   ├── virtual_display.*             # Virtual display driver management
│   │   │   └── ...
│   │   ├── linux/                    # Linux capture, audio, input
│   │   └── macos/                    # macOS capture
│   └── [other .cpp/.h files]
├── src_assets/                       # Web UI + assets
│   └── common/assets/web/            # Vue.js SPA
│       ├── views/                    # Vue views (WebRtcClientView.vue, etc.)
│       ├── utils/webrtc/             # Browser-side WebRTC (client.ts, input.ts)
│       ├── services/                 # API clients (webrtcApi.ts, etc.)
│       ├── router.ts                 # Vue router
│       └── types/                    # TypeScript types
├── cmake/                            # CMake build system
│   ├── compile_definitions/          # Platform-specific compile flags
│   ├── dependencies/                 # Dependency management (ffmpeg, webrtc, glad, etc.)
│   ├── packaging/                    # Installers (WiX for Windows)
│   └── targets/                      # Build targets
├── third-party/                      # Git submodules
│   ├── moonlight-common-c/           # Moonlight protocol (input packet formats)
│   ├── libdatachannel/               # WebRTC data channels
│   └── ...
├── docs/                             # Documentation
│   ├── architecture.md               # Detailed architecture (32KB)
│   ├── configuration.md              # Configuration reference (120KB)
│   ├── getting_started.md            # Setup guide
│   ├── api.md                        # API documentation
│   └── app_examples.md               # App configuration examples
├── .github/                          # CI/CD workflows
│   └── workflows/                    # Build, sign, release
├── docker/                           # Dockerfiles
├── .workflow/ultracode/              # AI development workflow docs
├── CMakeLists.txt                    # Root CMake
├── LICENSE                           # GPLv3
├── README.md                         # Project description
└── architecture.md                   # Detailed architecture document
```

---

## Key Components

### Core Daemon (C++)
- **Single process** running as the current user (NOT a Windows Service)
- Multiple long-lived threads: HTTP servers, discovery, capture, encode, media
- Cross-thread coordination via `safe::mail_raw_t` mailbox abstraction

### Virtual Display Driver
- Bundled with Vibepollo (own driver, not SudoVDA by default)
- SudoVDA kept as rollback option
- Can capture from any GPU including hybrid laptop GPUs

### Web UI (Vue.js SPA)
- Full configuration interface
- WebRTC client (experimental browser-based streaming)
- Session management, app management
- Display selection, audio settings, gamepad config

### Third-party Dependencies
- **moonlight-common-c**: Moonlight protocol, input packet formats
- **libwebrtc wrapper**: WebRTC support (optional)
- **FFmpeg**: Video encoding (AMF on AMD)
- **NVENC SDK**: NVIDIA hardware encoding
- **nlohmann_json**: JSON handling
- **Boost**: Various utilities
- **OpenSSL**: TLS for Web UI

---

## Build Targets

### Windows
- `sunshine.exe` (main daemon)
- WiX MSI installer
- Virtual display driver (signed)
- Virtual gamepad driver (v1.19.0+)

### Linux
- `sunshine` binary
- systemd service
- Package managers (deb, rpm, etc.)

### macOS
- `sunshine` binary
- Notarized app bundle

---

## File Statistics (approximate)

| Category | Count | Notes |
|----------|-------|-------|
| C++ source files | ~100+ | Core daemon |
| CMake files | ~30+ | Build system |
| Vue/TS files | ~50+ | Web UI |
| Python files | ~5 | CI/CD scripts |
| Documentation | ~10 | Markdown |
| Total repo size | ~50MB | With submodules |
