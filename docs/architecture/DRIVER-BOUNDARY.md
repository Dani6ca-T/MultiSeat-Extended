# Driver Boundary

**Date**: 2026-08-30
| **Status**: FROZEN

---

## Purpose

Define driver dependencies, boundaries, and replacement options.

---

## Driver Dependencies

### 1. SudoVDA (Virtual Display)

| Aspect | Details |
|--------|---------|
| Purpose | Virtual display per seat |
| Type | IddCx kernel-mode driver |
| License | Unknown |
| Required | Yes |
| Maintenance | Active (used by Apollo, Vibepollo) |
| Replacement | Virtual-Display-Driver, parsec-vdd |
| Risk | Unknown license |

**DECISION**: Keep SudoVDA. Investigate license.

### 2. HidHide (Input Isolation)

| Aspect | Details |
|--------|---------|
| Purpose | Gamepad session jail |
| Type | Kernel filter driver |
| License | MIT |
| Required | No (optional, default OFF) |
| Maintenance | Active (ViGEm) |
| Replacement | libvirtualhid (custom license) |
| Risk | Undocumented feature |

**DECISION**: Keep HidHide. Enable by default (target).

### 3. TermWrap (RDP Patching)

| Aspect | Details |
|--------|---------|
| Purpose | Concurrent RDP sessions |
| Type | DLL proxy (user-mode) |
| License | MIT |
| Required | Yes |
| Maintenance | Active (v0.6) |
| Replacement | None viable |
| Risk | Low |

**DECISION**: Keep TermWrap. No replacement needed.

---

## Driver Boundary Rules

### Rule 1: Core Never Touches Drivers

Core/Domain has no driver dependencies.

### Rule 2: Infrastructure Adapts Drivers

Infrastructure layer provides adapter interfaces.

### Rule 3: Drivers Are External

Drivers are installed separately, not bundled.

---

## Replacement Options

### Display Driver

| Option | License | Status |
|--------|---------|--------|
| SudoVDA | Unknown | Current |
| Virtual-Display-Driver | Unknown | Alternative |
| parsec-vdd | Unknown | Alternative |

### Input Driver

| Option | License | Status |
|--------|---------|--------|
| HidHide | MIT | Current |
| libvirtualhid | Custom | Alternative |
| Duo UMDF | Proprietary | Not available |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SudoVDA is IddCx-based | Driver architecture | FACT |
| HidHide is MIT | LICENSE file | FACT |
| TermWrap is MIT | LICENSE file | FACT |
| SudoVDA license unknown | LICENSE search | FACT (absent) |
