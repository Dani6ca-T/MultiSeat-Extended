# Dependency Boundary

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define external dependencies, boundaries, and replacement options.

---

## External Dependencies

### 1. Vibepollo

| Aspect | Details |
|--------|---------|
| Purpose | Streaming server |
| License | GPLv3 |
| Boundary | External process |
| Adapter | VibepolloManager |
| Failure | Auto-restart |
| Upgrade | Manual (new version) |
| Replacement | Apollo, Sunshine |

**DECISION**: Provider is external process behind adapter.

### 2. TermWrap

| Aspect | Details |
|--------|---------|
| Purpose | RDP patching |
| License | MIT |
| Boundary | DLL proxy |
| Adapter | SessionLauncher |
| Failure | RDP fails |
| Upgrade | Manual (new version) |
| Replacement | None viable |

**DECISION**: TermWrap is required dependency.

### 3. SudoVDA

| Aspect | Details |
|--------|---------|
| Purpose | Virtual display |
| License | Unknown |
| Boundary | Kernel driver |
| Adapter | VirtualDisplayManager |
| Failure | Display creation fails |
| Upgrade | Manual (new driver) |
| Replacement | Virtual-Display-Driver |

**DECISION**: SudoVDA is required dependency.

### 4. HidHide

| Aspect | Details |
|--------|---------|
| Purpose | Gamepad isolation |
| License | MIT |
| Boundary | Kernel driver |
| Adapter | HidHideConfigurator |
| Failure | Gamepad isolation fails |
| Upgrade | Manual (new version) |
| Replacement | libvirtualhid |

**DECISION**: HidHide is optional dependency.

### 5. Windows

| Aspect | Details |
|--------|---------|
| Purpose | OS platform |
| License | Proprietary |
| Boundary | Platform APIs |
| Adapter | Interop layer |
| Failure | OS-level failures |
| Upgrade | Windows Update |
| Replacement | None |

**DECISION**: Windows is required platform.

---

## Dependency Rules

### Rule 1: Core Has No External Dependencies

Core/Domain depends on nothing external.

### Rule 2: Infrastructure Adapts Dependencies

Infrastructure provides adapter interfaces.

### Rule 3: Dependencies Are Swappable

Dependencies can be replaced via adapters.

---

## License Compatibility

| Dependency | License | Can Embed in MIT? |
|------------|---------|-------------------|
| Vibepollo | GPLv3 | No (external process) |
| TermWrap | MIT | Yes |
| SudoVDA | Unknown | Investigate |
| HidHide | MIT | Yes |
| Windows | Proprietary | N/A (platform) |

**DECISION**: GPLv3 components are external processes.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Vibepollo is GPLv3 | LICENSE file | FACT |
| TermWrap is MIT | LICENSE file | FACT |
| HidHide is MIT | LICENSE file | FACT |
| SudoVDA license unknown | LICENSE search | FACT (absent) |
| GPLv3 cannot embed in MIT | License analysis | FACT |
