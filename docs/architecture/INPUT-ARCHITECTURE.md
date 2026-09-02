# Input Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define input management architecture, isolation model, and backend options.

---

## Input Backend Abstraction

### IInputBackend (Conceptual)

| Operation | Description |
|-----------|-------------|
| CloakDevice | Hide device from other sessions |
| UncloakDevice | Show device to all sessions |
| CreateVirtualDevice | Create virtual controller |
| DestroyVirtualDevice | Remove virtual controller |
| AssignDevice | Assign device to seat |

**DECISION**: Input backend is abstracted via IInputBackend.

---

## Current Architecture

### Gamepad Forwarding

```
Moonlight Client
    │
    ├── Controller input packets
    │
    └── Vibepollo
            ├── Receives packets
            └── Injects into seat session
```

**FACT**: Vibepollo handles gamepad forwarding natively.

### Gamepad Isolation

```
HidHide Driver
    │
    ├── Blacklist entry: USB\VID_xxxx&PID_yyyy!12345
    │   (12345 = session ID)
    │
    └── Device visible only inside that session
```

**FACT**: HidHide session jail uses undocumented `!<sessionId>` suffix.

---

## Input Components

### 1. HidHide (Gamepad Isolation)

| Aspect | Details |
|--------|---------|
| Purpose | Gamepad isolation per seat |
| Mechanism | Session ID filtering |
| License | MIT |
| Status | Integrated (default OFF) |
| Risk | Undocumented feature |

**FACT**: HidHideConfigurator manages session jail rules.

### 2. ViGEm Controller (Legacy)

| Aspect | Details |
|--------|---------|
| Purpose | Virtual Xbox 360 controller |
| Mechanism | Kernel-mode bus driver |
| License | MIT |
| Status | Optional (EnableViGEmController) |
| Risk | Legacy, Vibepollo handles natively |

**DECISION**: ViGEm controller is deprecated.

### 3. InputHookManager (No-Op)

| Aspect | Details |
|--------|---------|
| Purpose | Keyboard/Mouse isolation |
| Mechanism | WH_KEYBOARD_LL/WH_MOUSE_LL hooks |
| Status | No-op (runs from Session 0) |
| Risk | Hooks ineffective from Session 0 |

**FACT**: InputHookManager is no-op (CLAUDE.md "Known Constraints").

---

## Input Isolation Model

### Gamepad

```
Physical Gamepad
    │
    ├── HidHide session jail
    │   └── Visible only in assigned session
    │
    └── Vibepollo
        └── Forwards from Moonlight client
```

### Keyboard/Mouse

```
Physical K/M
    │
    └── Console session (no isolation)

Moonlight K/M
    │
    └── Vibepollo
        └── SendInput into seat session
```

**DECISION**: No K/M isolation needed (physical → console, Moonlight → seat).

---

## Short-Term Architecture

### Keep

| Component | Reason |
|-----------|--------|
| Vibepollo gamepad forwarding | Works natively |
| HidHide session jail | MIT, works for gamepad |
| Deprecate ViGEm controller | Vibepollo handles natively |

### Add

| Component | Priority |
|-----------|----------|
| Seat-to-device mapping | P3 |
| Device assignment UI | P3 |

---

## Long-Term Architecture

### Investigate

| Component | Reason |
|-----------|--------|
| libvirtualhid | Modern ViGEmBus replacement |
| VHF session affinity | Microsoft's virtual HID framework |
| UMDF input driver | Duo's approach (proprietary) |

### Blockers

| Component | Blocker |
|-----------|---------|
| libvirtualhid | Custom license requires agreement |
| UMDF driver | Driver development expertise |
| Duo UMDF | Proprietary, cannot reference |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Vibepollo handles gamepad natively | VibepolloManager | FACT |
| HidHide session jail works | HidHideConfigurator | FACT |
| InputHookManager is no-op | CLAUDE.md | FACT |
| ViGEm is legacy | LizardByte announcement | FACT |
| libvirtualhid requires license | GitHub README | FACT |
