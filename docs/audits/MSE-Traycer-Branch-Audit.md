# MultiSeat-Extended — Traycer Branch Audit

**Date**: 2026-08-31
**Auditor**: Buffy (Codebuff)
**Branch analyzed**: `traycer/multiseat-extended-polite-squid`

---

## 1. Branch Topology

```
master HEAD:     d27ad1f
traycer HEAD:    efa62dc
Common ancestor: 067a2d0 (fix(input): the jail probe reads which failure it was)
```

| Metric | Value |
|--------|-------|
| Commits unique to master | 23 |
| Commits unique to traycer | 5 |
| Total divergence | 28 commits |
| Files changed (diff) | 114 files, +3403/−8007 |

Traycer is **smaller** than master because Phase 5 deleted the entire audio subsystem and many test files that master subsequently added.

---

## 2. Traycer Commit History

### ca3da32 — Phase 1+2: Apollo→Vibepollo refactor + PerSession audio

| Aspect | Detail |
|--------|--------|
| **Date** | 2026-08-28 12:07 |
| **Author** | Dani6ca-T |
| **Files** | 71 files, +971/−899 |
| **Purpose** | Rename Apollo→Vibepollo across entire codebase + fix download URL + simplify audio |
| **Architectural impact** | BREAKING RENAME: 7 class renames, config key changes, install path changes |

Key changes:
- `ApolloManager` → `VibepolloManager`
- `ApolloConfigBuilder` → `VibepolloConfigBuilder`
- `ApolloLogParser` → `VibepolloLogParser`
- `HostApolloMonitor` → `HostVibepolloMonitor`
- `ApolloServerQuery` → `VibepolloServerQuery`
- `HostApolloInfo` → `HostVibepolloInfo`
- Config keys: `ApolloExePath` → `VibepolloExePath`
- Install path: `C:\Program Files\ApolloVibe` → `C:\Program Files\Vibepollo`
- Download URL fixed: `vibesoftwarecoder/Vibepollo` (404) → `Nonary/Vibepollo` (936⭐, signed MSI)
- PerSession audio made default (no drivers needed)
- VirtualDrivers Virtual Audio Driver added as MIT alternative for SharedHost

### bc6a1ca — Phase 3: Vibepollo advanced features

| Aspect | Detail |
|--------|--------|
| **Date** | 2026-08-28 13:12 |
| **Author** | Dani6ca-T |
| **Files** | 20 files, +1913/−9 |
| **Purpose** | Add per-seat Vibepollo feature toggles + per-app profiles + roadmap |
| **Architectural impact** | New models (SeatAppProfile), new services (SeatAppManager), new API endpoints, dashboard UI |

Key changes:
- Phase 3a: Per-seat feature toggles (Playnite, RTSS, Lossless Scaling, HDR)
- Phase 3b: Per-app Vibepollo profiles (SeatAppManager, CRUD API, dashboard UI)
- Phase 4: Roadmap in SPEC.md (Q3/Q4 2026 plan)
- 11 new nullable fields on SeatInfo for Vibepollo features
- 9 new fields on MultiSeatOptions for global defaults
- 4 new API endpoints: GET/POST/PUT/DELETE `/api/seats/{id}/apps`
- 2 new dashboard components: SeatAppsPanel, VibepolloFeaturesPanel

### 086bea1 — Phase 5: Remove VB-CABLE/VoiceMeeter legacy

| Aspect | Detail |
|--------|--------|
| **Date** | 2026-08-28 14:45 |
| **Author** | Dani6ca-T |
| **Files** | 25 files, +247/−2620 |
| **Purpose** | Delete entire SharedHost audio subsystem (PerSession only) |
| **Architectural impact** | DESTRUCTIVE: 7 source files deleted, 6 test files deleted, 26 audio tests dropped |

Key changes:
- Deleted: AudioRouter, AudioDeviceEnumerator, AudioCaptureHelper, AudioPolicyClient, AudioPeakHelper, VoiceMeeterConfigurator
- Deleted: AudioTests (26 tests)
- AudioMode enum deleted
- VacCableCount option deleted
- SeatInfo audio properties deleted
- SeatManager.ResetAudio → no-op stub
- Install script: removed VB-CABLE/VoiceMeeter/VirtualDrivers Audio sections
- Tests dropped from 247 to 219

### 622b0e7 — Fix: restore helpers

| Aspect | Detail |
|--------|--------|
| **Date** | 2026-08-28 16:29 |
| **Author** | Dani6ca-T |
| **Files** | 1 file, +38/−2 |
| **Purpose** | Restore Get-Prerequisite and Expand-ZipFile helpers accidentally deleted in Phase 5 |
| **Architectural impact** | None — bugfix for Phase 5 |

### efa62dc — Phase 6: RDPWrap→TermWrap

| Aspect | Detail |
|--------|--------|
| **Date** | 2026-08-28 18:57 |
| **Author** | Dani6ca-T |
| **Files** | 5 files, +298/−100 |
| **Purpose** | Replace RDPWrap with TermWrap for multi-session RDP patching |
| **Architectural impact** | Install script only — no C# code changes |

Key changes:
- Downloads `llccd/TermWrap` v0.6 release zip (prebuilt DLLs)
- Deploys 4 DLLs: TermWrap.dll, UmWrap.dll → `C:\Program Files\RDP Wrapper\`; EndpWrap.dll, Zydis.dll → `C:\Windows\System32\`
- Imports registry entries to repoint ServiceDll
- Restarts TermService
- No C# code changes — purely infrastructure

---

## 3. Vibepollo Analysis

### What was integrated

A **pure rename** of existing Apollo classes to Vibepollo, plus:
1. Fixed download URL (was pointing to 404)
2. Updated to MSI installer format (Nonary standard)
3. Added advanced feature toggles (Playnite, RTSS, Lossless Scaling, HDR)
4. Added per-app profile management (SeatAppManager)

### How Vibepollo lifecycle was handled

Same as Apollo — `VibepolloManager` is a concrete class directly injected into `SeatManager`. No interface abstraction. The lifecycle is:
- `StartAsync(seat, ct)` — launch process in seat session
- `Stop(seat)` — kill process
- `RestartAsync(seat, ct)` — stop + start
- `IsAlive(seatId)` — check if process is running
- `KillForReconnect(seat)` — kill for session reconnect

### Configuration handling

`VibepolloConfigBuilder` generates `sunshine.conf` per seat. Same pattern as `ApolloConfigBuilder` but with Vibepollo-specific config keys.

### Coupling assessment

**Tightly coupled** — same concrete dependency pattern as master. No interface, no abstraction boundary. The rename was mechanical (find-replace) without architectural improvement.

### Compatibility with planned Provider boundary

**Not directly compatible** — the rename creates a Vibepollo-specific API surface. A proper `IStreamingProvider` interface would need to abstract over both Apollo and Vibepollo. The traycer work makes this harder by introducing Vibepollo-specific config fields (Playnite, RTSS, Lossless Scaling, HDR) that are Vibepollo features, not generic provider features.

### Reusable work

- **Vibepollo URL fix** (Nonary/Vibepollo) — valuable, should be applied to master
- **Phase 3 advanced features** — valuable feature toggles, but need adaptation for provider-agnostic design
- **SeatAppManager** — useful pattern for per-seat app profiles, but tightly coupled to Vibepollo config

---

## 4. TermWrap Analysis

### What problem it solved

RDPWrap's `termsrv.dll` patch is rewritten by every Windows update. Routine Patch Tuesday silently breaks multiseat. TermWrap ships `RDPWrapOffsetFinder` inside the DLL — it disassembles `termsrv.dll` at startup and applies inline hooks automatically.

### How it works

- Downloads `llccd/TermWrap` v0.6 release zip
- Deploys 4 DLLs to system directories
- Imports registry entries to repoint `TermService.ServiceDll` and `UmRdpService.ServiceDll`
- Restarts `TermService` so new DLL loads

### What APIs/classes were introduced

**None** — purely install script changes. No C# code was modified.

### Session lifecycle impact

**None** — TermWrap patches `termsrv.dll` at the driver level. The session creation code (`SessionLauncher`) is unchanged.

### Compatibility with ISessionProvider

**Compatible** — TermWrap is a system-level patch, not a session management abstraction. It works at the same level as RDPWrap. The `ISessionLauncher` interface we extracted is independent of which RDP patching mechanism is used.

### What is already usable by master

**The install script changes only.** The actual C# code is unchanged. To adopt TermWrap on master, only `prerequisites/install-prerequisites.ps1` needs updating.

---

## 5. Audio Analysis

### What changed

Phase 5 (086bea1) **deleted the entire SharedHost audio subsystem**:
- AudioRouter (VAC cable assignment)
- AudioDeviceEnumerator (VAC enumeration)
- AudioCaptureHelper (per-session default device)
- AudioPolicyClient (IPolicyConfig wrapper)
- AudioPeakHelper (VAC peak meter)
- VoiceMeeterConfigurator (VoiceMeeter B1 routing)
- AudioTests (26 tests)

### Why it changed

The commit message explains: "SharedHost mode is gone. PerSession is now the only audio mode. The VAC routing subsystem is deleted because there is no way to install VB-CABLE or VoiceMeeter on a stock host, and every SharedHost seat provision collapsed the host's SWD\MMDEVAPI endpoint nodes from 27 to 1 anyway."

### How per-session audio isolation works

PerSession uses the RDP session's own "Remote Audio" endpoint. Each seat's RDP session has a private audio device that Windows creates automatically. Vibepollo loopback-captures from inside the session. No host-side audio drivers needed.

### Windows-specific APIs involved

- RDP `audiomode:i:0` setting (per-session audio)
- Windows audio endpoint management (automatic per-session)
- `AudioMuteHelper` (mutes console-side mstsc audio) — preserved on traycer

### Can it become an audio provider?

**No** — PerSession audio is not a pluggable provider. It's a fundamental property of the RDP session architecture. There is nothing to abstract.

### What is already reusable

**Nothing new** — master already supports PerSession audio (it was added in commit `8962efb`). Traycer's Phase 5 only deleted the legacy SharedHost code. Master still has both modes (PerSession default, SharedHost legacy).

---

## 6. Master vs Traycer Comparison

### Files that exist on master but NOT on traycer

| File | Purpose | Status on traycer |
|------|---------|-------------------|
| `ISessionLauncher.cs` | Interface extraction | DELETED |
| `IVirtualDisplayManager.cs` | Interface extraction | DELETED |
| `KeepaliveDesktopHelper.cs` | Session desktop isolation | DELETED |
| `ApolloOwnership.cs` | Process ownership | DELETED |
| `Audio/*.cs` (6 files) | Audio subsystem | DELETED (Phase 5) |
| `AudioPeakHelper.cs` | Audio diagnostics | DELETED (Phase 5) |
| 12 test files | Unit tests | DELETED |
| `smoke-seat.ps1` | Smoke test | DELETED |
| `suspend-mstsc-probe.ps1` | Debug script | DELETED |
| 3 audio diagnostic scripts | Audio tools | DELETED |

### Files that exist on traycer but NOT on master

| File | Purpose | Status on master |
|------|---------|------------------|
| `SPEC.md` | Project specification | MISSING |
| `TODO.md` | Task tracking | MISSING |
| `SeatAppsPanel.tsx` | Dashboard UI | MISSING |
| `VibepolloFeaturesPanel.tsx` | Dashboard UI | MISSING |
| `SeatAppManager.cs` | App profile management | MISSING |
| `SeatAppManagerAccessor.cs` | DI accessor | MISSING |
| `SeatAppProfile.cs` | App profile model | MISSING |

### Key naming differences

| Master | Traycer | Notes |
|--------|---------|-------|
| `ApolloManager` | `VibepolloManager` | Pure rename |
| `ApolloConfigBuilder` | `VibepolloConfigBuilder` | Pure rename |
| `ApolloLogParser` | `VibepolloLogParser` | Pure rename |
| `HostApolloMonitor` | `HostVibepolloMonitor` | Pure rename |
| `ApolloServerQuery` | `VibepolloServerQuery` | Pure rename |
| `HostApolloInfo` | `HostVibepolloInfo` | Pure rename |
| `ApolloProcessId` | `VibepolloProcessId` | Property rename |
| `ApolloExePath` | `VibepolloExePath` | Config key rename |

### Test count comparison

| Branch | Passed | Skipped | Failed | Total |
|--------|--------|---------|--------|-------|
| master | 383 | 17 | 0 | 400 |
| traycer | 219 | 14 | 0 | 233 |

Master has **164 more tests** — security tests, integration tests, smoke tests, and the ProcessTracking tests (untracked).

---

## 7. Architectural Compatibility

### Target architecture

```
MultiSeat-Extended
    ├── Control Plane
    │   ├── Seat lifecycle
    │   ├── Reconciliation
    │   ├── Policy
    │   └── Contracts
    └── Infrastructure
        ├── Session
        ├── Display
        ├── Audio
        ├── Input
        ├── Application
        └── Streaming
```

### Traycer's direction

Traycer moves **laterally**, not toward the target:
- Renames Apollo→Vibepollo (naming, not abstraction)
- Adds Vibepollo-specific features (tightening coupling)
- Deletes audio subsystem (simplifying, but not abstracting)
- No new interfaces (no dependency inversion)
- No separation of concerns (SeatManager still orchestrates everything)

### What traycer does NOT have

- No `ISessionLauncher` (our work)
- No `IVirtualDisplayManager` (our work)
- No `IProcessGroup`/`IProcessMonitor`/`IProcessTracker` (ProcessTracking work)
- No provider abstraction
- No control plane / infrastructure separation

### Compatibility assessment

Traycer's work is **orthogonal** to our architectural direction. The rename is mechanical. The advanced features are Vibepollo-specific. The audio deletion simplifies but doesn't abstract. None of it conflicts with our interface extraction work — it simply doesn't contribute to it.

---

## 8. Historical Question

> Was traycer an abandoned experiment, an unfinished implementation, or the continuation of the main project?

**Evidence-based answer: The continuation of the main project.**

Evidence:
1. **Commit messages follow the same convention** as master (conventional commits)
2. **Author is the same** (Dani6ca-T) — the repository owner
3. **Phase numbering is sequential** (1+2, 3, 5, 6) — suggests planned work
4. **SPEC.md and TODO.md** were created on traycer — project planning documents
5. **The work was done in a single day** (2026-08-28, 12:07→18:57) — focused sprint
6. **Tests were run after each phase** — disciplined development
7. **Phase 5 had a bugfix** (622b0e7) — real development, not experiment
8. **Live verification** mentioned in Phase 6 commit — tested on real hardware

However, the branch was **never merged** and master continued to evolve independently with security hardening, testing infrastructure, and interface extraction. The traycer work represents a **parallel development track** that was not integrated.

---

## 9. Reuse Candidates

### KEEP / REUSE

| Item | Why | How |
|------|-----|-----|
| **Vibepollo URL fix** (Nonary/Vibepollo) | Critical — current URL is 404 | Apply to master's install script |
| **Phase 3 advanced features** (Playnite, RTSS, Lossless Scaling, HDR) | Valuable feature set | Cherry-pick after adapter pattern exists |
| **SeatAppManager pattern** | Useful per-app profile management | Adapt for provider-agnostic design |
| **SPEC.md and TODO.md** | Project planning docs | Merge as documentation |

### REWORK

| Item | Why | What needs changing |
|------|-----|-------------------|
| **Apollo→Vibepollo rename** | Correct direction but risky merge | Must be done carefully to avoid conflicts with master's 23 unique commits |
| **Phase 3 Vibepollo config fields** | Vibepollo-specific, not provider-agnostic | Abstract behind provider capability model |
| **Dashboard components** (SeatAppsPanel, VibepolloFeaturesPanel) | Useful UI but tightly coupled to Vibepollo | Decouple from provider-specific naming |

### DO NOT MERGE

| Item | Why |
|------|-----|
| **Phase 5 audio deletion** | Master still has SharedHost support; deleting it removes functionality |
| **Phase 6 TermWrap install script** | Install script changes only; can be applied independently |
| **Test deletions** | Master has 164 more tests; traycer deleted them |
| **Interface deletions** (ISessionLauncher, IVirtualDisplayManager) | Our work; traycer doesn't have them |
| **KeepaliveDesktopHelper deletion** | Master added this; traycer doesn't have it |

---

## 10. ProcessTracking History

**Traycer contains NO ProcessTracking work.**

- No `ProcessTracking/` directory
- No `IProcessGroup.cs`, `IProcessGroupManager.cs`, `IProcessMonitor.cs`, `IProcessTracker.cs`
- No `ManagedProcess.cs`, `ProcessIdentity.cs`, `ProcessExitInfo.cs`
- No `SafeJobHandle.cs`

The ProcessTracking subsystem exists only as untracked files on master.

---

## 11. Recommended Integration Strategy

### Phase A: Documentation merge (safe, no code conflicts)

Merge `SPEC.md` and `TODO.md` from traycer. These are new files on traycer that don't conflict with master.

### Phase B: Vibepollo URL fix (critical, minimal risk)

Apply the download URL fix from traycer to master's install script. The current URL (`vibesoftwarecoder/Vibepollo`) returns 404.

### Phase C: Apollo→Vibepollo rename (careful, high conflict potential)

This is the biggest piece of traycer work but has high merge conflict potential because master has 23 unique commits that touch the same files. Options:
1. **Manual rename** — redo the rename on master, incorporating master's changes
2. **Selective cherry-pick** — cherry-pick specific non-conflicting changes
3. **Deferred** — do the rename as a standalone task after other work stabilizes

### Phase D: Phase 3 advanced features (valuable, needs adaptation)

Cherry-pick the Phase 3 commits after the rename is complete. The feature toggles need adaptation for provider-agnostic design.

### Phase E: Audio cleanup (deferred)

Only consider deleting SharedHost audio code after confirming no user depends on it. Master's保留 of both modes is safer.

---

## 12. Recommended Next Small Step

**Apply the Vibepollo URL fix to master's install script.**

This is the single most critical piece of traycer work:
- The current URL is 404 (broken)
- It's a 1-line fix in `prerequisites/install-prerequisites.ps1`
- Zero risk of conflict
- Immediately useful

After that, the next step would be evaluating whether to do the Apollo→Vibepollo rename as a standalone task.

---

## Evidence

| Section | Source | Status |
|---------|--------|--------|
| Branch topology | `git log --graph --all` | FACT |
| Commit details | `git show --stat` for each commit | FACT |
| File differences | `git diff --name-status` | FACT |
| Traycer code inspection | `git show traycer:...` | FACT |
| Test counts | `git show` commit messages | FACT |
| Historical intent | Commit messages + SPEC.md + TODO.md | ANALYSIS |
| Reuse assessment | Code compatibility analysis | RECOMMENDATION |
