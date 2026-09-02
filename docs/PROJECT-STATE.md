# MultiSeat-Extended — Project State

**Purpose**: Handoff/checkpoint for CLI coding agents. Read this first.
**Last updated**: 2026-09-02 (checkpoint commit)

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
HEAD:            35405d9
origin/master:   093c013 (behind by 6 commits)
Working tree:    Clean (only .freebuff/ and TestResults/ remain untracked)
```

---

## Completed Work

| Work | Commit | Status |
|------|--------|--------|
| ISessionLauncher interface extraction | `8fefd3c` | ✅ Interface created |
| ISessionLauncher wiring completion | `a6d9359` | ✅ SeatManager + SessionHealthCheck wired |
| IVirtualDisplayManager extraction | `5f03be0` | ✅ Fully wired |
| IAccountManager extraction | `0d3d02d` | ✅ Completed |
| Service Locator removal | `d4486c0` | ✅ Completed |
| Initial repository audit | `8fefd3c` | ✅ Complete |
| Traycer branch audit | `8cf77ff` | ✅ Complete |
| Historical architecture audit | `8cf77ff` | ✅ Complete |
| SeatManager architecture audit | `8cf77ff` | ✅ Complete |
| Display detection refactoring proposal | `8cf77ff` | ✅ DO NOT IMPLEMENT |
| AccountManager boundary audit | `8cf77ff` | ✅ APPROVED + COMPLETED |
| Apollo provider boundary | `cdee422` | ✅ DONE |
| Apollo DI smoke tests | `70369c0` | ✅ DONE |
| RdpWrapper → TermWrap migration | `06796f8` | ✅ DONE |
| ProcessTracking implementation | `6e73604` | ✅ DONE (not integrated) |
| Architecture documentation | `0aceacf` | ✅ DONE |
| Audit documentation | `8cf77ff` | ✅ DONE |
| Research documentation | `35405d9` | ✅ DONE |
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
| ProcessTracking not integrated | Important | Committed but not referenced by production code |
| 13 concrete dependencies in SeatManager | Important | Reduced from 14 after IAccountManager extraction |
| No provider abstraction (IStreamingProvider) | Important | Documented in PROVIDER-CONTRACT.md |
| Traycer branch diverged | Important | Evaluated, not merged |
| Apollo → Vibepollo migration not started | Important | Separate deliberate task needed |

---

## ProcessTracking

Committed in `6e73604`. Interfaces + implementations + tests exist but are NOT integrated:

```
src/MultiSeat.Shared/IProcessGroup.cs          ✅ committed
src/MultiSeat.Shared/IProcessGroupManager.cs   ✅ committed
src/MultiSeat.Shared/IProcessMonitor.cs        ✅ committed
src/MultiSeat.Shared/IProcessTracker.cs        ✅ committed
src/MultiSeat.Shared/IProviderLifecycleConsumer.cs  ✅ committed
src/MultiSeat.Shared/Models/ManagedProcess.cs  ✅ committed
src/MultiSeat.Shared/Models/ManagedProcessType.cs  ✅ committed
src/MultiSeat.Shared/Models/ProcessExitInfo.cs  ✅ committed
src/MultiSeat.Shared/Models/ProcessIdentity.cs ✅ committed
src/MultiSeat.Service/Interop/SafeJobHandle.cs ✅ committed
src/MultiSeat.Service/ProcessTracking/ (5 files)  ✅ committed
src/MultiSeat.Tests/ProcessTracking/ (6 files)   ✅ committed
```

**Status**: Committed but NOT referenced by SeatManager, ApolloManager, or Program.cs. Tests pass (included in 538 count).

**Rule**: Integration into production code requires a separate deliberate task.

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
- RDPWrap → TermWrap installation (Phase 6) — **applied to master 2026-09-02**

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
Last verified:     2026-09-02
Branch:            master
HEAD:              35405d9
origin/master:     093c013 (6 commits behind)
Working tree:      Clean
Current task:      Checkpoint commit — DONE
Status:            Stable checkpoint
Last completed:    6 commits (TermWrap, ProcessTracking, docs, audits, research)
Next exact step:   Push to origin, then select next architectural improvement
Known blocker:     None — build clean, tests pass
Test baseline:     538 passed / 17 skipped / 0 failed
Build:             ✅ 0 errors, 0 warnings
```
