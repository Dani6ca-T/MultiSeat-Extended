# Technology Gaps

**Date**: 2026-08-30
**Purpose**: Identify what technologies are still needed for a full open-source multiseat gaming platform

---

## Current State: What MultiSeat-Extended Already Has

| Capability | Technology | Status |
|------------|------------|--------|
| Multiple users | Windows accounts (NetApi32) | ✅ Working |
| Multiple sessions | TermWrap (multi-session RDP) | ✅ Working |
| Virtual displays | SudoVDA (IddCx) | ✅ Working |
| Per-session audio | RDP Remote Audio endpoints | ✅ Working |
| Gamepad isolation | HidHide session jail | ✅ Working (optional) |
| Streaming | Vibepollo (Sunshine fork) | ✅ Working |
| Crash recovery | Auto-restart (max 3) | ✅ Working |
| Health checks | SessionHealthCheck (5s) | ✅ Working |
| API | ASP.NET Core Minimal API | ✅ Working |
| Dashboard | React SPA | ✅ Working |
| Security | DPAPI + ACL hardening | ✅ Working |

---

## Gaps: What's Missing for Duo-like System

### Gap 1: HDR Support

**Problem**: MultiSeat-Extended has `EnableHdr` flag but it's a no-op.

**What's needed**:
1. Driver support: IddCx with HDR EDID metadata
2. Windows version: Windows 11 23H2+
3. GPU support: NVIDIA/AMD with HDR-capable encoder
4. Encoding: HEVC Main10 or AV1 with HDR10 metadata
5. Configuration: EDID with HDR metadata, color spaces

**Open-source solution**: Partially available
- SudoVDA has partial HDR support
- Virtual-Display-Driver has HDR support
- Vibepollo has HDR metadata handling

**Gap**: MultiSeat-Extended doesn't read/write HDR config, doesn't force FP16 primary

**Reference**: Duo has working HDR (supporter feature)

---

### Gap 2: Game Process Patching

**Problem**: Games that refuse to run in RDP sessions or check for single-instance mutexes don't work.

**What's needed**:
1. Detect problematic games
2. Patch process to bypass checks
3. Handle DirectX 8/9 applications
4. Handle applications that refuse remote sessions

**Open-source solution**: None available
- Duo has Application Compatibility Layer (proprietary)
- Windows Application Compatibility Toolkit exists but is complex

**Gap**: No open-source game patching for RDP sessions

**Reference**: Duo v1.5.5: "Added support for applications that actively refuse remote sessions"

---

### Gap 3: Steam Multi-Instance

**Problem**: Steam uses a mutex to prevent multiple instances.

**What's needed**:
1. Run multiple Steam clients simultaneously
2. Each Steam client in its own seat
3. Isolate Steam userdata per seat
4. Handle Steamworks SDK

**Open-source solution**: Partially available
- Sandboxie-Plus can sandbox Steam
- Windows Sandbox can isolate Steam
- `--userdatadir` flag for separate userdata

**Gap**: No seamless multi-instance Steam support

**Reference**: Duo v1.5.1: "Added Steam multiboxing support"

---

### Gap 4: Seamless Display Adjustment

**Problem**: Changing resolution requires disconnecting and reconnecting the RDP session.

**What's needed**:
1. Change resolution without session disconnect
2. Client-driven resolution matching
3. No stream interruption

**Open-source solution**: None available
- Requires display driver support
- Requires session manipulation

**Gap**: Fundamental Windows RDP limitation

**Reference**: Duo has seamless display adjustment (inferred from README)

---

### Gap 5: UMDF Input Driver

**Problem**: InputHookManager is no-op; no real session-aware input filtering.

**What's needed**:
1. UMDF driver with session ID filtering
2. Filter HID devices by session
3. Keyboard/mouse isolation
4. Gamepad isolation

**Open-source solution**: Partially available
- HidHide session jail (undocumented feature)
- libvirtualhid (virtual devices, not filtering)
- Duo has proprietary UMDF driver

**Gap**: No open-source session-aware input filtering driver

**Reference**: Duo v1.5.1: "Disabled Windows' ControllerToVKMapping because it ignores the DEVPKEY_Device_SessionId attribute"

---

### Gap 6: Game Process Tracking

**Problem**: SeatManager doesn't track game PID separately.

**What's needed**:
1. Track launched game process
2. Monitor game health
3. Auto-restart crashed games
4. Kill game on teardown

**Open-source solution**: None needed (standard Windows APIs)
- Process.GetProcessById
- Job Objects for cleanup

**Gap**: MultiSeat-Extended doesn't implement this yet

**Reference**: Duo tracks processes per instance

---

### Gap 7: Provider Abstraction

**Problem**: Vibepollo is tightly coupled to SeatManager.

**What's needed**:
1. IStreamingProvider interface
2. VibepolloProvider implementation
3. ApolloProvider implementation
4. SunshineProvider implementation

**Open-source solution**: None (architecture pattern)
- Helios has Named Pipe IPC pattern
- Could use dependency injection

**Gap**: No abstraction layer exists

**Reference**: Multiple providers exist (Vibepollo, Apollo, Sunshine)

---

### Gap 8: Process Isolation on Teardown

**Problem**: Teardown is best-effort; processes may survive.

**What's needed**:
1. Job Objects for process groups
2. Kill all processes on teardown
3. Guarantee cleanup

**Open-source solution**: Windows API (Job Objects)
- CreateJobObject
- AssignProcessToJobObject
- JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

**Gap**: MultiSeat-Extended doesn't use Job Objects yet

**Reference**: Standard Windows technique

---

## Gap Priority Matrix

| Gap | Impact | Effort | Open Source? | Priority |
|-----|--------|--------|--------------|----------|
| HDR support | HIGH | HIGH | Partial | MEDIUM |
| Game process patching | HIGH | HIGH | No | LOW (complex) |
| Steam multi-instance | HIGH | MEDIUM | Partial | MEDIUM |
| Seamless display | HIGH | HIGH | No | LOW (Windows limitation) |
| UMDF input driver | MEDIUM | HIGH | Partial | LOW (complex) |
| Game process tracking | MEDIUM | LOW | Yes | HIGH |
| Provider abstraction | MEDIUM | MEDIUM | Yes | HIGH |
| Process isolation | MEDIUM | LOW | Yes | HIGH |

---

## Recommended Next Steps

### Quick Wins (Low effort, high impact)
1. **Game process tracking** — Use Process.GetProcessById
2. **Process isolation** — Use Job Objects
3. **Provider abstraction** — Create IStreamingProvider interface

### Medium Term (Medium effort, high impact)
4. **Steam multi-instance** — Research `--userdatadir` approach
5. **HDR support** — Investigate SudoVDA HDR + encoding changes

### Long Term (High effort, high impact)
6. **Game process patching** — Research Application Compatibility Toolkit
7. **UMDF input driver** — Consider libvirtualhid or custom driver
8. **Seamless display** — Fundamental Windows limitation, may not be solvable
