# Game Compatibility Strategy

**Date**: 2026-08-30
**Purpose**: Analyze game compatibility problems and solutions

---

## Problem Categories

### 1. RDP Detection

**Problem**: Games check for RDP/Remote Desktop session and refuse to run

**Known games**: Various DirectX 8/9 titles, some anti-cheat protected games

**Duo solution**: Application Compatibility Layer (process patching)

**Open-source solutions**:
- ❌ No known open-source solution
- Application Compatibility Toolkit (Windows built-in) — limited
- DLL injection/hooking — risky, anti-cheat conflicts

**Feasibility**: VERY HIGH difficulty, requires deep Windows internals knowledge

**Risk**: Anti-cheat detection, game ban

---

### 2. Single-Instance Mutex

**Problem**: Games use named mutexes to prevent multiple instances

**Known games**: Steam games, Epic games, most modern titles

**Duo solution**: Process patching (Application Compatibility Layer)

**Open-source solutions**:
- ❌ No known open-source solution for general mutex handling
- Steam isolation (Duol (Duo) v1.5.1: "Steam multiboxing")

**Feasibility**: HIGH difficulty, game-specific

**Risk**: Anti-cheat detection

---

### 3. Steam Restrictions

**Problem**: Steam client uses mutex, IPC, and userdata to prevent multi-instance

**Known behavior**:
- Steam client mutex prevents multiple instances
- Steam userdata locked per account
- Steam library sharing limitations

**Duo solution**: Steam isolation (v1.5.1: "Steam multiboxing", v1.5.5: "Steamworks SDK support")

**Open-source solutions**:
- ❌ No known open-source solution
- `--userdatadir` flag (undocumented, may work)

**Feasibility**: HIGH difficulty

**Risk**: Steam TOS violation, account ban

---

### 4. Session Restrictions

**Problem**: Games check session type (console vs RDP) and refuse to run

**Known behavior**: Some games check `GetSystemMetrics(SM_REMOTESESSION)`

**Duo solution**: Application Compatibility Layer

**Open-source solutions**:
- ❌ No known open-source solution
- Registry patching (risky)

**Feasibility**: MEDIUM difficulty

**Risk**: Low (if done correctly)

---

### 5. GPU Restrictions

**Problem**: Games check GPU availability or adapter type

**Known behavior**: Some games require specific GPU or refuse virtual displays

**Duo solution**: Custom WDDM driver (appears as physical GPU)

**Open-source solutions**:
- SudoVDA (appears as monitor, not GPU)
- Virtual-Display-Driver (similar)

**Feasibility**: LOW difficulty (driver already handles this)

**Risk**: Low

---

### 6. Display Restrictions

**Problem**: Games check display type, resolution, or refresh rate

**Known behavior**: Some games refuse to run below certain resolution

**MultiSeat solution**: RdpGeometry sets resolution at session creation

**Open-source solutions**:
- SudoVDA (virtual display with configurable resolution)
- Display isolation (SudoVDA primary + RDP shrunk)

**Feasibility**: LOW difficulty (already handled)

**Risk**: Low

---

### 7. Anti-Cheat

**Problem**: Anti-cheat software detects virtual environments, hooks, or patches

**Known anti-cheat**: EasyAntiCheat, BattlEye, Vanguard, nProtect

**Duo solution**: Verifier opt-out option (v1.5.6: "this WILL break things")

**Open-source solutions**:
- ❌ No known open-source solution
- ❌ Cannot patch anti-cheat without ban risk

**Feasibility**: IMPOSSIBLE without ban risk

**Risk**: VERY HIGH (account ban, game ban)

---

### 8. Game-Specific Problems

**Problem**: Individual games have unique compatibility issues

**Known issues**:
- DirectX 8/9 games (v1.5.5: "Added support for DirectX 8 & 9 applications")
- Games with external launchers (Epic, Ubisoft Connect)
- Games with mandatory online services
- Games with hardware checks

**Duo solution**: Game-specific patches in Application Compatibility Layer

**Open-source solutions**:
- ❌ No comprehensive solution
- Game-specific patches possible but labor-intensive

**Feasibility**: MEDIUM per game, HIGH for comprehensive coverage

**Risk**: Varies per game

---

## Current MultiSeat-Extended State

| Problem | Current Solution | Status |
|---------|-----------------|--------|
| RDP detection | None | ❌ MISSING |
| Single-instance mutex | None | ❌ MISSING |
| Steam restrictions | None | ❌ MISSING |
| Session restrictions | None | ❌ MISSING |
| GPU restrictions | SudoVDA | ✅ HANDLED |
| Display restrictions | RdpGeometry + display isolation | ✅ HANDLED |
| Anti-cheat | None | ❌ CANNOT SOLVE |
| Game-specific | None | ❌ MISSING |

---

## Recommendation

### Priority 1: Document Known Issues

Create a list of games that work/don't work with MultiSeat-Extended.

### Priority 2: Investigate Application Compatibility Toolkit

Windows built-in tool for compatibility shims. May handle some RDP detection cases.

### Priority 3: Research Process Patching Concepts

Understand how Duo's Application Compatibility Layer works (conceptually, not code).

### Priority 4: Accept Limitations

Some games will never work in RDP sessions without proprietary patching. Document these.

### DO NOT BUILD

- Custom Anti-Cheat bypass (ban risk)
- Game-specific patches for all games
- DLL injection framework

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Duo has Application Compatibility Layer | Release notes v1.5.5 | VERIFIED (public) |
| Duo supports DirectX 8/9 | Release notes v1.5.5 | VERIFIED (public) |
| Duo has Steam multiboxing | Release notes v1.5.1 | VERIFIED (public) |
| Duo has anti-cheat opt-out | Release notes v1.5.6 | VERIFIED (public) |
| No open-source RDP detection bypass | Research | VERIFIED (absent) |
| No open-source Steam isolation | Research | VERIFIED (absent) |
| SudoVDA handles GPU/display | MultiSeat implementation | VERIFIED |
