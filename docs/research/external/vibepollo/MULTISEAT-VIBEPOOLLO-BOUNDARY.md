# MultiSeat-Extended / Vibepollo Responsibility Boundary

**Based on**: Source-level analysis of Nonary/Vibepollo (architecture.md, source code, releases)
**Date**: 2026-08-30

---

## Executive Summary

Vibepollo is a **single-user streaming server**. It captures, encodes, and streams from ONE user's session. It does NOT manage multiple seats, sessions, or users. MultiSeat-Extended is the **orchestrator** that creates sessions, launches Vibepollo instances, and manages their lifecycle.

---

## VIBEPOOLLO OWNS

### Streaming Core
| Component | Evidence |
|-----------|----------|
| Video capture (DDA, WGC, DXGI) | `src/video.cpp`, `src/platform/windows/` |
| Video encoding (NVENC, AMF, FFmpeg) | `src/video.cpp`: `video::capture()` |
| Audio capture (WASAPI loopback) | `src/audio.cpp`: `audio::capture()` |
| Audio encoding (Opus) | `src/audio.cpp` |
| RTSP session management | `src/stream.cpp`, `src/rtsp.cpp` |
| WebRTC streaming | `src/webrtc_stream.cpp` |
| Moonlight protocol | `src/nvhttp.cpp` |
| Input injection (keyboard/mouse/gamepad/touch) | `src/input.cpp`: `input::passthrough()` |

### Configuration & UI
| Component | Evidence |
|-----------|----------|
| sunshine.conf format | `src/config.cpp`: `config::parse()` |
| Web UI (Vue.js SPA) | `src_assets/common/assets/web/` |
| REST API | `src/confighttp.cpp`: `/api/**` endpoints |
| App management | `src/process.cpp`: `proc::proc.execute()` |
| Certificate/pairing management | `src/crypto.cpp` |

### Virtual Display
| Component | Evidence |
|-----------|----------|
| Bundled virtual display driver | README: "Vibepollo uses its bundled virtual display driver" |
| Display management | `src/platform/windows/virtual_display.*` |
| Display helper integration | `src/platform/windows/display_helper_integration.*` |
| SudoVDA as rollback option | README: "keeps SudoVDA installed as a rollback option" |

### Audio
| Component | Evidence |
|-----------|----------|
| WASAPI loopback capture | `src/audio.cpp`: `audio::capture()` |
| Microphone passthrough | WebRTC `bypass_opus=true` path, mic RTP port |
| Audio sink selection | `sunshine.conf`: `audio_sink`, `virtual_sink` |
| Host audio muting | Config option: `mute_host_audio` |

### Input
| Component | Evidence |
|-----------|----------|
| Keyboard/mouse injection | `src/input.cpp`: `input::passthrough()` |
| Gamepad forwarding | `src/input.cpp`: XInput, DirectInput, GameInput |
| Touch input | `src/input.cpp` |
| Virtual gamepad (v1.19.0+) | "Vibepollo Virtual Gamepad driver" |
| ViGEm integration | Config option: gamepad settings |

### Recovery
| Component | Evidence |
|-----------|----------|
| Crash detection | Built-in process monitoring |
| Auto-restart | Configurable in config |
| Display restoration | Release notes: "display restoration after hard crashes, shutdowns, or reboots" |
| Layout restoration | "restores your layout after hard crashes" |

### Game Launching
| Component | Evidence |
|-----------|----------|
| App list management | `src/process.cpp` |
| Game launching | `proc::proc.execute()` |
| Playnite integration | README: "Deep integration with Playnite" |
| Pre/post launch hooks | Config options |

---

## EXTERNAL COMPONENT OWNS (MultiSeat-Extended)

### Session Management
| Component | Evidence |
|-----------|----------|
| Windows session creation | MultiSeat uses RDP loopback (127.0.0.2) |
| Session monitoring | MultiSeat: WTS query + keepalive |
| Session reconnect | MultiSeat: auto on sleep/wake |
| Session cleanup | MultiSeat: TeardownSeatInternalAsync |

### User Management
| Component | Evidence |
|-----------|----------|
| Windows account creation | MultiSeat: AccountManager (NetApi32) |
| Account privilege management | MultiSeat: Users + Remote Desktop Users groups |
| Credential storage | MultiSeat: DPAPI (SYSTEM scope) |

### Port Allocation
| Component | Evidence |
|-----------|----------|
| Port block assignment | MultiSeat: PortAllocator (bitmap, 30 ports/seat) |
| Port conflict prevention | MultiSeat: 48100 base, per-seat 30-port blocks |
| Firewall management | MultiSeat: FirewallManager (per-seat) |

### Display Isolation
| Component | Evidence |
|-----------|----------|
| SudoVDA as session primary | MultiSeat: `--setup-display-isolation` helper |
| RDP display shrunk to 640x480 | MultiSeat: reduces TermService CPU ~70% to <5% |
| Display isolation lifecycle | MultiSeat: applied after SudoVDA UUID discovery |

### Multi-Seat Orchestration
| Component | Evidence |
|-----------|----------|
| Seat lifecycle (9-step pipeline) | MultiSeat: SeatManager.ProvisionSeatAsync |
| Per-seat config generation | MultiSeat: VibepolloConfigBuilder |
| Per-seat Vibepollo launch | MultiSeat: ProcessInjector.LaunchVibepolloInSessionAsync |
| Health monitoring | MultiSeat: SessionHealthCheck (5s interval) |
| Crash recovery with limits | MultiSeat: VibepolloManager.RestartAsync (max 3) |

### Audio Isolation
| Component | Evidence |
|-----------|----------|
| Per-session Remote Audio endpoints | MultiSeat: Windows RDP per-session audio |
| Host audio protection | MultiSeat: AudioMuteHelper (mstsc muted) |

### Gamepad Isolation
| Component | Evidence |
|-----------|----------|
| HidHide session jail | MultiSeat: HidHideConfigurator (optional) |

### API & Dashboard
| Component | Evidence |
|-----------|----------|
| Management API | MultiSeat: ASP.NET Core Minimal API (port 9550) |
| WebSocket real-time | MultiSeat: /ws/seats |
| React dashboard | MultiSeat: MultiSeat.Dashboard |

---

## SHARED / BOUNDARY

### Configuration Generation
| Component | Who | Evidence |
|-----------|-----|----------|
| sunshine.conf generation | MultiSeat generates, Vibepollo reads | MultiSeat: VibepolloConfigBuilder; Vibepollo: config::parse() |
| Per-seat config isolation | MultiSeat creates per-seat directories | MultiSeat: VibepolloConfigBuilder.BuildConfig |

### Display Creation
| Component | Who | Evidence |
|-----------|-----|----------|
| Virtual display creation | Vibepollo creates via bundled driver | Vibepollo: virtual_display.* |
| Display UUID tracking | MultiSeat tracks via log parsing | MultiSeat: VibepolloManager.ParseSudoVdaDisplayId |
| Display target (output_name) | MultiSeat writes to config | MultiSeat: VibepolloConfigBuilder.UpdateDisplayOutput |

### Process Lifecycle
| Component | Who | Evidence |
|-----------|-----|----------|
| Process launch | MultiSeat launches Vibepollo | MultiSeat: ProcessInjector.LaunchVibepolloInSessionAsync |
| Process monitoring | Both (MultiSeat: PID tracking; Vibepollo: internal) | |
| Process kill | MultiSeat kills Vibepollo | MultiSeat: VibepolloManager.Stop (entireProcessTree) |

---

## WHAT MULTIEXTENDED MUST NEVER DUPLICATE

1. **Encoding/streaming protocols** — Vibepollo handles H.264, HEVC, AV1, NVENC, AMF
2. **Virtual display creation** — Vibepollo's bundled driver or SudoVDA
3. **Audio capture** — Vibepollo's WASAPI loopback
4. **Input forwarding** — Vibepollo's injection pipeline
5. **Pairing/authentication** — Vibepollo's certificate management
6. **Encoder probing** — Vibepollo's `video::probe_encoders()`
7. **WebRTC streaming** — Vibepollo's WebRTC stack

## WHAT MULTIEXTENDED MUST IMPLEMENT

1. **Session lifecycle** — create, monitor, reconnect, destroy Windows sessions
2. **User management** — create, delete Windows accounts
3. **Port allocation** — per-seat 30-port blocks (48100+)
4. **Display isolation** — SudoVDA primary + RDP shrunk to 640x480
5. **Audio isolation** — per-session Remote Audio endpoints
6. **Health monitoring** — session, process, display checks
7. **Crash recovery** — auto-restart with limits (max 3)
8. **API + Dashboard** — management interface
9. **Security** — credentials, ACLs, authentication
10. **Gamepad isolation** — HidHide session jail

---

## KEY ARCHITECTURAL INSIGHT

Vibepollo is a **single-user streaming daemon**. It has NO concept of:
- Multiple seats
- Multiple sessions
- Multiple users
- Session creation
- User management
- Port allocation
- Display isolation between seats

All of this is MultiSeat-Extended's responsibility.

**Vibepollo assumes it runs in a single user session with a single display and single audio device.** MultiSeat-Extended creates that session, provides that display, and provides that audio device.
