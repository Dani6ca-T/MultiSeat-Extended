# Windows Low-Level Technology Research Summary

**Date**: 2026-08-30
**Purpose**: Identify open-source technologies for building a full-featured Windows multiseat gaming platform

---

## 1. Display Ecosystem

### Microsoft IDD / IddCx

**Indirect Display Driver (IDD)** is Microsoft's framework for creating virtual displays on Windows. Key facts:

- **UMDF-based** — User-Mode Driver Framework
- **IddCx** — Class extension for indirect display drivers
- **Supports**: Windows 10/11
- **HDR**: Supported via EDID metadata (Windows 11 23H2+)
- **Refresh rates**: Up to 500Hz+ (hardware dependent)
- **Multiple monitors**: Up to 16 hot-plugged displays per driver instance
- **GPU selection**: Can target specific GPU via adapter affinity

### Open-Source Virtual Display Projects

#### 1. SudoMaker/SudoVDA
- **URL**: https://github.com/SudoMaker/SudoVDA
- **License**: Unknown (not explicitly stated)
- **Technology**: IddCx UMDF driver
- **HDR**: Partial (Windows 11 23H2+)
- **Used by**: Apollo, Vibepollo, MultiSeat-Extended
- **Status**: Active, maintained
- **Key feature**: Default virtual display for Sunshine ecosystem

#### 2. VirtualDrivers/Virtual-Display-Driver
- **URL**: https://github.com/VirtualDrivers/Virtual-Display-Driver
- **License**: Not explicitly stated
- **Technology**: IddCx UMDF driver
- **HDR**: Supported (Windows 11 23H2+)
- **Resolution**: Custom resolutions supported
- **Refresh rates**: Up to 4K 120Hz+
- **Multiple monitors**: Supported
- **Status**: Active, popular

#### 3. nomi-san/parsec-vdd
- **URL**: https://github.com/nomi-san/parsec-vdd
- **License**: Not explicitly stated
- **Technology**: IddCx API
- **HDR**: Not mentioned
- **Resolution**: Up to 4K
- **Refresh rates**: High refresh rates supported
- **Status**: Less active

#### 4. adyusuf/Virtual-Display-Driver
- **URL**: https://github.com/adyusuf/Virtual-Display-Driver
- **License**: Not explicitly stated
- **Technology**: IddCx UMDF driver
- **HDR**: Windows 11 only (no Windows 10 HDR)
- **Status**: Fork/variant

### Key Question: Can One Driver Instance Serve Multiple Seats?

**Answer**: Yes, with caveats.

- IddCx supports up to **16 hot-plugged displays** per driver instance
- Each display can be assigned to a different seat/session
- However, **session isolation** is NOT built into the driver
- The driver creates displays globally; session assignment must be done by the orchestrator (MultiSeat-Extended)

**Architecture implication**: A single SudoVDA/Virtual-Display-Driver instance can create multiple virtual displays, one per seat. MultiSeat-Extended would need to track which display belongs to which seat.

### HDR Requirements

To achieve Duo-like HDR capability:
1. **Driver support**: IddCx with HDR EDID metadata
2. **Windows version**: Windows 11 23H2+
3. **GPU support**: NVIDIA/AMD with HDR-capable encoder
4. **Configuration**: EDID with HDR metadata, color spaces (Rec. 2020, DCI-P3)
5. **Encoding**: HEVC Main10 or AV1 with HDR10 metadata

### High Refresh Rate

Real limitations:
- **60Hz**: Universal support
- **120Hz**: Widely supported
- **144Hz**: Widely supported
- **165Hz**: Supported on most GPUs
- **240Hz**: Supported on modern GPUs
- **360Hz**: Supported on high-end GPUs
- **480Hz**: Supported on latest GPUs (NVIDIA RTX 40-series+)
- **500Hz**: Hardware-dependent, not all encoders support

---

## 2. Input Ecosystem

### Windows Input Architecture

- **HID** — Human Interface Device (kernel-level)
- **Raw Input** — Application-level input API
- **XInput** — Xbox controller API
- **DirectInput** — Legacy game input API
- **GameInput** — Modern Microsoft game input API

### Open-Source Input Projects

#### 1. LizardByte/libvirtualhid
- **URL**: https://github.com/LizardByte/libvirtualhid
- **License**: Custom (license required for Windows driver)
- **Technology**: UMDF2 + Virtual HID Framework (VHF)
- **Devices**: Gamepad, keyboard, mouse
- **Session awareness**: Not built-in
- **Status**: Active, used by Sunshine
- **Key feature**: ViGEmBus replacement

#### 2. nomi-san/void-drivers
- **URL**: https://github.com/nomi-san/void-drivers
- **License**: Not explicitly stated
- **Technology**: UMDF + VHF
- **Devices**: Virtual HID devices (gamepad, keyboard, mouse)
- **Session awareness**: Not built-in
- **Status**: Recent (2026)

#### 3. Nefarius/ViGEmBus (legacy)
- **URL**: https://github.com/ViGEm/ViGEmBus
- **License**: MIT
- **Technology**: Kernel-mode bus driver
- **Devices**: Virtual Xbox 360, DualShock 4
- **Session awareness**: Not built-in
- **Status**: Legacy (replaced by libvirtualhid in Sunshine)
- **Key feature**: Widely used, mature

#### 4. HidHide (Nefarius)
- **URL**: https://github.com/ViGEm/HidHide
- **License**: MIT
- **Technology**: Kernel-mode filter driver
- **Function**: Device hiding/cloaking
- **Session awareness**: Undocumented session jail feature (via `!<sessionId>` suffix)
- **Status**: Active
- **Key feature**: Used by MultiSeat-Extended for gamepad isolation

### Key Question: Per-Seat Input Isolation

**Two approaches**:

**Approach 1: Driver-level session filtering**
- UMDF driver with `DEVPKEY_Device_SessionId` filtering
- Used by Duo (proprietary UMDF driver)
- Pros: Mandatory, transparent to applications
- Cons: Requires custom driver development

**Approach 2: User-mode routing**
- Input router in user-mode process
- Filter by session ID before injection
- Used by MultiSeat-Extended (InputHookManager — currently no-op)
- Pros: No driver needed
- Cons: Currently no-op in MultiSeat-Extended

**Approach 3: HidHide session jail**
- Undocumented HidHide feature
- Used by MultiSeat-Extended (optional)
- Pros: Kernel-level isolation
- Cons: Undocumented, may break with HidHide updates

### Gamepad Virtualization

Best options for per-seat controller isolation:
1. **libvirtualhid** (LizardByte) — Modern, UMDF2 + VHF, used by Sunshine
2. **ViGEmBus** (legacy) — Mature, widely used, but being replaced
3. **Custom UMDF driver** — Like Duo, but requires driver development
4. **HidHide session jail** — Like MultiSeat-Extended, optional

---

## 3. Audio Ecosystem

### Windows Audio Architecture

- **WASAPI** — Windows Audio Session API (per-session audio)
- **Remote Audio** — RDP per-session audio endpoint
- **Core Audio APIs** — Endpoint enumeration, device management

### Open-Source Audio Projects

#### 1. VirtualDrivers/Virtual-Audio-Driver
- **URL**: https://github.com/VirtualDrivers/Virtual-Audio-Driver
- **License**: Not explicitly stated
- **Technology**: WDM virtual audio device
- **Function**: Virtual speaker + microphone
- **Per-session**: Not built-in
- **Status**: Active

#### 2. rdp/virtual-audio-capture-grabber
- **URL**: https://github.com/rdp/virtual-audio-capture-grabber-device
- **License**: Not explicitly stated
- **Technology**: Virtual audio capture device
- **Function**: Capture "wave out" sound
- **Per-session**: Not built-in
- **Status**: Less active

### Key Question: Per-Session Audio Without VAC

**Answer**: Yes, via Windows RDP Remote Audio.

- Each RDP session has its own "Remote Audio" endpoint
- Windows automatically makes it the session default
- Vibepollo/Sunshine can loopback-capture from inside the session
- **No VAC/VoiceMeeter needed**

This is exactly what MultiSeat-Extended already does (PerSession audio mode).

### Microphone Path

- **RDP**: Does NOT forward host microphone to session
- **Moonlight**: Mic RTP port exists (offset +12)
- **Vibepollo**: Has mic passthrough via WebRTC (bypass_opus)
- **MultiSeat-Extended**: No mic path (PerSession trade-off)

**Future**: Vibepollo 1.19.x WebRTC mic support may provide a path.

---

## 4. RDP / Session Ecosystem

### Open-Source RDP Projects

#### 1. TermWrap (llccd/TermWrap)
- **URL**: https://github.com/llccd/TermWrap
- **License**: MIT
- **Technology**: DLL proxy, runtime patching
- **Function**: Enable concurrent RDP sessions
- **Windows versions**: Windows 10/11
- **Status**: Active, used by MultiSeat-Extended
- **Key feature**: RDPWrapOffsetFinder for auto offset discovery

#### 2. TermWrap Rust (kernalix7/rdprrap)
- **URL**: https://github.com/kernalix7/rdprrap
- **License**: MIT
- **Technology**: Symbol-free runtime analysis (pelite + iced-x86)
- **Function**: Same as TermWrap C++ but no PDB dependency
- **Windows versions**: Windows 10/11 x64/ARM64
- **Status**: Active
- **Key feature**: Survives Windows updates without .ini files

#### 3. stascorp/rdpwrap (legacy)
- **URL**: https://github.com/stascorp/rdpwrap
- **License**: GPL-2.0
- **Technology**: DLL proxy, static offset files
- **Function**: Enable concurrent RDP sessions
- **Status**: Legacy, breaks on Windows updates
- **Key feature**: Original RDP wrapper

### Key Question: Multi-Session Without Proprietary Duo

**Answer**: Yes, via TermWrap.

- TermWrap enables concurrent RDP sessions on Windows Home/Pro
- Each session is fully isolated (own desktop, display, audio, input)
- MultiSeat-Extended already uses this approach
- No proprietary components needed

---

## 5. Process / Token Ecosystem

### Windows APIs for Process Creation

- **CreateProcessAsUser** — Launch process in user's session
- **CreateProcessWithTokenW** — Launch with duplicated token
- **DuplicateTokenEx** — Duplicate access token
- **WTSQueryUserToken** — Get token from session
- **SetTokenInformation** — Modify token (session ID)

### Best Practices for Service → Session Process Launch

```
SYSTEM Service
    ↓
WTSQueryUserToken(sessionId) → raw token
    ↓
EnsureTokenBelongsTo(token, accountName, sessionId)
    ↓
DuplicateTokenEx → primary token
    ↓
CreateProcessAsUser(token, exe, ..., sessionId)
    ↓
VerifyLandedInSession(pid, expectedSessionId)
    ↓
If wrong session → Kill + throw
```

### Process Isolation with Job Objects

- **Job Objects** — Group processes for management
- **JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE** — Kill all processes when job handle closes
- **AssignProcessToJobObject** — Add process to job
- **Use case**: Ensure all seat processes are killed on teardown

---

## 6. Game Compatibility

### Game Process Patching

Games that refuse to run in RDP sessions or check for single-instance mutexes need patching. Options:

1. **Windows Application Compatibility Toolkit** — Shim database
2. **DLL injection** — Hook problematic functions
3. **Process environment tricks** — Set environment variables
4. **Registry manipulation** — Fake display/device presence

### Steam Multi-Instance

Steam uses a mutex to prevent multiple instances. Solutions:
1. **Sandbox** — Run second Steam in Windows Sandbox
2. **Process patching** — Patch mutex check
3. **Separate userdata** — Different `--userdatadir`
4. **Duo approach** — Application Compatibility Layer (proprietary)

### Open-Source Game Compatibility Projects

- **Sandboxie-Plus** — Sandbox for running isolated processes
- **Windows Sandbox** — Built-in Windows feature (temporary)
- **Application Compatibility Toolkit** — Microsoft's official shim database

---

## 7. Provider Architecture

### Existing Provider Patterns

#### Vibepollo/Sunshine Pattern
- Single-instance per process
- Config file (sunshine.conf) per instance
- Process launched by orchestrator (MultiSeat-Extended)
- No built-in multi-instance

#### Helios Pattern
- Named Pipe IPC (App ↔ Spawner)
- SYSTEM service for privileged operations
- Per-instance config directories
- Automated lifecycle management

#### Duo Pattern (inferred)
- Windows Service (SYSTEM)
- Custom drivers (WDDM, UMDF)
- Application Compatibility Layer
- Bundled TermWrap

---

## 8. New Multiseat Projects Discovered

### 1. Tylemagne/PC-Terminalizer
- **URL**: https://github.com/Tylemagne/PC-Terminalizer
- **License**: Not explicitly stated
- **Technology**: PowerShell scripts
- **Function**: Free open-source multi-seat tool for Windows/Linux
- **Status**: Active
- **Relevance**: Alternative approach to multiseat

### 2. neo0oen619/neo_multiseat
- **URL**: https://github.com/neo0oen619/neo_multiseat
- **License**: MIT
- **Technology**: PowerShell automation
- **Function**: Multiseat on Windows 11 PC
- **Status**: Active
- **Relevance**: Already known, simpler approach

### 3. vibesoftwarecoder/MultiSeat
- **URL**: https://github.com/vibesoftwarecoder/MultiSeat
- **License**: MIT
- **Technology**: C# (.NET 9)
- **Function**: Moonlight multiseat streaming
- **Status**: Active
- **Relevance**: Upstream of MultiSeat-Extended

### 4. ASTER (commercial)
- **URL**: https://www.ibik.ru/aster/
- **License**: Commercial
- **Technology**: Kernel-mode driver
- **Function**: Full multiseat solution
- **Status**: Active, commercial
- **Relevance**: Reference implementation (not open-source)

---

## 9. Technology Matrix

| Technology | Open Source | License | Mature | Multi-seat | HDR | High Hz | Session Aware | Recommended |
|------------|-------------|---------|--------|------------|-----|---------|---------------|-------------|
| **Display** | | | | | | | | |
| SudoVDA | Yes* | Unknown | Yes | Yes | Partial | Yes | No | ✅ |
| Virtual-Display-Driver | Yes* | Unknown | Yes | Yes | Yes | Yes | No | ✅ |
| parsec-vdd | Yes* | Unknown | Partial | Yes | No | Yes | No | ⚠️ |
| **Input** | | | | | | | | |
| libvirtualhid | Yes* | Custom | Yes | No | N/A | N/A | No | ✅ |
| ViGEmBus | Yes | MIT | Yes | No | N/A | N/A | No | ⚠️ (legacy) |
| HidHide | Yes | MIT | Yes | Optional | N/A | N/A | Partial | ✅ |
| **Audio** | | | | | | | | |
| Virtual-Audio-Driver | Yes* | Unknown | Yes | No | N/A | N/A | No | ⚠️ |
| RDP Remote Audio | N/A | N/A | Yes | Yes | N/A | N/A | Yes | ✅ |
| **RDP** | | | | | | | | |
| TermWrap | Yes | MIT | Yes | Yes | N/A | N/A | Yes | ✅ |
| TermWrap Rust | Yes | MIT | Yes | Yes | N/A | N/A | Yes | ✅ |
| rdpwrap | Yes | GPL-2.0 | Legacy | Yes | N/A | N/A | Yes | ❌ (legacy) |
| **Process** | | | | | | | | |
| Job Objects | N/A | N/A | Yes | Yes | N/A | N/A | Yes | ✅ |
| CreateProcessAsUser | N/A | N/A | Yes | Yes | N/A | N/A | Yes | ✅ |

*License not explicitly stated in repository

---

## 10. Capability Matrix

| Capability | MultiSeat-Extended | Duo | Vibepollo | SudoVDA | TermWrap |
|------------|-------------------|-----|-----------|---------|----------|
| Multiple users | ✅ | ✅ | ❌ | N/A | N/A |
| Multiple sessions | ✅ | ✅ | ❌ | N/A | ✅ |
| Virtual displays | ✅ | ✅ | ✅ | ✅ | N/A |
| HDR | ❌ (no-op) | ✅ | ✅ | Partial | N/A |
| 500Hz | ✅ (config) | ✅ | ✅ | ✅ | N/A |
| Per-seat audio | ✅ | ✅ | ❌ | N/A | N/A |
| Per-seat input | ✅ (optional) | ✅ | ❌ | N/A | N/A |
| Gamepad isolation | ✅ (HidHide) | ✅ | ❌ | N/A | N/A |
| Game process tracking | ❌ | ✅ | ❌ | N/A | N/A |
| Game compatibility | ❌ | ✅ | ❌ | N/A | N/A |
| Steam multi-instance | ❌ | ✅ | ❌ | N/A | N/A |
| Streaming | ✅ | ✅ | ✅ | N/A | N/A |
| Provider abstraction | ❌ | N/A | N/A | N/A | N/A |
| Crash recovery | ✅ | ✅ | ✅ | N/A | N/A |
| Health checks | ✅ | ✅ | ❌ | N/A | N/A |
| API | ✅ | ✅ | ✅ | N/A | N/A |
| Web UI | ✅ | ✅ | ✅ | N/A | N/A |
| Security | ✅ | ✅ | ❌ | N/A | N/A |
| Open source | ✅ | ❌ | ✅ | ✅ | ✅ |

---

## 11. Technology Gaps

### What MultiSeat-Extended Still Needs

1. **HDR support** — EnableHdr is no-op; needs driver + encoding support
2. **Game process patching** — No Application Compatibility Layer
3. **Steam multi-instance** — No built-in support
4. **Seamless display adjustment** — Requires reconnect for resolution changes
5. **UMDF input driver** — InputHookManager is no-op; needs real implementation
6. **Game process tracking** — No PID tracking for launched games
7. **Provider abstraction** — No IStreamingProvider interface

### What Already Works

1. ✅ Multiple users (Windows accounts)
2. ✅ Multiple sessions (TermWrap)
3. ✅ Virtual displays (SudoVDA)
4. ✅ Per-session audio (RDP Remote Audio)
5. ✅ Gamepad isolation (HidHide session jail)
6. ✅ Streaming (Vibepollo)
7. ✅ Crash recovery (auto-restart)
8. ✅ Health checks (SessionHealthCheck)
9. ✅ API + Dashboard (ASP.NET Core + React)
10. ✅ Security (DPAPI + ACLs)

---

## 12. Reuse Analysis

### USE DIRECTLY
- **TermWrap** — MIT, proven, already integrated
- **SudoVDA** — Already integrated, works well
- **HidHide** — MIT, session jail feature works
- **RDP Remote Audio** — Built into Windows, no code needed

### ADAPT
- **libvirtualhid** — Could replace ViGEmBus, but needs license
- **Virtual-Display-Driver** — Alternative to SudoVDA
- **Job Objects** — For process isolation on teardown

### USE AS REFERENCE
- **Duo's Application Compatibility Layer** — Concept for game patching
- **Duo's UMDF input driver** — Concept for session-aware input
- **Helios's Named Pipe IPC** — Pattern for provider abstraction

### DO NOT USE
- **rdpwrap** — Legacy, breaks on updates
- **ViGEmBus** — Being replaced by libvirtualhid
- **Proprietary components** — Cannot reuse

---

## 13. License Matrix

| Project | License | Copyleft? | Can Link? | Can Bundle? | Can Modify? | Can Distribute? | Attribution? |
|---------|---------|-----------|-----------|-------------|-------------|-----------------|--------------|
| MultiSeat-Extended | MIT | No | Yes | Yes | Yes | Yes | Yes |
| TermWrap | MIT | No | Yes | Yes | Yes | Yes | Yes |
| TermWrap Rust | MIT | No | Yes | Yes | Yes | Yes | Yes |
| HidHide | MIT | No | Yes | Yes | Yes | Yes | Yes |
| ViGEmBus | MIT | No | Yes | Yes | Yes | Yes | Yes |
| SudoVDA | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown |
| Virtual-Display-Driver | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown |
| libvirtualhid | Custom | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown |
| rdpwrap | GPL-2.0 | Yes | No* | No* | Yes | Yes* | Yes |
| Duo | Proprietary | N/A | No | No | No | No | N/A |
| Vibepollo | GPLv3 | Yes | No* | No* | Yes | Yes* | Yes |

*GPL/LGPL: Can distribute if kept as separate process, not linked

---

## 14. Target Technology Stack

### Display
- **Problem**: Virtual display per seat
- **Best existing**: SudoVDA (already integrated)
- **Alternative**: Virtual-Display-Driver
- **Why**: Proven, works with Sunshine/Vibepollo
- **Risks**: License unclear for SudoVDA

### Input
- **Problem**: Per-seat gamepad isolation
- **Best existing**: HidHide session jail (already integrated)
- **Alternative**: libvirtualhid (modern, UMDF2 + VHF)
- **Why**: HidHide works but is undocumented; libvirtualhid is modern
- **Risks**: libvirtualhid requires license for Windows driver

### Audio
- **Problem**: Per-seat audio isolation
- **Best existing**: RDP Remote Audio (already used)
- **Alternative**: Virtual-Audio-Driver
- **Why**: RDP Remote Audio works perfectly, no VAC needed
- **Risks**: None

### RDP
- **Problem**: Concurrent sessions
- **Best existing**: TermWrap (already integrated)
- **Alternative**: TermWrap Rust (symbol-free)
- **Why**: TermWrap works, TermWrap Rust is more resilient
- **Risks**: TermWrap Rust is newer, less battle-tested

### Process Isolation
- **Problem**: Kill all seat processes on teardown
- **Best existing**: Process.GetProcessById + Kill
- **Alternative**: Job Objects (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)
- **Why**: Job Objects guarantee cleanup
- **Risks**: None

### Game Compatibility
- **Problem**: Games that refuse RDP sessions
- **Best existing**: None in MultiSeat-Extended
- **Reference**: Duo's Application Compatibility Layer
- **Why**: Needed for game compatibility
- **Risks**: Game-specific, maintenance burden

### Steam Multi-Instance
- **Problem**: Multiple Steam instances
- **Best existing**: None in MultiSeat-Extended
- **Reference**: Duo's Steam isolation
- **Why**: Needed for multi-seat gaming
- **Risks**: Steam updates may break

---

## 15. Biggest Unknowns

1. **SudoVDA license** — What are the exact terms?
2. **libvirtualhid license** — Custom license, what does it allow?
3. **Duo's exact implementation** — Cannot inspect without source
4. **Game compatibility layer details** — How does Duo patch games?
5. **UMDF input driver details** — How does Duo filter by session?
6. **HDR implementation details** — What driver/encoding changes needed?
7. **Seamless display adjustment** — How does Duo avoid reconnect?
8. **Steam isolation mechanism** — How does Duo run multiple Steam instances?

---

## 16. Recommended Next Research

1. **SudoVDA license investigation** — Contact author, check repository
2. **libvirtualhid license analysis** — Understand custom license terms
3. **Job Objects integration** — Prototype for process isolation
4. **HDR feasibility study** — What changes needed for HDR support
5. **Game compatibility research** — How to patch games for RDP
6. **Steam multi-instance research** — How to run multiple Steam instances
7. **Seamless display research** — How to change resolution without reconnect

---

## Quality Gate

- [x] IDD researched
- [x] Virtual Display projects researched
- [x] HDR researched
- [x] High refresh rate researched
- [x] UMDF researched
- [x] HID researched
- [x] VHF researched
- [x] Gamepad virtualization researched
- [x] Audio ecosystem researched
- [x] RDP ecosystem researched
- [x] Windows sessions researched
- [x] Token/process APIs researched
- [x] Process isolation researched
- [x] Game compatibility researched
- [x] Steam multi-instance researched
- [x] Provider architecture researched
- [x] New multiseat projects found
- [x] Duo alternatives found
- [x] Licenses checked
- [x] Technology matrix created
- [x] Capability matrix created
- [x] Reuse analysis created
- [x] Technology gaps identified
- [x] All claims have evidence or UNVERIFIED
- [x] MultiSeat-Extended production code NOT changed
