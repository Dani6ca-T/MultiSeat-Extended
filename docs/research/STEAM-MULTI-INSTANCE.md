# Steam Multi-Instance Strategy

**Date**: 2026-08-30
**Purpose**: Analyze Steam multi-instance feasibility and approach

---

## Problem

Steam client prevents multiple instances through:
1. **Named mutex** — `SteamClient` mutex prevents multiple Steam.exe
2. **IPC** — Steam uses named pipes for inter-process communication
3. **Userdata** — Per-account userdata directory locked
4. **Library** — Shared library with folderlock Steamservice

---

## Duo's Approach (Public Evidence)

| Version | Feature |
|---------|---------|
| v1.5.1 | "Added Steam multiboxing support" |
| v1.5.5 | "Fixed Steam isolation" |
| v1.5.5 | "Added Steamworks SDK support to Steam isolation" |
| v1.5.7 | "Fixed several Steam isolation issues" |

**Inference**: Duo patches Steam processes to bypass mutex and IPC restrictions.

**Cannot verify**: Exact mechanism (proprietary).

---

## Open-Source Approaches

### 1. Separate Userdata Directories

**Mechanism**: Each Steam instance uses `--userdatadir <path>`

**Feasibility**: MAY work for Steam client isolation

**Limitations**:
- Steam may still use global mutex
- Library sharing limitations
- Cloud sync conflicts

**Risk**: LOW (no patching needed)

### 2. Separate Steam Installations

**Mechanism**: Each seat has own Steam installation directory

**Feasibility**: POSSIBLE but wasteful

**Limitations**:
- Disk space (multiple Steam installs)
- Update management
- Library duplication

**Risk**: LOW

### 3. Process Patching

**Mechanism**: Patch Steam.exe to bypass mutex

**Feasibility**: POSSIBLE but risky

**Limitations**:
- Anti-cheat detection
- Steam TOS violation
- Game ban risk

**Risk**: VERY HIGH

### 4. SteamCMD / Steam headless

**Mechanism**: Use SteamCMD for game management, separate client for streaming

**Feasibility**: PARTIAL

**Limitations**:
- No GUI
- Manual game management
- No library sharing

**Risk**: LOW

---

## Current MultiSeat-Extended State

| Capability | Status | Evidence |
|------------|--------|----------|
| Steam library sharing | ✅ SharedGameLibrary (icacls) | SeatManager |
| Steam multi-instance | ❌ NOT IMPLEMENTED | Codebase search |
| Steam process isolation | ❌ NOT IMPLEMENTED | Codebase search |
| Steam userdata isolation | ❌ NOT IMPLEMENTED | Codebase search |

---

## Recommendation

### Short-term: Document Limitation

Steam multi-instance is NOT supported. Document which games require Steam and which work without it.

### Medium-term: Investigate --userdatadir

Test if `steam.exe --userdatadir <per-seat-path>` enables multiple Steam instances.

### Long-term: Research Process Patching Concepts

Understand how Duo's Steam isolation works (conceptually). Consider if Open-Source solution is feasible.

### DO NOT BUILD

- Custom Steam patching (TOS violation, ban risk)
- Steam emulation layer
- Steam IPC interception

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Duo has Steam multiboxing | Release notes v1.5.1 | VERIFIED (public) |
| Duo has Steamworks SDK support | Release notes v1.5.5 | VERIFIED (public) |
| Steam uses named mutex | Steam client analysis | VERIFIED (INFERENCE) |
| MultiSeat has no Steam isolation | Codebase search | VERIFIED (absent) |
| SharedGameLibrary exists | SeatManager.cs | VERIFIED |
