# Input Strategy

**Date**: 2026-08-30
**Purpose**: Compare input isolation options and provide recommendation

---

## Current State

### What Works

| Component | Status | Evidence |
|-----------|--------|----------|
| Gamepad forwarding | ✅ COMPLETE | Vibepollo handles Moonlight client gamepad natively |
| Gamepad isolation | ⚠️ PARTIAL | HidHide session jail (undocumented, default OFF) |
| ViGEm controller | ⚠️ PARTIAL | Optional, legacy path (EnableViGEmController) |

### What Doesn't Work

| Component | Status | Evidence |
|-----------|--------|----------|
| Keyboard/Mouse isolation | ❌ NO-OP | InputHookManager runs from Session 0, hooks ineffective |
| Seat-to-device mapping | ❌ MISSING | No device assignment per seat |
| DualSense Edge support | ❌ MISSING | No custom UMDF driver |

---

## Options Compared

### 1. HidHide Session Jail (Current)

| Aspect | Details |
|--------|---------|
| Mechanism | Undocumented `!<sessionId>` suffix in blacklist |
| Kernel mode | Yes (kernel filter driver) |
| Session aware | Yes (filters at open time) |
| Device types | HID (gamepads, keyboards, mice) |
| License | MIT |
| Maintenance | Active (ViGEm/HidHide) |
| Risk | **Undocumented feature** — may break in future HidHide updates |
| Current use | ✅ Integrated (default OFF) |

**How it works**:
- Write blacklist entry: `USB\VID_xxxx&PID_yyyy!12345` (12345 = session ID)
- Device visible only inside that session
- Filtered at `IRP_MJ_CREATE` time
- Must be written BEFORE device is opened (pre-write rule)

**Known issues**:
- Default OFF because wrong pad can be confined
- Rules written after device open are too late
- HidHideInspector verifies jail from Session 0

### 2. ViGEmBus (Legacy)

| Aspect | Details |
|--------|---------|
| Mechanism | Kernel-mode bus driver, virtual Xbox 360 controllers |
| Session aware | No (global) |
| Device types | Xbox 360, DualShock 4 |
| License | MIT |
| Maintenance | Legacy (replaced by libvirtualhid) |
| Risk | Deprecated, being replaced |
| Current use | ⚠️ Optional (EnableViGEmController) |

### 3. libvirtualhid (Modern)

| Aspect | Details |
|--------|---------|
| Mechanism | UMDF2 + VHF (Virtual HID Framework) |
| Session aware | VHF supports session affinity |
| Device types | Xbox, DualSense, DualShock 4, Switch Pro |
| License | Custom (license required) |
| Maintenance | Active (LizardByte) |
| Risk | License restrictions, requires agreement |
| Current use | Not integrated |

### 4. void-drivers

| Aspect | Details |
|--------|---------|
| Mechanism | UMDF virtual HID |
| Session aware | Unknown |
| Device types | Various HID devices |
| License | Unknown |
| Maintenance | Unknown |
| Risk | Unknown license |
| Current use | Not integrated |

### 5. Vibepollo Virtual Gamepad (v1.19.0+)

| Aspect | Details |
|--------|---------|
| Mechanism | Own driver, no ViGEmBus dependency |
| Session aware | Via Moonlight protocol (per-client) |
| Device types | Xbox Series/One, DualSense, DualShock 4, Switch Pro |
| License | GPLv3 (Vibepollo) |
| Maintenance | Active |
| Risk | Tied to Vibepollo |
| Current use | ✅ Used by Vibepollo natively |

### 6. Duo UMDF Input Driver

| Aspect | Details |
|--------|---------|
| Mechanism | Custom UMDF driver with DEVPKEY_Device_SessionId |
| Session aware | Yes (session ID filtering) |
| Device types | HID devices, gamepads |
| License | Proprietary |
| Maintenance | Tied to Duo |
| Risk | Cannot inspect or modify |
| Current use | Duo uses this |

### 7. VHF (Virtual HID Framework)

| Aspect | Details |
|--------|---------|
| Mechanism | Microsoft's UMDF2 framework for virtual HID |
| Session aware | Yes (via session affinity) |
| Device types | Any HID device |
| License | Windows SDK |
| Maintenance | Microsoft |
| Risk | Requires driver development |
| Current use | Used by libvirtualhid |

---

## Recommendation

### Short-term: KEEP HidHide Session Jail

**Reasons**:
1. Already integrated
2. MIT license
3. Works for gamepad isolation
4. Pre-write rules handle timing

**Improvements needed**:
1. Make it default ON (currently OFF due to wrong-pad risk)
2. Add device identity tracking (SeatPadDevicePaths)
3. Improve rule management

### Short-term: KEEP Vibepollo Native Gamepad

**Reasons**:
1. Vibepollo handles Moonlight client input natively
2. No duplicate controllers
3. Automatic profile selection per client

### Long-term: INVESTIGATE libvirtualhid

**Reasons**:
1. Modern replacement for ViGEmBus
2. VHF session affinity support
3. Multiple device types
4. Active maintenance by LizardByte

**Blockers**:
1. Custom license requires agreement
2. UMDF2 driver development expertise needed
3. Must verify VHF session affinity works for multiseat

### Long-term: DO NOT BUILD Custom UMDF Input Driver

**Reasons**:
1. Duo's UMDF driver is proprietary — cannot reference
2. Driver development is expensive and risky
3. HidHide session jail works for gamepad isolation
4. Vibepollo handles input forwarding natively

### Keyboard/Mouse Isolation: RESEARCH NEEDED

**Current state**: InputHookManager is no-op (runs from Session 0)

**Options**:
1. Move hooks into seat session (requires session-scoped injection)
2. Use HidHide for K/M HID devices (if supported)
3. Use UMDF driver for K/M session filtering
4. Accept no K/M isolation (physical K/M → console, Moonlight K/M → seat)

**Evidence**: Physical input goes to console session; Moonlight input is SendInput'd inside seat session. There is no cross-session K/M bleed to prevent.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| HidHide session jail is undocumented | HidHide source analysis | VERIFIED |
| InputHookManager is no-op | CLAUDE.md "Known Constraints" | VERIFIED |
| Vibepollo handles gamepad natively | VibepolloManager.cs | VERIFIED |
| ViGEmBus is legacy | LizardByte announcement | VERIFIED |
| libvirtualhid requires license | README | VERIFIED |
| Duo has proprietary UMDF driver | README | VERIFIED (public) |
| No K/M cross-session bleed | Architecture analysis | VERIFIED (INFERENCE) |
