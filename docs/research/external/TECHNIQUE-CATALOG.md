# Technique Catalog

**Date**: 2026-08-30
**Purpose**: Catalog all Windows techniques discovered across researched projects

---

## RDP / Session Management

### 1. RDP Wrapper (termsrv.dll patching)

**Source**: TermWrap, Duo, neo_multiseat

**Technique**: DLL proxy that patches termsrv.dll at runtime

**Implementation**:
- TermWrap: DLL proxy with auto offset discovery
- rdpwrap: Manual .ini files (abandoned)
- Duo: Bundled TermWrap (proprietary)

**Patches**:
- DefPolicyPatch (concurrent sessions)
- SingleUserPatch (single user restriction)
- LocalOnlyPatch (local only restriction)

**Windows APIs**:
- DLL proxying
- PDB symbol parsing (offset discovery)
- Registry modification

**License implications**:
- TermWrap: MIT
- rdpwrap: GPL-2.0
- Duo: Proprietary

---

### 2. CreateProcessAsUser

**Source**: Helios, MultiSeat-Extended

**Technique**: Launch process with different token

**Implementation**:
1. `WTSGetActiveConsoleSessionId()` — Get interactive session
2. `WTSQueryUserToken()` — Get user token (for env block)
3. `OpenProcessToken()` — Get SYSTEM token
4. `DuplicateTokenEx()` — Create primary token
5. `SetTokenInformation()` — Assign token to session
6. `CreateEnvironmentBlock()` — Build user environment
7. `CreateProcessAsUser()` — Launch process

**Security context**:
- SYSTEM token (full privileges)
- Assigned to user's session
- User's environment variables
- Can capture Winlogon desktop

**Evidence**:
- Helios: ProcessLauncher.LaunchViaCreateProcessAsUser()
- MultiSeat-Extended: SessionLauncher

---

### 3. WTS APIs

**Source**: Helios, MultiSeat-Extended

**Technique**: Windows Terminal Services APIs

**APIs**:
- `WTSGetActiveConsoleSessionId()` — Get active console session
- `WTSQueryUserToken()` — Get user token for session
- `WTSEnumerateSessions()` — List sessions
- `WTSQuerySessionInformation()` — Get session info

**Use cases**:
- Session discovery
- Token acquisition
- Session monitoring

**Evidence**:
- Helios: ProcessLauncher.cs
- MultiSeat-Extended: SessionLauncher

---

### 4. Token Manipulation

**Source**: Helios, MultiSeat-Extended

**Technique**: Windows token manipulation

**APIs**:
- `OpenProcessToken()` — Access process token
- `DuplicateTokenEx()` — Create new token
- `SetTokenInformation()` — Modify token
- `GetTokenInformation()` — Query token
- `AdjustTokenPrivileges()` — Modify privileges

**Use cases**:
- Privilege escalation (SYSTEM → user session)
- Session assignment
- Elevation verification

**Evidence**:
- Helios: ProcessLauncher.cs
- MultiSeat-Extended: Security implementations

---

## Display Management

### 5. SudoVDA (Virtual Display)

**Source**: MultiSeat-Extended, Apollo, Vibepollo

**Technique**: IddCx-based virtual display driver

**Implementation**:
- Kernel-mode driver (IddCx)
- Creates virtual monitors
- Hotplug support
- Resolution/refresh rate control

**Use cases**:
- Virtual display per seat
- HDR support
- High refresh rate
- Multiple monitors

**License**:
- Unknown (SudoMaker)

**Evidence**:
- MultiSeat-Extended: SudoVDA integration
- Apollo: Built-in SudoVDA
- Vibepollo: Own bundled driver

---

### 6. WDDM Display Driver

**Source**: Duo

**Technique**: Windows Display Driver Model

**Implementation**:
- Custom WDDM driver
- More integrated than IddCx
- Session-aware

**Use cases**:
- Virtual display per seat
- Seamless display adjustment
- HDR support

**License**:
- Proprietary

**Evidence**:
- Duo: Custom WDDM driver (proprietary)

---

### 7. Display Isolation (Primary + Shrunk)

**Source**: MultiSeat-Extended

**Technique**: SudoVDA primary + RDP shrunk

**Implementation**:
- SudoVDA as primary display
- RDP display shrunk to minimum
- Reduces CPU usage
- True isolation

**Use cases**:
- Per-seat display isolation
- CPU optimization
- Independent resolution

**Evidence**:
- MultiSeat-Extended: Display isolation implementation

---

## Audio Management

### 8. RDP Remote Audio

**Source**: MultiSeat-Extended

**Technique**: Windows RDP per-session audio

**Implementation**:
- Per-session audio endpoint
- No VAC needed
- True isolation
- Low overhead

**Use cases**:
- Per-seat audio isolation
- Game audio
- Microphone (limited)

**Limitations**:
- No microphone path (MultiSeat-Extended)
- Windows dependency

**Evidence**:
- MultiSeat-Extended: PerSession audio

---

### 9. WASAPI Loopback

**Source**: Vibepollo, Apollo

**Technique**: Windows Audio Session API

**Implementation**:
- Loopback capture
- Device selection
- Per-session routing

**Use cases**:
- Audio capture
- Per-session audio
- Device assignment

**Evidence**:
- Vibepollo: WASAPI capture
- Apollo: WASAPI capture

---

### 10. Virtual Audio Cable (VAC)

**Source**: External

**Technique**: Virtual audio device

**Implementation**:
- Kernel-mode driver
- Virtual speaker/microphone
- Audio routing

**Use cases**:
- Per-seat audio isolation
- Audio routing
- Recording

**License**:
- Various (some commercial)

**Evidence**:
- External projects (Virtual-Audio-Driver)

---

## Input Management

### 11. HidHide Session Jail

**Source**: MultiSeat-Extended

**Technique**: Undocumented HidHide feature

**Implementation**:
- HidHide driver
- Session ID filtering
- Gamepad isolation

**Use cases**:
- Per-seat gamepad isolation
- Input device assignment
- Gamepad hiding

**Limitations**:
- Undocumented feature
- May break in future updates
- Driver dependency

**Evidence**:
- MultiSeat-Extended: HidHideConfigurator

---

### 12. UMDF Input Driver

**Source**: Duo

**Technique**: User-Mode Driver Framework

**Implementation**:
- Custom UMDF driver
- Session ID filtering
- HID device creation
- Input routing

**Use cases**:
- Per-seat input isolation
- Gamepad virtualization
- Keyboard/mouse isolation

**License**:
- Proprietary

**Evidence**:
- Duo: UMDF input driver (proprietary)

---

### 13. ViGEmBus (Legacy)

**Source**: External

**Technique**: Virtual gamepad bus driver

**Implementation**:
- Kernel-mode bus driver
- Virtual Xbox/DS4 controllers
- XInput/DirectInput support

**Use cases**:
- Virtual gamepad creation
- Gamepad emulation
- Controller virtualization

**License**:
- MIT

**Evidence**:
- ViGEm/ViGEmBus (legacy)

---

### 14. libvirtualhid (Modern)

**Source**: LizardByte/libvirtualhid

**Technique**: UMDF2 + VHF virtual HID

**Implementation**:
- UMDF2 driver
- VHF (Virtual HID Framework)
- Modern replacement for ViGEmBus

**Use cases**:
- Virtual gamepad creation
- HID device virtualization
- Controller virtualization

**License**:
- Custom (license required)

**Evidence**:
- LizardByte/libvirtualhid

---

## Process Management

### 15. Job Objects

**Source**: None (missing pattern)

**Technique**: Windows Job Objects

**Implementation**:
- Process group management
- Resource limits
- Kill on job close

**Use cases**:
- Process isolation on teardown
- Resource limits
- Cleanup guarantee

**Evidence**:
- None (recommended for MultiSeat-Extended)

---

### 16. WMI Process Discovery

**Source**: Helios

**Technique**: Windows Management Instrumentation

**Implementation**:
- `ManagementObjectSearcher`
- Query `Win32_Process`
- Match by command line/executable path

**Use cases**:
- Process discovery
- Residual process detection
- Process matching

**Evidence**:
- Helios: ProcessManager.FindResidualInstancePids()

---

### 17. Graceful Shutdown

**Source**: Helios, MultiSeat-Extended

**Technique**: Graceful process termination

**Implementation**:
- Send close message
- Wait for timeout
- Force terminate if needed

**Use cases**:
- Clean shutdown
- Data preservation
- Resource cleanup

**Evidence**:
- Helios: GracefulShutdown.cs
- MultiSeat-Extended: Shutdown implementations

---

## Security

### 18. DPAPI (Data Protection API)

**Source**: MultiSeat-Extended

**Technique**: Windows data protection

**Implementation**:
- Encrypt credentials
- Per-user encryption
- Machine-level protection

**Use cases**:
- Credential storage
- API key protection
- Certificate protection

**Evidence**:
- MultiSeat-Extended: DPAPI usage

---

### 19. ACL (Access Control List)

**Source**: MultiSeat-Extended

**Technique**: Windows access control

**Implementation**:
- File/directory permissions
- Registry permissions
- Process permissions

**Use cases**:
- Security hardening
- Access restriction
- Privilege management

**Evidence**:
- MultiSeat-Extended: ACL usage

---

### 20. API Key Authentication

**Source**: MultiSeat-Extended

**Technique**: HTTP API authentication

**Implementation**:
- API key in headers
- CORS configuration
- Rate limiting

**Use cases**:
- API security
- Client authentication
- Access control

**Evidence**:
- MultiSeat-Extended: API key middleware

---

## Streaming

### 21. Moonlight Protocol

**Source**: Vibepollo, Apollo

**Technique**: Game streaming protocol

**Implementation**:
- RTSP signaling
- RTP media
- WebRTC alternative
- H.264/H.265/AV1 encoding

**Use cases**:
- Game streaming
- Low latency
- Hardware encoding

**Evidence**:
- Vibepollo: Moonlight protocol
- Apollo: Moonlight protocol

---

### 22. DXGI Desktop Duplication

**Source**: Vibepollo, Apollo

**Technique**: Desktop capture API

**Implementation**:
- DXGI output duplication
- Hardware-accelerated capture
- HDR support

**Use cases**:
- Desktop capture
- Game capture
- HDR capture

**Evidence**:
- Vibepollo: DDA capture
- Apollo: DDA capture

---

### 23. Windows Graphics Capture (WGC)

**Source**: Vibepollo, Apollo

**Technique**: Modern capture API

**Implementation**:
- Windows 10 1803+
- Per-monitor capture
- Hardware-accelerated

**Use cases**:
- Desktop capture
- Game capture
- Window capture

**Evidence**:
- Vibepollo: WGC capture
- Apollo: WGC capture

---

## Configuration

### 24. sunshine.conf

**Source**: Vibepollo, Apollo

**Technique**: Configuration file format

**Implementation**:
- Key-value pairs
- Per-instance configuration
- Web UI editing

**Use cases**:
- Server configuration
- Client pairing
- Display/audio settings

**Evidence**:
- Vibepollo: sunshine.conf
- Apollo: sunshine.conf

---

### 25. JSON Configuration

**Source**: Helios, MultiSeat-Extended

**Technique**: JSON-based configuration

**Implementation**:
- JSON files
- Schema validation
- Runtime updates

**Use cases**:
- Application configuration
- Instance settings
- Runtime state

**Evidence**:
- Helios: settings.json
- MultiSeat-Extended: appsettings.json

---

## Evidence Summary

| Technique | Source | Evidence | Status |
|-----------|--------|----------|--------|
| RDP Wrapper | TermWrap, Duo, neo_multiseat | README, releases | VERIFIED |
| CreateProcessAsUser | Helios, MultiSeat-Extended | ProcessLauncher.cs | VERIFIED |
| WTS APIs | Helios, MultiSeat-Extended | ProcessLauncher.cs | VERIFIED |
| Token Manipulation | Helios, MultiSeat-Extended | ProcessLauncher.cs | VERIFIED |
| SudoVDA | MultiSeat-Extended, Apollo, Vibepollo | Integration code | VERIFIED |
| WDDM Driver | Duo | Proprietary | UNVERIFIED |
| Display Isolation | MultiSeat-Extended | Implementation | VERIFIED |
| RDP Remote Audio | MultiSeat-Extended | PerSession audio | VERIFIED |
| WASAPI Loopback | Vibepollo, Apollo | Audio capture | VERIFIED |
| HidHide Session Jail | MultiSeat-Extended | HidHideConfigurator | VERIFIED |
| UMDF Input Driver | Duo | Proprietary | UNVERIFIED |
| ViGEmBus | External | MIT license | VERIFIED |
| libvirtualhid | LizardByte | Custom license | VERIFIED |
| Job Objects | None | Recommended | UNVERIFIED |
| WMI Process Discovery | Helios | ProcessManager.cs | VERIFIED |
| Graceful Shutdown | Helios, MultiSeat-Extended | Shutdown implementations | VERIFIED |
| DPAPI | MultiSeat-Extended | Security implementations | VERIFIED |
| ACL | MultiSeat-Extended | Security implementations | VERIFIED |
| API Key Auth | MultiSeat-Extended | API middleware | VERIFIED |
| Moonlight Protocol | Vibepollo, Apollo | Streaming implementation | VERIFIED |
| DXGI Desktop Duplication | Vibepollo, Apollo | Capture implementation | VERIFIED |
| Windows Graphics Capture | Vibepollo, Apollo | Capture implementation | VERIFIED |
| sunshine.conf | Vibepollo, Apollo | Configuration format | VERIFIED |
| JSON Configuration | Helios, MultiSeat-Extended | Configuration files | VERIFIED |
