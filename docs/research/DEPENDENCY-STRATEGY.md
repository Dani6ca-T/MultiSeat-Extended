# Dependency Strategy

**Date**: 2026-08-30
**Purpose**: Analyze external dependencies and replacement options

---

## Dependencies

### 1. TermWrap

| Aspect | Details |
|--------|---------|
| Purpose | Concurrent RDP sessions (termsrv.dll patching) |
| License | MIT |
| Maintenance | Active (v0.6, 2025-05-26) |
| Risk | Low (proven, MIT) |
| Can replace? | No (unique capability) |
| Alternatives | rdpwrap (GPL-2.0, abandoned) |
| Current use | ✅ Integrated |

**Recommendation**: KEEP. No viable alternative.

### 2. SudoVDA

| Aspect | Details |
|--------|---------|
| Purpose | Virtual display driver (IddCx) |
| License | Unknown (not explicitly stated) |
| Maintenance | Active (used by Apollo, Vibepollo) |
| Risk | Medium (unknown license) |
| Can replace? | Yes (Virtual-Display-Driver, parsec-vdd) |
| Alternatives | Virtual-Display-Driver, parsec-vdd |
| Current use | ✅ Integrated |

**Recommendation**: KEEP, but investigate license. Alternatives exist but are equivalent.

### 3. HidHide

| Aspect | Details |
|--------|---------|
| Purpose | Gamepad isolation (session jail) |
| License | MIT |
| Maintenance | Active (ViGEm/HidHide) |
| Risk | Medium (undocumented feature) |
| Can replace? | Yes (libvirtualhid, custom UMDF) |
| Alternatives | libvirtualhid (custom license), Duo UMDF (proprietary) |
| Current use | ✅ Integrated (default OFF) |

**Recommendation**: KEEP for now. Investigate libvirtualhid long-term.

### 4. Vibepollo

| Aspect | Details |
|--------|---------|
| Purpose | Streaming server (capture, encode, stream) |
| License | GPLv3 |
| Maintenance | Very active (multiple releases/week) |
| Risk | Medium (GPLv3, AI-generated code) |
| Can replace? | Yes (Apollo, Sunshine) |
| Alternatives | Apollo (GPLv3), Sunshine (GPLv3) |
| Current use | ✅ External process |

**Recommendation**: KEEP, but abstract via IStreamingProvider for provider flexibility.

### 5. ViGEmBus

| Aspect | Details |
|--------|---------|
| Purpose | Virtual gamepad bus driver |
| License | MIT |
| Maintenance | Legacy (replaced by libvirtualhid) |
| Risk | Low (deprecated) |
| Can replace? | Yes (libvirtualhid, Vibepollo native) |
| Alternatives | libvirtualhid (custom license), Vibepollo Virtual Gamepad |
| Current use | ⚠️ Optional (EnableViGEmController) |

**Recommendation**: DEPRECATE. Vibepollo handles gamepad natively now.

### 6. mstsc

| Aspect | Details |
|--------|---------|
| Purpose | RDP loopback session creation |
| License | Windows built-in |
| Maintenance | Microsoft |
| Risk | Low (Windows component) |
| Can replace? | No (Windows built-in) |
| Alternatives | None |
| Current use | ✅ SessionLauncher |

**Recommendation**: KEEP. Windows built-in, no alternative.

### 7. Windows Firewall API

| Aspect | Details |
|--------|---------|
| Purpose | Port management per seat |
| License | Windows built-in |
| Maintenance | Microsoft |
| Risk | Low (Windows component) |
| Can replace? | No (Windows built-in) |
| Alternatives | Netsh (command-line) |
| Current use | ✅ FirewallManager |

**Recommendation**: KEEP. Windows built-in.

### 8. DPAPI

| Aspect | Details |
|--------|---------|
| Purpose | Credential encryption |
| License | Windows built-in |
| Maintenance | Microsoft |
| Risk | Low (Windows component) |
| Can replace? | No (Windows built-in) |
| Alternatives | None |
| Current use | ✅ Security implementations |

**Recommendation**: KEEP. Windows built-in.

### 9. ASP.NET Core

| Aspect | Details |
|--------|---------|
| Purpose | REST API framework |
| License | MIT |
| Maintenance | Microsoft (very active) |
| Risk | Low (Microsoft-backed) |
| Can replace? | Yes (minimal APIs, Fastify) |
| Alternatives | FastEndpoints, Carter |
| Current use | ✅ ApiServer |

**Recommendation**: KEEP. Industry standard.

### 10. React

| Aspect | Details |
|--------|---------|
| Purpose | Web UI framework |
| License | MIT |
| Maintenance | Meta (very active) |
| Risk | Low (industry standard) |
| Can replace? | Yes (Vue, Svelte, Blazor) |
| Alternatives | Vue, Svelte, Blazor |
| Current use | ✅ Dashboard |

**Recommendation**: KEEP. Industry standard.

---

## Dependency Risk Summary

| Dependency | License Risk | Maintenance Risk | Replacement Difficulty |
|------------|-------------|-----------------|----------------------|
| TermWrap | Low (MIT) | Low (active) | Impossible |
| SudoVDA | Medium (unknown) | Low (active) | Medium |
| HidHide | Low (MIT) | Low (active) | High |
| Vibepollo | Medium (GPLv3) | Low (very active) | Medium |
| ViGEmBus | Low (MIT) | High (legacy) | Low |
| mstsc | None (Windows) | None (Microsoft) | Impossible |
| Firewall API | None (Windows) | None (Microsoft) | Impossible |
| DPAPI | None (Windows) | None (Microsoft) | Impossible |
| ASP.NET Core | Low (MIT) | Low (Microsoft) | Medium |
| React | Low (MIT) | Low (Meta) | Medium |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| TermWrap is MIT | LICENSE file | VERIFIED |
| SudoVDA license unknown | LICENSE search | VERIFIED (absent) |
| HidHide is MIT | LICENSE file | VERIFIED |
| Vibepollo is GPLv3 | LICENSE file | VERIFIED |
| ViGEmBus is legacy | LizardByte announcement | VERIFIED |
| Vibepollo handles gamepad natively | Vibepollo v1.19.0 release | VERIFIED |
