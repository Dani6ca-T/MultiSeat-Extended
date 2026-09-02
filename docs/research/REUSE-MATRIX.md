# MultiSeat-Extended: Матрица переиспользования (Reuse Matrix)

## Легенда

| Category | Описание |
|----------|----------|
| REUSE | Можно использовать напрямую |
| ADAPT | Можно адаптировать с изменениями |
| REFERENCE ONLY | Только как справочный материал |
| DO NOT USE | Не стоит использовать |
| UNKNOWN | Информации недостаточно |

---

## Session Management

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| RDP concurrent sessions patching | TermWrap | Dynamic termsrv patching via DLL proxy | MIT | ADAPT | SessionLauncher | MultiSeat uses RDPWrap; TermWrap's dynamic offset discovery is superior but needs integration |
| Automatic termsrv offset discovery | TermWrap (Rust fork) | Symbol-free runtime analysis with pelite + iced-x86 | MIT | REFERENCE ONLY | SessionLauncher | Novel approach; could replace static .ini files |
| RDP loopback session creation | MultiSeat-Extended | SessionLauncher.CreateSessionViaRdpLoopbackAsync | MIT | REUSE | SessionLauncher | Already implemented, proven approach |
| Session monitoring via WTS | MultiSeat-Extended | WTSEnumerateSessions + WTSQuerySessionInformation | MIT | REUSE | SessionHealthCheck | Standard Windows API approach |
| Session reconnect on sleep | MultiSeat-Extended | SessionHealthCheck auto-reconnect | MIT | REUSE | SessionHealthCheck | Handles sleep/wake correctly |
| Keepalive process (mstsc hidden) | MultiSeat-Extended | WindowHideHelper.WatchAndHideNew | MIT | REUSE | SessionLauncher | Proven approach for keeping sessions Active |
| RDP credential management | MultiSeat-Extended | RdpCredentialStore (CredWrite/CredRead) | MIT | REUSE | SessionLauncher | Secure credential handling |
| Live session monitor | neo_multiseat | PowerShell script with CSV export | MIT | REFERENCE ONLY | Diagnostics | Useful monitoring approach for dashboard |

---

## Streaming Provider

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| Sunshine/Apollo multi-instance | Helios | Per-instance config directory + Spawner service | GPLv3 | ADAPT | VibepolloManager | Helios architecture is good reference for provider lifecycle |
| Provider process lifecycle | Helios | Start/Stop/Restart via Named Pipes to SYSTEM service | GPLv3 | ADAPT | VibepolloManager | Named Pipe IPC pattern is reusable |
| Per-instance audio routing | Helios | VAC assignment per instance | GPLv3 | REFERENCE ONLY | AudioRouter | MultiSeat uses PerSession audio (different approach) |
| Headless mode | Vibepollo | Auto-create virtual display on cold boot | GPLv3 | REUSE | VibepolloConfigBuilder | Already implemented in config |
| Display layout restoration | Vibepollo | Auto-restore after crash/reboot | GPLv3 | REFERENCE ONLY | SessionHealthCheck | MultiSeat has own late detection approach |
| Encoder auto-detection | Vibepollo | Probe NVENC/AMF/software at startup | GPLv3 | REUSE | VibepolloConfigBuilder | Vibepollo handles this internally |
| RTSS frame limiting | Vibepollo/MultiSeat | Integrated RTSS profile management | GPLv3/MIT | REUSE | VibepolloConfigBuilder | Already implemented |
| Lossless Scaling integration | Vibepollo/MultiSeat | Automated upscaling per seat | GPLv3/MIT | REUSE | VibepolloConfigBuilder | Already implemented |
| Playnite integration | Vibepollo/MultiSeat | Database path in config | GPLv3/MIT | REUSE | VibepolloConfigBuilder | Already implemented |

---

## Virtual Display

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| SudoVDA virtual display | SudoVDA (separate project) | IddCx driver, UUID-based output_name | Unknown | REUSE | VirtualDisplayManager | Core dependency, already integrated |
| Virtual display driver (alternative) | virtual-display-rs (MolotovCherry) | Rust IddCx driver, up to 10 monitors | Open Source | REFERENCE ONLY | VirtualDisplayManager | Alternative to SudoVDA; more monitors |
| Display isolation | MultiSeat-Extended | SudoVDA primary + RDP shrunk to 640×480 | MIT | REUSE | SeatManager | Unique approach, reduces TermService CPU |
| Resolution matching | MultiSeat-Extended | Client-driven via RDP reconnect | MIT | REUSE | ClientResolutionFollower | Works correctly with RDP sessions |
| Late display detection | MultiSeat-Extended | Re-parse Vibepollo log for SudoVDA UUID | MIT | REUSE | SeatManager.TryLateDisplayDetectionAsync | Handles Vibepollo lazy display creation |

---

## Audio

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| Per-session audio isolation | MultiSeat-Extended | RDP "Remote Audio" endpoint per session | MIT | REUSE | SeatManager | No VAC needed, true isolation |
| Audio muting (mstsc side) | MultiSeat-Extended | AudioMuteHelper via Core Audio API | MIT | REUSE | SessionLauncher | Prevents host audio leak |
| Per-instance VAC routing | Helios | Virtual audio cable per instance | GPLv3 | REFERENCE ONLY | AudioRouter | MultiSeat doesn't need VAC anymore |
| Microphone passthrough | Apollo/Vibepollo | Steam Streaming Microphone in session | GPLv3 | REFERENCE ONLY | N/A | Not supported in PerSession mode |

---

## Input / Controller

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| XInput → ViGEm routing | MultiSeat-Extended | InputRouter (1ms polling thread) | MIT | REUSE | InputRouter | Working implementation |
| HidHide session jail | MultiSeat-Extended | Undocumented !<sessionId> suffix | MIT | REUSE | HidHideConfigurator | Proven approach (issue #19) |
| Controller auto-assignment | MultiSeat-Extended | First-come-first-served mapping | MIT | REUSE | InputRouter | Simple, effective |
| Gamepad isolation (UMDF driver) | Duo | Custom user-mode driver | Proprietary | REFERENCE ONLY | N/A | Closed-source, cannot reuse |
| KB/M session isolation | Duo | Session ID property filtering on HID devices | Proprietary | REFERENCE ONLY | InputHookManager | MultiSeat's approach is no-op; Duo's approach works |
| Native Moonlight controller | Vibepollo | Gamepad forwarding in sunshine.conf | GPLv3 | REUSE | VibepolloConfigBuilder | Default mode, no MultiSeat intervention needed |

---

## Game / Process

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| Process injection (CreateProcessAsUser) | MultiSeat-Extended | ProcessInjector with token verification | MIT | REUSE | ProcessInjector | Robust implementation with session verification |
| Launch-on-connect | MultiSeat-Extended | OnConnectAppLauncher (log tailing) | MIT | REUSE | OnConnectAppLauncher | Solves controller detection timing |
| Game mutex isolation | Duo | Application Compatibility Layer | Proprietary | REFERENCE ONLY | N/A | Closed-source; complex to reimplement |
| Steam multi-instance | Duo | Process patching for Steam | Proprietary | REFERENCE ONLY | N/A | Closed-source; depends on Steam internals |
| Shared game library | MultiSeat-Extended | icacls grant BUILTIN\Users | MIT | REUSE | SharedLibraryProvisioner | Simple, effective |
| Emulator netplay | MultiSeat-Extended | Per-seat RetroArch port from block | MIT | REUSE | RetroArchConfigSeeder | Unique feature |

---

## Service / Infrastructure

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| Windows Service (SYSTEM) | MultiSeat-Extended | AddWindowsService + BackgroundService | MIT | REUSE | MultiSeatWorker | Standard .NET approach |
| Provider lifecycle (Spawner) | Helios | Separate SYSTEM service + Named Pipes | GPLv3 | ADAPT | VibepolloManager | Could separate provider management from seat management |
| Orphan process cleanup | MultiSeat-Extended | WMI query + managed PID tracking | MIT | REUSE | MultiSeatWorker | Prevents killing standalone Vibepollo |
| Auto-restart with limit | MultiSeat-Extended | VibepolloManager.RestartAsync (max 3) | MIT | REUSE | VibepolloManager | Prevents infinite restart loops |
| Crash recovery | MultiSeat-Extended | SessionHealthCheck periodic probe | MIT | REUSE | SessionHealthCheck | Comprehensive checks |

---

## API / Dashboard

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| React dashboard | MultiSeat-Extended | Vite + React + TypeScript SPA | MIT | REUSE | Dashboard | Clean, modern UI |
| WebSocket real-time | MultiSeat-Extended | /ws/seats broadcast | MIT | REUSE | WebSocketHub | Real-time seat state |
| API key authentication | MultiSeat-Extended | X-MultiSeat-Key header | MIT | REUSE | ApiServer | Simple, effective |
| Named Pipe IPC | Helios | UI ↔ Spawner communication | GPLv3 | REFERENCE ONLY | N/A | Good pattern but MultiSeat uses embedded API |

---

## Security

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| DPAPI credential encryption | MultiSeat-Extended | ProtectedData (CurrentUser/SYSTEM scope) | MIT | REUSE | AccountManager | System-scope encryption |
| ACL hardening | MultiSeat-Extended | SecureFile.TryRestrictToSystemAndAdmins | MIT | REUSE | Storage | Prevents non-admin access |
| Seat accounts as standard users | MultiSeat-Extended | Users + Remote Desktop Users groups | MIT | REUSE | AccountManager | No admin needed for SudoVDA |
| Token verification | MultiSeat-Extended | EnsureTokenBelongsTo + VerifyLandedInSession | MIT | REUSE | ProcessInjector | Prevents wrong-session launches |

---

## Diagnostics

| Feature | Source Project | Implementation | License | Category | Target Layer | Reason |
|---------|---------------|----------------|---------|----------|--------------|--------|
| GPU monitoring | MultiSeat-Extended | GpuMonitor (nvml.dll) | MIT | REUSE | GpuMonitor | NVIDIA GPU stats |
| Metrics collection | MultiSeat-Extended | MetricsCollector (PerfCounter) | MIT | REUSE | MetricsCollector | CPU, memory, GPU metrics |
| HidHide diagnostics | MultiSeat-Extended | HidHideInspector (--hidhide CLI) | MIT | REUSE | Diagnostics | Read-only probe of HidHide state |
| Log filter inspection | MultiSeat-Extended | LogFilterInspector (--log-filters CLI) | MIT | REUSE | Diagnostics | Verifies Event Log visibility |
| Display enumeration | MultiSeat-Extended | DisplayEnumeratorHelper (--enum-displays) | MIT | REUSE | Diagnostics | Console-session helper |
