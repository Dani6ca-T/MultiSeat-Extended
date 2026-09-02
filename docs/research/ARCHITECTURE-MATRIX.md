# MultiSeat-Extended: Матрица архитектур (Architecture Matrix)

## Сравнение архитектурных подходов

### Core / Platform

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap | neo_multiseat |
|--------|-------------------|-----|-----------|--------|----------|---------------|
| **Язык** | C# (.NET 9) | C#/Proprietary | C++ | C# (.NET 8) | C++ / Rust | PowerShell |
| **UI Framework** | React + TypeScript | WPF / Web UI | Web UI | WPF (WinUI) | CLI | PowerShell TUI |
| **Service Model** | Windows Service (SYSTEM) | Windows Service | Windows Service | Windows Service (Spawner) | DLL proxy | None (script) |
| **Architecture** | Monolithic service + embedded API | Monolithic + proprietary drivers | Standalone process | WPF App + Service + Named Pipes | DLL injection | Script automation |
| **License** | MIT | Freemium/Proprietary | GPLv3 | GPLv3 | MIT | MIT |

### Seat Management

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap | neo_multiseat |
|--------|-------------------|-----|-----------|--------|----------|---------------|
| **Seat entity** | SeatInfo (in-memory + presets) | Windows user account | N/A (single user) | Instance profile | N/A | Windows user account |
| **Seat lifecycle** | 9-step provisioning pipeline | Automated setup | N/A | Create/Edit/Clone/Delete | N/A | Manual script |
| **Max seats** | Configurable (default 4, max 8) | Hardware-limited (paid: unlimited) | 1 | Unlimited | N/A | 3-10 (hardware) |
| **Seat persistence** | SeatPresetStore (JSON) | Registry/Config | N/A | Config files | N/A | RDP files |
| **Auto-provision** | YES (from presets) | YES (auto-start) | N/A | NO | N/A | NO |

### Session Management

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap | neo_multiseat |
|--------|-------------------|-----|-----------|--------|----------|---------------|
| **Session creation** | RDP loopback (127.0.0.2) | RDP via TermWrap | N/A | N/A | termsrv patching | RDP wrapper |
| **Session type** | Interactive (RDP) | Interactive (RDP) | N/A | N/A | Concurrent RDP | Interactive (RDP) |
| **Session monitoring** | WTS query + keepalive | Proprietary | N/A | N/A | N/A | Live Monitor script |
| **Session reconnect** | YES (auto on sleep/wake) | YES | N/A | N/A | N/A | NO |
| **Session persistence** | YES (session ID preserved) | YES | N/A | N/A | N/A | NO |
| **Keepalive process** | YES (mstsc hidden) | YES | N/A | N/A | NO | NO |

### Streaming

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Provider** | Vibepollo (configurable) | Sunshine (bundled) | Self (is the provider) | Managed (external) | N/A |
| **Multi-instance** | YES (per seat) | YES (per user) | N/A (single) | YES (core feature) | N/A |
| **Config isolation** | Per-seat sunshine.conf | Per-user Sunshine | N/A | Per-instance directory | N/A |
| **Port allocation** | 30-port blocks, bitmap | Per-instance | N/A | User-defined ports | N/A |
| **Process lifecycle** | Start/Stop/Restart/Health | Start/Stop | N/A | Start/Stop/All | N/A |
| **Crash recovery** | Auto-restart (max 3) | Auto-restart | YES (built-in) | Manual restart | N/A |
| **Encoder selection** | Configurable per seat | Per-user | Auto-detect | Per-instance | N/A |

### Display

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Virtual display** | SudoVDA (IddCx) | Custom WDDM driver | Own driver + SudoVDA | No (delegates) | No |
| **Display per seat** | YES (UUID tracked) | YES (per user) | YES | Configurable | No |
| **Display isolation** | SudoVDA primary + RDP shrunk | Proprietary | No | No | No |
| **Resolution control** | Client-driven (reconnect) | Client-driven (auto) | Client-driven | Configurable | No |
| **Refresh rate** | Up to seat.Fps | Up to 500Hz (paid) | Up to client max | Configurable | No |
| **HDR** | Probe only (no-op) | YES (paid) | YES | No | No |
| **Headless support** | YES | YES | YES | YES | No |
| **Display restoration** | YES (health check) | YES | YES | No | No |

### Audio

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Isolation model** | PerSession (RDP endpoint) | Per-session (Sunshine capture) | Virtual sink | Per-instance device | No |
| **Virtual audio device** | Not needed (PerSession) | Sunshine built-in | VB-CABLE etc. | VAC routing | No |
| **Host audio protection** | YES (mstsc muted) | YES | No | YES | No |
| **Microphone** | No | Unknown | YES (passthrough) | No | No |
| **Audio recovery** | YES (health check) | YES | YES | No | No |

### Input / Controller

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **KB/M isolation** | No-op (InputHookManager) | YES (session ID filtering) | No | No | No |
| **Gamepad isolation** | HidHide session jail (opt-in) | YES (custom UMDF driver) | No | No | No |
| **Virtual controller** | ViGEm (opt-in) | Custom (GameInput API) | Moonlight native | No | No |
| **XInput routing** | YES (InputRouter) | YES | No | No | No |
| **Controller assignment** | Auto + Manual API | Auto | N/A | No | No |

### Game / Process

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Game launching** | ProcessInjector (CreateProcessAsUser) | Native execution | App list (Moonlight) | No | No |
| **Launch-on-connect** | YES (OnConnectAppLauncher) | YES | YES (hooks) | No | No |
| **Process tracking** | Partial (Vibepollo PID only) | YES | No | Process tracking | No |
| **Game mutex isolation** | No | YES (compatibility layer) | No | No | No |
| **Steam multi-instance** | No | YES (patched) | No | No | No |
| **Shared library** | YES (icacls) | YES (Steam multi-box) | No | No | No |

### Services / Drivers

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Windows Service** | MultiSeatService | TermWrap service | Vibepollo service | Helios.Spawner | No |
| **Custom drivers** | SudoVDA (IddCx) | WDDM + UMDF | SudoVDA/own | No | No (DLL only) |
| **RDP wrapper** | RDPWrap/TermWrap | TermWrap (bundled) | No | No | Self (is the wrapper) |
| **ViGEmBus** | Optional | Yes (legacy) | No | No | No |
| **HidHide** | Optional | No | No | No | No |

### IPC / API

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **REST API** | ASP.NET Core Minimal API | Web UI (port 38299) | Sunshine REST API | No | No |
| **WebSocket** | YES (/ws/seats) | YES | No | No | No |
| **Named Pipes** | No | No | No | YES (Spawner ↔ UI) | No |
| **Dashboard** | React SPA (port 9550) | Web UI + Duo Manager | Web UI | WPF App | No |
| **API auth** | API key (X-MultiSeat-Key) | Session-based | PIN/cert pairing | No | No |

### Configuration

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Config format** | appsettings.json + sunshine.conf | Proprietary + sunshine.conf | sunshine.conf | Per-instance configs | INI |
| **Global settings** | MultiSeatOptions | Duo Manager | sunshine.conf | Helios.Core settings | N/A |
| **Per-seat settings** | SeatPreset + sunshine.conf | Per-user Sunshine | N/A | Per-instance | N/A |
| **Host-local overrides** | appsettings.local.json | No | No | No | N/A |
| **Presets** | SeatPresetStore (JSON) | Auto-start config | N/A | No | N/A |

### Security

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Credential storage** | DPAPI (SYSTEM scope) | Unknown | sunshine_state.json | Config files | No |
| **Seat privileges** | Standard user (default) | Standard user | N/A | N/A | N/A |
| **ACL hardening** | YES (SecureFile) | Unknown | No | No | No |
| **API authentication** | API key | Session auth | PIN/cert pairing | No | No |
| **HTTPS** | No | Unknown | YES | No | No |

### Recovery

| Aspect | MultiSeat-Extended | Duo | Vibepollo | Helios | TermWrap |
|--------|-------------------|-----|-----------|--------|----------|
| **Health check** | SessionHealthCheck (5s) | Built-in monitoring | Built-in | Status tracking | No |
| **Auto-restart** | YES (Vibepollo, max 3) | YES | YES | YES (batch) | No |
| **Session reconnect** | YES (sleep/wake) | YES | No | No | No |
| **Display restoration** | YES (late detection) | YES | YES | No | No |
| **Orphan cleanup** | YES (startup WMI) | YES | YES | No | No |
| **Teardown** | Best-effort reverse order | Automated | No | Stop commands | No |
