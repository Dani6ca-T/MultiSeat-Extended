# MultiSeat-Extended: Ограничения Windows платформы

## Windows Limitations

### W-001: Concurrent RDP Sessions

- **Limitation**: Windows Home/Pro limits concurrent RDP sessions to 1
- **Type**: Windows-imposed
- **Root cause**: Microsoft licensing — concurrent sessions only in Server/Enterprise with RDS CAL
- **Software workaround**: RDPWrap/TermWrap patches termsrv.dll
- **Can software remove it?**: Yes — via DLL proxy patching
- **Security implications**: Modifies termsrv behavior, may break with updates
- **Status**: Worked around via RDPWrap/TermWrap

### W-002: CreateProcessAsUser Cannot Create Sessions from Session 0

- **Limitation**: CreateProcessAsUser always launches in caller's session
- **Type**: Windows API limitation
- **Root cause**: Windows security model — Session 0 is non-interactive
- **Software workaround**: RDP loopback (mstsc → 127.0.0.2) triggers session creation
- **Can software remove it?**: No — must use RDP protocol
- **Security implications**: Requires SYSTEM privileges
- **Status**: Worked around via RDP loopback

### W-003: Session Resolution Fixed at Connect Time

- **Limitation**: mstsc sets geometry at connect; cannot change from inside session
- **Type**: Windows RDP limitation
- **Root cause**: RDP protocol design — resolution negotiated at connection
- **Software workaround**: Disconnect + reconnect with new geometry
- **Can software remove it?**: No — RDP protocol constraint
- **Security implications**: None
- **Status**: Worked around via reconnect (brief interruption)

### W-004: DXGI ACCESS_DENIED for Disconnected Sessions

- **Limitation**: QueryDisplayConfig fails when session is Disconnected
- **Type**: Windows security
- **Root cause**: Display APIs require Active session
- **Software workaround**: Keep session Active (mstsc connected)
- **Can software remove it?**: No — Windows security model
- **Security implications**: None
- **Status**: Worked around via keepalive process

### W-005: Single Machine-Wide Default Audio Device

- **Limitation**: Windows has one default output shared by console + all sessions
- **Type**: Windows architecture
- **Root cause**: Audio subsystem design — single default endpoint
- **Software workaround**: PerSession audio (separate endpoints per session)
- **Can software remove it?**: No — Windows architecture
- **Security implications**: None
- **Status**: Worked around via PerSession audio

### W-006: WTSQueryUserToken Returns Filtered Token for Admins

- **Limitation**: UAC filtering for admin accounts
- **Type**: Windows security
- **Root cause**: User Account Control — admin tokens are filtered
- **Software workaround**: GetTokenLinkedToken for elevated token
- **Can software remove it?**: No — Windows security
- **Security implications**: Must handle both admin and standard user tokens
- **Status**: Worked around via linked token retrieval

### W-007: Builtin Group Names Are Localized

- **Limitation**: "Users" is "Usuarios" on Spanish, "Benutzer" on German
- **Type**: Windows localization
- **Root cause**: Localized group names in different Windows languages
- **Software workaround**: ResolveLocalGroupName from WellKnown SID
- **Can software remove it?**: No — Windows design
- **Security implications**: None
- **Status**: Worked around via SID-based resolution

### W-008: NLA (Network Level Authentication)

- **Limitation**: NLA must be disabled for loopback RDP logon
- **Type**: Windows RDP setting
- **Root cause**: NLA requires interactive prompt; loopback can't answer
- **Software workaround**: Disable NLA on RDP-Tcp listener
- **Can software remove it?**: No — RDP protocol constraint
- **Security implications**: Weakens RDP security for ALL connections
- **Status**: Worked around (security trade-off documented)

### W-009: RDP Certificate Warnings

- **Limitation**: mstsc shows certificate warning for self-signed certs
- **Type**: Windows RDP security
- **Root cause**: No trusted certificate for loopback connection
- **Software workaround**: Suppress warnings via machine policy
- **Can software remove it?**: No — Windows security
- **Security implications**: Suppresses security warnings for all users
- **Status**: Worked around (documented in security-posture.md)

---

## RDP Limitations

### R-001: RDP Display Compression

- **Limitation**: RDP uses its own display compression, not GPU capture
- **Type**: RDP protocol
- **Root cause**: RDP is designed for remote desktop, not game streaming
- **Software workaround**: SudoVDA display isolation (shrink RDP to 640×480)
- **Can software remove it?**: Partially — via display isolation
- **Security implications**: None
- **Status**: Worked around via display isolation

### R-002: RDP Input Latency

- **Limitation**: RDP adds latency to keyboard/mouse input
- **Type**: RDP protocol
- **Root cause**: RDP input is forwarded over network protocol
- **Software workaround**: Vibepollo handles input directly (not via RDP)
- **Can software remove it?**: Yes — bypass RDP input for streaming
- **Security implications**: None
- **Status**: Worked around via Vibepollo direct input

### R-003: RDP Wrapper Breaks After Windows Updates

- **Limitation**: termsrv.dll changes break RDPWrap patches
- **Type**: Windows update
- **Root cause**: Microsoft changes internal structures
- **Software workaround**: Re-run prereq script to refresh .ini file
- **Can software remove it?**: Partially — TermWrap Rust uses dynamic discovery
- **Security implications**: None
- **Status**: Partially worked around (manual .ini update)

### R-004: mstsc Window Re-shows

- **Limitation**: mstsc window reappears on connect/reconnect/resolution change
- **Type**: Windows RDP behavior
- **Root cause**: mstsc manages its own window state
- **Software workaround**: WindowHideHelper.WatchAndHideNew monitors new processes
- **Can software remove it?**: No — mstsc behavior
- **Security implications**: None
- **Status**: Worked around via window hiding

---

## GPU Limitations

### G-001: NVIDIA Consumer GPU NVENC Session Limit

- **Limitation**: 3-5 concurrent NVENC sessions on consumer GPUs
- **Type**: Hardware/driver limitation
- **Root cause**: NVIDIA artificially limits consumer GPU encoding sessions
- **Software workaround**: Limit seats to NVENC capacity; mix encoders
- **Can software remove it?**: No — hardware/driver limit
- **Security implications**: None
- **Status**: Documented limitation

### G-002: Single GPU Assumption

- **Limitation**: Multi-GPU not tested or supported
- **Type**: Software limitation
- **Root cause**: No multi-GPU enumeration or assignment logic
- **Software workaround**: All seats share one GPU
- **Can software remove it?**: Yes — add GPU enumeration
- **Security implications**: None
- **Status**: Known limitation

### G-003: GPU Encoder Contention

- **Limitation**: Multiple seats compete for GPU encoder resources
- **Type**: Hardware limitation
- **Root cause**: Shared GPU encoder hardware
- **Software workaround**: NVENC quality presets per seat
- **Can software remove it?**: No — hardware limit
- **Security implications**: None
- **Status**: Managed via quality presets

---

## Encoder Limitations

### E-001: NVENC Quality vs Latency Trade-off

- **Limitation**: Higher quality presets increase encoding latency
- **Type**: Encoder limitation
- **Root cause**: NVENC architecture — more processing = higher quality = more latency
- **Software workaround**: Configurable presets (P1-P7)
- **Can software remove it?**: No — encoder trade-off
- **Security implications**: None
- **Status**: Managed via presets

### E-002: AV1 Encoding Hardware Requirement

- **Limitation**: AV1 requires RTX 40-series or newer
- **Type**: Hardware limitation
- **Root cause**: AV1 hardware encoder not in older GPUs
- **Software workaround**: Fallback to HEVC/H.264
- **Can software remove it?**: No — hardware requirement
- **Security implications**: None
- **Status**: Handled by Vibepollo encoder probing

---

## Display Limitations

### D-001: SudoVDA Display Created on Client Connect

- **Limitation**: Vibepollo creates virtual display when client connects, not at startup
- **Type**: Provider behavior
- **Root cause**: Vibepollo gates display creation on headless_mode + client connect
- **Software workaround**: Late display detection in health check
- **Can software remove it?**: No — Vibepollo behavior
- **Security implications**: None
- **Status**: Worked around via late detection

### D-002: Display Isolation Doesn't Survive Disconnect

- **Limitation**: SudoVDA primary state lost on session disconnect
- **Type**: Windows display behavior
- **Root cause**: Session disconnect resets display topology
- **Software workaround**: Re-apply display isolation after reconnect
- **Can software remove it?**: No — Windows behavior
- **Security implications**: None
- **Status**: Worked around via re-application

### D-003: HDR Not Working in RDP Sessions

- **Limitation**: VidPN SOURCE mode stays SDR in RDP sessions
- **Type**: Windows display limitation
- **Root cause**: RDP display pipeline doesn't support HDR source modes
- **Software workaround**: Force FP16 primary (Nonary approach — not implemented)
- **Can software remove it?**: Partially — requires D3DKMTSetVidPnSourceOwner
- **Security implications**: None
- **Status**: Known limitation (EnableHdr is no-op)

---

## Audio Limitations

### A-001: No Microphone in PerSession Mode

- **Limitation**: Seat session cannot see host's Steam Streaming Microphone
- **Type**: Windows audio architecture
- **Root cause**: Per-session audio isolation prevents cross-session audio device access
- **Software workaround**: None — deliberate trade-off
- **Can software remove it?**: Wait for Vibepollo WebRTC mic support
- **Security implications**: None
- **Status**: Known limitation (documented)

### A-002: Audio Endpoint Name Localization

- **Limitation**: "Remote Audio" is localized ("Audio remoto" on Spanish)
- **Type**: Windows localization
- **Root cause**: Windows localizes audio endpoint names
- **Software workaround**: Never match by name; use session default
- **Can software remove it?**: No — Windows design
- **Security implications**: None
- **Status**: Handled (never name the endpoint)

---

## Input Limitations

### I-001: Keyboard/Mouse Session Isolation Not Working

- **Limitation**: InputHookManager WH_KEYBOARD_LL/WH_MOUSE_LL hooks are no-op in Session 0
- **Type**: Windows hook limitation
- **Root cause**: Low-level hooks in SYSTEM context see NULL foreground window
- **Software workaround**: None — re-architecture needed to run inside seat session
- **Can software remove it?**: Yes — run hooks inside seat session
- **Security implications**: None
- **Status**: Known limitation (documented as no-op)

### I-002: XInput Limited to 4 Controllers

- **Limitation**: XInput API supports only 4 controllers
- **Type**: Windows API limitation
- **Root cause**: XInput design — 4 player slots
- **Software workaround**: None — physical limit
- **Can software remove it?**: No — API design
- **Security implications**: None
- **Status**: Documented limitation

---

## Game Limitations

### GM-001: Game Mutex / Single Instance

- **Limitation**: Some games use mutex to prevent multiple instances
- **Type**: Game-imposed
- **Root cause**: Game design — prevent cheating/duplication
- **Software workaround**: None — game-specific
- **Can software remove it?**: No — game behavior
- **Security implications**: None
- **Status**: Known limitation

### GM-002: Anti-Cheat in Virtual Sessions

- **Limitation**: Some anti-cheat may flag virtual sessions
- **Type**: Game-imposed
- **Root cause**: Anti-cheat detects RDP/virtual environment
- **Software workaround**: None — game-specific
- **Can software remove it?**: No — game behavior
- **Security implications**: None
- **Status**: Known limitation

### GM-003: Steam Single Instance

- **Limitation**: Steam terminates when second instance starts
- **Type**: Application-imposed
- **Root cause**: Steam design — single instance per machine
- **Software workaround**: Shared game library (avoid re-download)
- **Can software remove it?**: Yes — process patching (Duo approach)
- **Security implications**: Modifies Steam behavior
- **Status**: Workaround via shared library

---

## Anti-Cheat Limitations

### AC-001: Kernel-Level Anti-Cheat

- **Limitation**: Some anti-cheat (Vanguard, EAC) may conflict with virtual sessions
- **Type**: Game-imposed
- **Root cause**: Anti-cheat requires physical hardware access
- **Software workaround**: None — game-specific
- **Can software remove it?**: No — game behavior
- **Security implications**: None
- **Status**: Known limitation

---

## DRM Limitations

### DRM-001: Hardware-Based DRM

- **Limitation**: Some DRM (Denuvo, always-online) may not work in virtual sessions
- **Type**: Game-imposed
- **Root cause**: DRM requires physical hardware attestation
- **Software workaround**: None — game-specific
- **Can software remove it?**: No — game behavior
- **Security implications**: None
- **Status**: Known limitation

---

## Hardware Limitations

### H-001: CPU Limit

- **Limitation**: Each seat runs game + Vibepollo encoder
- **Type**: Hardware limitation
- **Root cause**: Shared CPU resources
- **Software workaround**: Monitor via MetricsCollector, limit seat count
- **Can software remove it?**: No — hardware limit
- **Security implications**: None
- **Status**: Managed via seat count limits

### H-002: RAM Limit

- **Limitation**: Each seat has own Windows session + game
- **Type**: Hardware limitation
- **Root cause**: Shared RAM
- **Software workaround**: Monitor via MetricsCollector
- **Can software remove it?**: No — hardware limit
- **Security implications**: None
- **Status**: Monitored

### H-003: Network Bandwidth

- **Limitation**: Each stream uses network bandwidth
- **Type**: Hardware limitation
- **Root cause**: Shared network link
- **Software workaround**: Configure quality settings per seat
- **Can software remove it?**: No — hardware limit
- **Security implications**: None
- **Status**: Managed via quality settings
