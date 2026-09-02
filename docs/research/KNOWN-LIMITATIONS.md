# MultiSeat-Extended: Известные ограничения

## Software Limitations

### A-001: Vibepollo tightly coupled to Seat lifecycle

- **Type**: Software
- **Root cause**: VibepolloConfigBuilder generates sunshine.conf (Vibepollo-specific format)
- **Can software remove it?**: Partially — abstract StreamingProvider interface
- **Can architecture work around it?**: Yes — separate config generation from process management

### A-002: Display logic coupled to streaming provider

- **Type**: Software
- **Root cause**: Display discovery via Vibepollo log parsing
- **Can software remove it?**: Partially — abstract display discovery interface
- **Can architecture work around it?**: Yes — separate display manager from streaming

### A-003: InputHookManager is a no-op

- **Type**: Software
- **Root cause**: WH_KEYBOARD_LL/WH_MOUSE_LL hooks in Session 0 → GetForegroundWindow() = NULL
- **Can software remove it?**: Yes — re-architect to run hooks inside seat session
- **Can architecture work around it?**: Not needed — no cross-session K/M bleed with RDP loopback

### A-004: MaxSeats architectural ceiling

- **Type**: Software
- **Root cause**: Constants.MaxSeats = 8 (PortAllocator bitmap size)
- **Can software remove it?**: Yes — increase constant or use dynamic allocation
- **Can architecture work around it?**: Yes — operator limit in appsettings.json (default 4)

### A-005: Single GPU assumption

- **Type**: Software
- **Root cause**: No multi-GPU enumeration or assignment logic
- **Can software remove it?**: Yes — add GPU enumeration and seat-GPU assignment
- **Can architecture work around it?**: Partially — all seats share one GPU today

### A-006: No game process tracking

- **Type**: Software
- **Root cause**: SeatManager doesn't track game PID separately
- **Can software remove it?**: Yes — track game process, handle spawn children
- **Can architecture work around it?**: Vibepollo crash detection covers most cases

### A-007: No HTTPS for API

- **Type**: Software
- **Root cause**: No TLS certificate management, no Kestrel HTTPS config
- **Can software remove it?**: Yes — add certificate management
- **Can architecture work around it?**: Reverse proxy, loopback only

### A-008: No microphone path

- **Type**: Software
- **Root cause**: PerSession audio isolates seat from host audio stack
- **Can software remove it?**: Wait for Vibepollo WebRTC mic support
- **Can architecture work around it?**: No — fundamental to PerSession design

---

## Windows Limitations

### W-001: RDP Wrapper dependency

- **Type**: Windows
- **Root cause**: termsrv.dll must be patched for concurrent sessions
- **Can software remove it?**: No — Windows limitation
- **Can architecture work around it?**: TermWrap (fork of RDPWrap) maintained for newer builds

### W-002: RDPWrap breaks after Windows updates

- **Type**: Windows
- **Root cause**: termsrv.dll changes in updates break patches
- **Can software remove it?**: No — must re-run prereq script
- **Can architecture work around it?**: Auto-detect and warn, prereq script refreshes

### W-003: Session resolution fixed at connect time

- **Type**: Windows
- **Root cause**: mstsc sets geometry at connect, cannot change from inside session
- **Can software remove it?**: No — Windows RDP limitation
- **Can architecture work around it?**: Disconnect + reconnect with new geometry

### W-004: DXGI ACCESS_DENIED for disconnected sessions

- **Type**: Windows
- **Root cause**: QueryDisplayConfig fails when session is Disconnected
- **Can software remove it?**: No — Windows security
- **Can architecture work around it?**: Keep session Active (mstsc connected)

### W-005: Single machine-wide default audio device

- **Type**: Windows
- **Root cause**: Windows has one default output shared by console + all sessions
- **Can software remove it?**: No — Windows architecture
- **Can architecture work around it?**: PerSession audio (separate endpoints per session)

### W-006: CreateProcessAsUser cannot create new sessions from Session 0

- **Type**: Windows
- **Root cause**: CreateProcessAsUser always launches in caller's session
- **Can software remove it?**: No — Windows API limitation
- **Can architecture work around it?**: RDP loopback triggers session creation

### W-007: WTSQueryUserToken returns filtered token for admins

- **Type**: Windows
- **Root cause**: UAC filtering for admin accounts
- **Can software remove it?**: No — Windows security
- **Can architecture work around it?**: GetTokenLinkedToken for elevated token

### W-008: Builtin group names are LOCALIZED

- **Type**: Windows
- **Root cause**: "Users" is "Usuarios" on Spanish, "Benutzer" on German
- **Can software remove it?**: No — Windows localization
- **Can architecture work around it?**: ResolveLocalGroupName from WellKnown SID

---

## Driver Limitations

### D-001: NVIDIA consumer GPU NVENC session limit

- **Type**: Driver
- **Root cause**: 3-5 concurrent NVENC sessions on consumer GPUs
- **Can software remove it?**: No — hardware/driver limitation
- **Can architecture work around it?**: Limit seats to NVENC capacity, mix encoders

### D-002: SudoVDA HDR support incomplete

- **Type**: Driver
- **Root cause**: VidPN SOURCE mode stays SDR in RDP sessions
- **Can software remove it?**: Partially — force FP16 primary (Nonary approach)
- **Can architecture work around it?**: EnableHdr flag exists but is no-op

### D-003: SudoVDA display created only on client connect

- **Type**: Driver
- **Root cause**: Vibepollo gates creation on headless_mode + client connect
- **Can software remove it?**: No — Vibepollo behavior
- **Can architecture work around it?**: Late display detection in health check

### D-004: HidHide session jail is undocumented

- **Type**: Driver
- **Root cause**: Feature not in README, CLI help, or release notes
- **Can software remove it?**: No — depends on HidHide implementation
- **Can architecture work around it?**: Careful testing, version pinning

---

## Provider Limitations

### P-001: Vibepollo headless_mode required for RDP seats

- **Type**: Provider
- **Root cause**: Without headless_mode, Vibepollo captures RDP surface instead
- **Can software remove it?**: No — Vibepollo behavior
- **Can architecture work around it?**: Always set headless_mode = enabled

### P-002: Vibepollo ignores log_path config key

- **Type**: Provider
- **Root cause**: Writes timestamped files to logs/ subdirectory
- **Can software remove it?**: No — Vibepollo behavior
- **Can architecture work around it?**: ResolveLogPath() inspects actual files

### P-003: Vibepollo creates display at client connect, not startup

- **Type**: Provider
- **Root cause**: Display creation gated on headless_mode + client connect
- **Can software remove it?**: No — Vibepollo behavior
- **Can architecture work around it?**: Late display detection + retry

### P-004: Vibepollo port offsets hardcoded

- **Type**: Provider
- **Root cause**: map_port(N) = sunshine.port + N (from network.cpp)
- **Can software remove it?**: No — Vibepollo protocol
- **Can architecture work around it?**: Constants define offsets, abstract for other providers

### P-005: No Vibepollo API for seat management

- **Type**: Provider
- **Root cause**: Vibepollo has no programmatic API for display/audio control
- **Can software remove it?**: No — Vibepollo design
- **Can architecture work around it?**: Config file + process management + log parsing

---

## Game Limitations

### G-001: Game mutex / DRM

- **Type**: Game
- **Root cause**: Some games use single-instance mutex, online DRM
- **Can software remove it?**: No — game-specific
- **Can architecture work around it?**: Each seat is separate Windows account

### G-002: Anti-cheat may not work in virtual sessions

- **Type**: Game
- **Root cause**: Some anti-cheat requires physical hardware access
- **Can software remove it?**: No — game-specific
- **Can architecture work around it?**: Test per-game, document compatibility

### G-003: Controller scan at startup

- **Type**: Game
- **Root cause**: Games scanning controllers before Vibepollo creates virtual pad
- **Can software remove it?**: No — game behavior
- **Can architecture work around it?**: LaunchOnConnectApps (launch after client connect)

### G-004: Game expects specific display device

- **Type**: Game
- **Root cause**: Some games hardcode display device names
- **Can software remove it?**: No — game-specific
- **Can architecture work around it?**: SudoVDA EDID mimics real display

---

## Hardware Limitations

### H-001: CPU limit

- **Type**: Hardware
- **Root cause**: Each seat runs game + Vibepollo encoder
- **Can software remove it?**: No — hardware limit
- **Can architecture work around it?**: NVENC offloads encoding, monitor CPU usage

### H-002: RAM limit

- **Type**: Hardware
- **Root cause**: Each seat has own Windows session + game
- **Can software remove it?**: No — hardware limit
- **Can architecture work around it?**: Monitor via MetricsCollector, limit seat count

### H-003: GPU limit

- **Type**: Hardware
- **Root cause**: Each seat renders game + Vibepollo encodes
- **Can software remove it?**: No — hardware limit
- **Can architecture work around it?**: NVENC for encoding, limit concurrent seats

### H-004: Network bandwidth

- **Type**: Hardware
- **Root cause**: Each stream uses network bandwidth
- **Can software remove it?**: No — hardware limit
- **Can architecture work around it?**: Configure quality settings per seat

### H-005: Display output limit

- **Type**: Hardware
- **Root cause**: GPU has limited display outputs
- **Can software remove it?**: Partially — SudoVDA adds virtual displays
- **Can architecture work around it?**: SudoVDA bypasses physical output limit
