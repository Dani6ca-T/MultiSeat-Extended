# MultiSeat-Extended — Project State

**Purpose**: Handoff/checkpoint for CLI coding agents. Read this first.
**Last updated**: 2026-08-31

---

## Project Purpose

MultiSeat-Extended enables multiple simultaneous Moonlight game-streaming sessions on one Windows host. Each seat gets an isolated Windows account, RDP session, virtual display, per-session audio, and a dedicated streaming server instance.

**Stack**: .NET 9 Windows Service + ASP.NET Core Minimal API + React/TypeScript dashboard

---

## Architecture Direction

Target (NOT current state):

```
Control Plane
├── Seat lifecycle
├── Reconciliation
├── Policy
└── Contracts
    │
    └── Infrastructure boundaries
         ├── Session
         ├── Display
         ├── Audio
         ├── Input
         ├── Application
         └── Streaming
```

Current reality: 2-layer (`MultiSeat.Shared` + `MultiSeat.Service`) with `SeatManager` orchestrating 12 concrete dependencies + 3 interfaces + 1 interface collection.

---

## Current Git State

```
Branch:          master
HEAD:            d27ad1f5b18380d190f97ff76bebfc0d48b851d9
origin/master:   d27ad1f5b18380d190f97ff76bebfc0d48b851d9
traycer HEAD:    efa62dc (NOT merged, NOT on master)
Working tree:    Clean (only untracked ProcessTracking + docs)
```

---

## Completed Work

| Work | Commit | Status |
|------|--------|--------|
| ISessionLauncher interface extraction | `8fefd3c` | ✅ Interface created |
| ISessionLauncher wiring completion | `a6d9359` | ✅ SeatManager + SessionHealthCheck wired |
| IVirtualDisplayManager extraction | `5f03be0` | ✅ Fully wired |
| IAccountManager extraction | `0d3d02d` | ✅ Completed |
| Service Locator removal | PENDING | ✅ Implemented, awaiting commit |
| Initial repository audit | `8fefd3c` | ✅ Complete |
| Traycer branch audit | Untracked | ✅ Complete |
| Historical architecture audit | Untracked | ✅ Complete |
| SeatManager architecture audit | Untracked | ✅ Complete |
| Display detection refactoring proposal | Untracked | ✅ DO NOT IMPLEMENT |
| AccountManager boundary audit | Untracked | ✅ APPROVED + COMPLETED |
| IEmulatorConfigSeeder | Original | ✅ Existing |

**SeatManager dependency status**: 12 concrete, 3 interfaced (ISessionLauncher, IVirtualDisplayManager, IAccountManager), 1 interface collection (IEnumerable\<IEmulatorConfigSeeder\>). Public properties removed: InputRouter, InputHookManager, ApolloManager.

---

## Current Architecture Status

The project is **transitioning** from:

```
Single-process Windows Service
  + embedded ASP.NET API
  + SeatManager orchestration
  + concrete infrastructure managers
```

toward:

```
Provider-oriented architecture with clean boundaries
```

Existing interfaces (ISessionLauncher, IVirtualDisplayManager) are **dependency-inversion extractions**, not final Provider SDK boundaries. They enable mocking and loose coupling but do not represent the target architecture.

---

## Current Task

```
COMPLETED — Create and maintain persistent project-state/checkpoint documentation.
```

---

## Next Exact Step

```
Review the current repository after PROJECT-STATE creation and
select the next smallest architectural improvement based on actual
repository evidence.
```

Do NOT automatically assume the next step is another interface extraction. Evaluate whether the current interface count provides sufficient architectural value before adding more.

---

## Known Problems

| Problem | Severity | Notes |
|---------|----------|-------|
| ProcessTracking can't compile on master | Critical | Untracked, not blocking master |
| 13 concrete dependencies in SeatManager | Important | Reduced from 14 after IAccountManager extraction |
| No provider abstraction (IStreamingProvider) | Important | Documented in PROVIDER-CONTRACT.md |
| Traycer branch diverged | Important | Evaluated, not merged |
| Previous audit doc references wrong HEAD | Low | Stale but non-blocking |

---

## ProcessTracking

Pre-existing untracked ProcessTracking work exists in the working tree:

```
src/MultiSeat.Shared/IProcessGroup.cs
src/MultiSeat.Shared/IProcessGroupManager.cs
src/MultiSeat.Shared/IProcessMonitor.cs
src/MultiSeat.Shared/IProcessTracker.cs
src/MultiSeat.Shared/IProviderLifecycleConsumer.cs
src/MultiSeat.Shared/Models/ManagedProcess.cs
src/MultiSeat.Shared/Models/ManagedProcessType.cs
src/MultiSeat.Shared/Models/ProcessExitInfo.cs
src/MultiSeat.Shared/Models/ProcessIdentity.cs
src/MultiSeat.Service/Interop/SafeJobHandle.cs
src/MultiSeat.Service/ProcessTracking/ (5 files)
src/MultiSeat.Tests/ProcessTracking/ (6 files)
```

**Status**: Unfinished. Cannot compile on master (references missing Kernel32 types and `VibepolloExePath`).

**Rule**: MUST NOT be modified, moved, deleted, stashed, staged, committed, or included in unrelated tasks.

---

## Traycer Branch

```
Branch: traycer/multiseat-extended-polite-squid
HEAD:   efa62dc
Status: NOT merged
Audit:  docs/audits/MSE-Traycer-Branch-Audit.md
```

Contains historical work by the repository owner (Dani6ca-T):
- Apollo → Vibepollo backend migration (Phase 1+2)
- Vibepollo advanced features (Phase 3)
- Audio subsystem removal (Phase 5)
- RDPWrap → TermWrap installation (Phase 6)

**Rule**: Do NOT merge wholesale. Evaluate piece-by-piece if needed.

---

## Apollo vs Vibepollo

### Current master

Master uses **vibesoftwarecoder/Apollo**:

```
Repository:  vibesoftwarecoder/Apollo
Release:     v2026.6.1-multiseat.1
Format:      ZIP (apollovibe-v2026.6.1-multiseat.1-windows-x64.zip)
Install dir: C:\Program Files\ApolloVibe
Status:      URL verified valid (HTTP 200)
```

### Traycer

Traycer uses **Nonary/Vibepollo**:

```
Repository:  Nonary/Vibepollo
Release:     v1.18.4-stable.3
Format:      EXE/MSI installer
Install dir: C:\Program Files\Vibepollo
Status:      Different streaming backend
```

### Conclusion

These are **different streaming backends**, not a URL bugfix.

```
Apollo → Vibepollo migration = NOT STARTED
```

Future Vibepollo work requires a separate, deliberate migration task — not a "fix."

---

## Important Rules

1. Architecture First → Approval → Implementation
2. Small incremental changes, one focused task at a time
3. Do not introduce abstractions merely to increase interface count
4. Do not blindly merge historical branches
5. Run build/tests after implementation
6. Update documentation after meaningful changes
7. Commit and push completed work
8. Preserve pre-existing unfinished work (ProcessTracking)
9. Never modify ProcessTracking as part of unrelated tasks
10. Control Plane separated from provider/infrastructure implementations
11. Provider-specific details must not leak into Core contracts
12. Credentials/secrets never in SeatSpec or wire contracts
13. Avoid kernel patching as an architectural shortcut
14. Preserve existing working functionality while introducing boundaries

---

## Agent Checkpoint Protocol

### At START

1. Read this file (`docs/PROJECT-STATE.md`).
2. Run:
   ```bash
   git status --short
   git branch --show-current
   git log -1 --oneline
   ```
3. Verify actual state matches this document.
4. Identify `Current Task`.
5. Do not start a different task unless explicitly instructed.

### During work

Work only on `Current Task`. Do not silently switch tasks.

### Before stopping

Update the CURRENT CHECKPOINT section below with actual values:

```
Current Task:
Status:
Completed:
Remaining:
Next Exact Step:
Files Changed:
Tests:
Build:
Commit:
Push:
Blockers:
```

Describe reality, not intended work.

### If tokens/time run out

Leave the repository in a safe state. Update this file before stopping whenever possible. The next agent must continue without reconstructing the previous conversation.

---

## CURRENT CHECKPOINT

```
Last verified:     2026-08-31
Branch:            master
HEAD:              PENDING (after Service Locator removal commit)
origin/master:     0d3d02d (IAccountManager extraction)
Working tree:      Service Locator removal staged for commit
Current task:      Service Locator removal — COMPLETED, awaiting commit+push
Status:            Done
Last completed:    Service Locator removal
Next exact step:   Commit and push, then select next architectural improvement
Known blocker:     ProcessTracking still can't compile on master (pre-existing, untracked)
Test baseline:     387 passed / 17 skipped / 0 failed
Build:             ✅ 0 errors from tracked code, 9 errors from pre-existing ProcessTracking
```
