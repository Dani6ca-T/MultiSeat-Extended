# Input Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate input/HID architecture against actual source code.

---

## 1. Current State

### HidHide

**Evidence**: HidHideConfigurator.cs
```csharp
public void CloakForSession(SeatInfo seat)
{
    // HidHide session jail implementation
}
```

**Analysis**: HidHide session jail implemented.

**VERDICT**: Implemented.

---

### ViGEm

**Evidence**: SeatManager.cs
```csharp
if (_options.EnableViGEmController)
{
    seat.ViGEmControllerIndex = _controllerManager.CreateController(seat);
}
```

**Analysis**: ViGEm controller optional (default OFF).

**VERDICT**: Implemented (optional).

---

### InputHookManager

**Evidence**: SeatManager.cs
```csharp
_inputHookManager.InstallForSession((uint)seat.SessionId);
```

**Analysis**: InputHookManager installed but is no-op (runs from Session 0).

**VERDICT**: Implemented (no-op).

---

## 2. Target State

| Component | Current | Target |
|-----------|---------|--------|
| HidHide | Implemented (default OFF) | Enable by default |
| ViGEm | Optional (default OFF) | Deprecate |
| InputHookManager | No-op | Re-architect or remove |
| Keyboard/Mouse isolation | Not needed | Not needed |
| Gamepad forwarding | Vibepollo native | Keep |

---

## 3. Missing

| Component | Status |
|-----------|--------|
| Seat-to-device mapping | Missing |
| Device assignment UI | Missing |
| UMDF input driver | Missing (experimental) |

---

## 4. Unsafe

| Component | Risk |
|-----------|------|
| HidHide session jail | Undocumented feature |
| InputHookManager | No-op (Session 0) |

---

## Summary

| Aspect | Status | Evidence |
|--------|--------|----------|
| HidHide session jail | Implemented | HidHideConfigurator.cs |
| ViGEm controller | Optional | SeatManager.cs |
| InputHookManager | No-op | CLAUDE.md |
| K/M isolation | Not needed | Architecture analysis |
| Gamepad forwarding | Vibepollo native | VibepolloManager.cs |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| HidHide session jail works | HidHideConfigurator.cs | FACT |
| ViGEm is optional | MultiSeatOptions.cs | FACT |
| InputHookManager is no-op | CLAUDE.md | FACT |
| Vibepollo handles gamepad | VibepolloManager | FACT |
