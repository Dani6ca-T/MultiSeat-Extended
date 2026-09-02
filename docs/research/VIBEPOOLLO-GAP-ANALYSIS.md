# MultiSeat-Extended: Анализ Vibepollo (Gap Analysis)

## Главный вопрос

> Что уже умеет Vibepollo и поэтому не должно дублироваться в MultiSeat-Extended?

## Vibepollo Features (что НЕ должно дублироваться)

### Streaming Core

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| H.264 encoding | ✅ | ❌ |
| HEVC encoding | ✅ | ❌ |
| AV1 encoding | ✅ | ❌ |
| NVENC support | ✅ | ❌ |
| AMF support | ✅ | ❌ |
| Software encoding fallback | ✅ | ❌ |
| Encoder probing | ✅ | ❌ |
| RTP video streaming | ✅ | ❌ |
| ENet control channel | ✅ | ❌ |
| RTSP session setup | ✅ | ❌ |

### Display Management

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| Virtual display creation (SudoVDA) | ✅ | ❌ |
| Display enumeration | ✅ | ❌ |
| Resolution matching (dd_resolution_option=auto) | ✅ | ❌ |
| Refresh rate matching (dd_refresh_rate_option=auto) | ✅ | ❌ |
| Display activation (dd_configuration_option=ensure_active) | ✅ | ❌ |
| Headless mode | ✅ | ❌ |
| HDR metadata handling | ✅ | ❌ |
| Display layout restoration | ✅ | ❌ |
| Frame generation capture fixes | ✅ | ❌ |

### Audio

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| Audio capture (WASAPI loopback) | ✅ | ❌ |
| Audio streaming (RTP) | ✅ | ❌ |
| Microphone passthrough | ✅ | ❌ |
| Virtual audio sink binding | ✅ | ❌ |
| Audio recovery | ✅ | ❌ |

### Input

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| Gamepad forwarding (Moonlight → host) | ✅ | ❌ |
| Keyboard/mouse forwarding | ✅ | ❌ |
| Virtual controller creation | ✅ | ❌ |
| Controller detection | ✅ | ❌ |
| Touch input support | ✅ | ❌ |

### Authentication / Pairing

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| PIN-based pairing | ✅ | ❌ |
| Certificate management | ✅ | ❌ |
| Client state tracking | ✅ | ❌ |
| Per-client identity | ✅ | ❌ |
| Web UI authentication | ✅ | ❌ |
| API token management | ✅ | ❌ |

### Configuration

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| sunshine.conf format | ✅ | ❌ |
| Encoder selection | ✅ | ❌ |
| Port configuration | ✅ | ❌ |
| Logging | ✅ | ❌ |
| State persistence | ✅ | ❌ |
| App list management | ✅ | ❌ |

### Advanced Integration

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| Playnite integration | ✅ | ❌ |
| RTSS frame limiting | ✅ | ❌ |
| Lossless Scaling | ✅ | ❌ |
| NVIDIA Smooth Motion | ✅ | ❌ |
| RTX HDR / TrueHDR | ✅ | ❌ |
| Vulkan HDR layers | ✅ | ❌ |

### Recovery

| Feature | Vibepollo Handles | MultiSeat Should NOT Implement |
|---------|-------------------|-------------------------------|
| Crash detection | ✅ | ❌ |
| Auto-restart | ✅ | ❌ |
| State cleanup | ✅ | ❌ |
| Display restoration | ✅ | ❌ |

---

## Orchestration Features (что ДОЛЖЕН делать MultiSeat-Extended)

### Vibepollo отсутствует → MultiSeat должен обеспечить

| Feature | Vibepollo Has | MultiSeat Must Implement |
|---------|---------------|------------------------|
| Multi-seat lifecycle | ❌ | ✅ SeatManager (9-step provisioning) |
| Windows session creation | ❌ | ✅ SessionLauncher (RDP loopback) |
| Windows user management | ❌ | ✅ AccountManager (NetApi32) |
| Port allocation per seat | ❌ | ✅ PortAllocator (bitmap) |
| Display isolation | ❌ | ✅ SudoVDA primary + RDP shrunk |
| Per-session audio routing | ❌ | ✅ RDP Remote Audio endpoint |
| Firewall management | ❌ | ✅ FirewallManager (per-seat ports) |
| Health monitoring | ❌ | ✅ SessionHealthCheck (5s loop) |
| Crash recovery | Partial | ✅ Auto-restart with limit |
| Sleep/wake handling | ❌ | ✅ Session reconnect |
| Late display detection | ❌ | ✅ Log re-parsing |
| Orphan process cleanup | ❌ | ✅ WMI-based identification |
| API for dashboard | ❌ | ✅ ASP.NET Core Minimal API |
| WebSocket real-time | ❌ | ✅ /ws/seats broadcast |
| Dashboard UI | ❌ | ✅ React SPA |
| Credential management | ❌ | ✅ DPAPI encryption |
| Gamepad isolation | ❌ | ✅ HidHide session jail |
| Controller routing | ❌ | ✅ InputRouter (XInput → ViGEm) |
| Launch-on-connect | ❌ | ✅ OnConnectAppLauncher |
| Shared game library | ❌ | ✅ icacls-based provisioner |
| Emulator netplay | ❌ | ✅ RetroArch port assignment |
| Diagnostics | ❌ | ✅ HidHideInspector, LogFilterInspector |

---

## Shared Responsibilities (оба уровня)

| Feature | Vibepollo Role | MultiSeat Role |
|---------|---------------|----------------|
| Streaming server | Runs the process | Starts/stops/restarts the process |
| Display management | Creates/destroys virtual display | Tracks UUID, applies isolation |
| Audio capture | Loopback-captures session audio | Mutes mstsc on console side |
| Configuration | Reads sunshine.conf | Generates sunshine.conf |
| Error handling | Internal crash recovery | External process monitoring |
| Logging | Writes vibepollo.log | Parses log for display detection |

---

## Ключевые выводы

### 1. Vibepollo — это streaming engine, не orchestrator

Vibepollo handles everything inside a single session: encoding, streaming, display creation, audio capture, input forwarding. It knows nothing about seats, sessions, users, or multi-instance management.

### 2. MultiSeat — это orchestrator, не streaming engine

MultiSeat handles everything outside the session: user creation, session lifecycle, port allocation, display isolation, health monitoring, API, dashboard. It knows nothing about encoding, streaming protocols, or video capture.

### 3. The boundary is clean

- **Vibepollo**: "I have a session, a display, and a config. I stream to Moonlight clients."
- **MultiSeat**: "I create sessions, assign displays, allocate ports, and manage Vibepollo processes."

### 4. MultiSeat should NEVER reimplement

- Encoding/streaming protocols
- Virtual display creation (SudoVDA)
- Audio capture
- Input forwarding
- Pairing/authentication
- Encoder probing
- Display resolution matching

### 5. MultiSeat MUST implement

- Session lifecycle (create, monitor, reconnect, destroy)
- User management (create, delete, credentials)
- Port allocation (per-seat blocks)
- Display isolation (SudoVDA primary + RDP shrunk)
- Health checking (session, process, display)
- API + Dashboard
- Security (credentials, ACLs, authentication)
- Gamepad isolation (HidHide)
- Launch-on-connect apps

### 6. The orchestration layer is what makes MultiSeat unique

Vibepollo alone is just a streaming server. MultiSeat turns it into a multiseat platform. The value is in the orchestration, not the streaming.
