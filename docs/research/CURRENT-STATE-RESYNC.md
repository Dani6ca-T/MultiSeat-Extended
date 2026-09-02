# Current State Re-Sync

## Date

2026-08-30

## Git State

```
Branch:    master
HEAD:      efa62dc1d6e418cbf479c2f9d3fff775caa661e6
Date:      2026-08-28 18:57:01 +0300
Message:   feat: Phase 6 - migrate RDPWrap to TermWrap
Author:    Dani6ca-T
```

### Uncommitted Changes

Our P0-1 (process tracking) and P0-2 (Job Objects) work is **uncommitted**:

```
M  Kernel32.cs, Program.cs, SeatManager.cs, VibepolloManager.cs
?? SafeJobHandle.cs, ProcessTracking/, IProcessGroup.cs, IProcessGroupManager.cs
?? IProcessTracker.cs, ManagedProcess.cs, ManagedProcessType.cs, ProcessIdentity.cs
?? ProcessGroupTests.cs, ProcessTrackerTests.cs
```

---

## Latest Commits (since our research)

| Date | Author | Area | Change |
|------|--------|------|--------|
| 2026-08-28 18:57 | Dani6ca-T | RDP | Phase 6: RDPWrap → TermWrap |
| 2026-08-28 16:29 | Dani6ca-T | Scripts | Fix Get-Prerequisite helpers |
| 2026-08-28 14:45 | Dani6ca-T | Audio | Phase 5: Remove VB-CABLE/VoiceMeeter |
| 2026-08-28 13:12 | Dani6ca-T | Streaming | Phase 3: Vibepollo advanced features |
| 2026-08-28 12:07 | Dani6ca-T | Core | Phase 1+2: Apollo→Vibepollo + PerSession |
| 2026-08-26 15:03 | José | Input | HidHide jail probe fix (#20) |
| 2026-08-22 11:55 | vibesoftwarecoder | Sessions | Refuse wrong-session launch |
| 2026-08-21 11:08 | vibesoftwarecoder | Scripts | watch-seat-cursor.ps1 |
| 2026-08-21 10:38 | vibesoftwarecoder | Input | HidHide jail proven |
| 2026-08-21 09:40 | vibesoftwarecoder | Input | Per-seat gamepad isolation via HidHide |

**Key architectural changes committed AFTER our research:**
1. **Apollo → Vibepollo** — full rename across 67 files
2. **PerSession audio only** — VB-CABLE/VoiceMeeter removed
3. **TermWrap** — replaces RDPWrap
4. **Vibepollo advanced features** — Playnite, RTSS, Lossless Scaling, HDR config

---

## Current Repository Structure

```
src/
├── MultiSeat.slnx
├── MultiSeat.Shared/           # Domain models, interfaces, constants
│   ├── Models/
│   │   ├── ProcessIdentity.cs      ← P0-1 (uncommitted)
│   │   ├── ManagedProcess.cs       ← P0-1 (uncommitted)
│   │   ├── ManagedProcessType.cs   ← P0-1 (uncommitted)
│   │   ├── SeatInfo.cs
│   │   ├── SeatRequest.cs
│   │   └── ...
│   ├── IProcessTracker.cs          ← P0-1 (uncommitted)
│   ├── IProcessGroup.cs            ← P0-2 (uncommitted)
│   └── IProcessGroupManager.cs     ← P0-2 (uncommitted)
├── MultiSeat.Service/          # Windows Service (SYSTEM)
│   ├── Interop/
│   │   ├── Kernel32.cs             ← P0-2 added Job Object P/Invoke
│   │   ├── SafeJobHandle.cs        ← P0-2 (uncommitted)
│   │   └── ...
│   ├── ProcessTracking/
│   │   ├── WindowsProcessTracker.cs    ← P0-1 (uncommitted)
│   │   ├── WindowsProcessGroup.cs      ← P0-2 (uncommitted)
│   │   └── WindowsProcessGroupManager.cs ← P0-2 (uncommitted)
│   ├── Sessions/
│   │   ├── SeatManager.cs          ← P0-1/P0-2 integration (uncommitted)
│   │   ├── SessionLauncher.cs
│   │   ├── ProcessInjector.cs
│   │   ├── RdpWrapper.cs
│   │   └── ...
│   ├── Streaming/
│   │   ├── VibepolloManager.cs     ← P0-1/P0-2 integration (uncommitted)
│   │   ├── VibepolloConfigBuilder.cs
│   │   ├── OnConnectAppLauncher.cs
│   │   └── ...
│   ├── Display/
│   │   └── VirtualDisplayManager.cs
│   ├── Input/
│   │   ├── HidHideConfigurator.cs
│   │   ├── InputRouter.cs
│   │   └── InputHookManager.cs     ← still no-op
│   ├── Monitoring/
│   │   └── SessionHealthCheck.cs
│   └── Program.cs                  ← P0-1/P0-2 DI (uncommitted)
├── MultiSeat.Tests/
│   ├── ProcessTracking/
│   │   ├── ProcessTrackerTests.cs  ← P0-1 (uncommitted)
│   │   └── ProcessGroupTests.cs    ← P0-2 (uncommitted)
│   └── ...
├── MultiSeat.Dashboard/        # React + TypeScript
└── MultiSeat.InputHook/        # C++ DLL (currently no-op)
```

---

## Current Architecture

### Streaming: Vibepollo (NOT Apollo)

**Source evidence:**
- `src/MultiSeat.Service/Streaming/VibepolloManager.cs` — class `VibepolloManager`
- `VibepolloConfigBuilder.cs`, `VibepolloLogParser.cs`
- `MultiSeatOptions.VibepolloExePath`, `VibepolloConfigDir`
- Download URL: `https://github.com/Nonary/Vibepollo`

### Audio: PerSession ONLY (no VB-CABLE/VoiceMeeter)

**Source evidence:**
- `MultiSeatOptions.cs` line 137: "PerSession (the only supported mode)"
- `MultiSeatOptions.cs` line 140: "There used to be a SharedHost mode... It is gone"
- `RdpFileBuilder.cs`: always uses PerSession audio
- No VB-CABLE/VoiceMeeter installation in `install-prerequisites.ps1`

### RDP: TermWrap (NOT RDPWrap)

**Source evidence:**
- `install-prerequisites.ps1` line 395: "# 4. TermWrap (concurrent RDP sessions on Home/Pro)"
- `install-prerequisites.ps1` line 480: downloads `TermWrap-0.6.zip` from `llccd/TermWrap`
- `prerequisites/README.txt`: lists `TermWrap-0.6.zip`
- `RdpWrapper.cs` still detects both for backward compatibility

### Process Tracking: P0-1 EXISTS (uncommitted)

**Source evidence (uncommitted):**
- `MultiSeat.Shared/IProcessTracker.cs` — interface
- `MultiSeat.Shared/Models/ProcessIdentity.cs` — PID + StartedAt
- `MultiSeat.Shared/Models/ManagedProcess.cs` — ownership record
- `MultiSeat.Service/ProcessTracking/WindowsProcessTracker.cs` — implementation
- `VibepolloManager.cs` — integrates Register/Unregister

### Job Objects: P0-2 EXISTS (uncommitted)

**Source evidence (uncommitted):**
- `MultiSeat.Shared/IProcessGroup.cs` — interface
- `MultiSeat.Shared/IProcessGroupManager.cs` — manager interface
- `MultiSeat.Service/ProcessTracking/WindowsProcessGroup.cs` — Win32 implementation
- `MultiSeat.Service/Interop/Kernel32.cs` — CreateJobObjectW, AssignProcessToJobObject
- `MultiSeat.Service/Interop/SafeJobHandle.cs` — SafeHandle wrapper
- `SeatManager.cs` — creates group early, disposes last
- `VibepolloManager.cs` — assigns process to group after start

### Health Check: EXISTS (committed)

**Source evidence:**
- `src/MultiSeat.Service/Monitoring/SessionHealthCheck.cs`
- `MultiSeatOptions.HealthCheckIntervalMs = 5_000`
- `VibepolloManager.MaxRestartAttempts = 3`

### Input: HidHide session jail EXISTS, InputHookManager is NO-OP

**Source evidence:**
- `HidHideConfigurator.cs` — working session jail
- `InputHookManager.cs` line 73: "EnableKeyboardMouseIsolation is ON, but it filters NOTHING"
- `MultiSeatOptions.EnableKeyboardMouseIsolation = false` (default)

### Display: SudoVDA, no HDR

**Source evidence:**
- `VirtualDisplayManager.cs` — SudoVDA integration
- `MultiSeatOptions.EnableHdr = false` — comment says "Currently a NO-OP"
- `AdvancedColorHelper.cs` line 33: "EnableHdr option never invoked it"

---

## P0-1 Status: EXISTS (uncommitted)

| Component | Status | Location |
|-----------|--------|----------|
| ProcessIdentity | EXISTS | `MultiSeat.Shared/Models/ProcessIdentity.cs` |
| ManagedProcess | EXISTS | `MultiSeat.Shared/Models/ManagedProcess.cs` |
| ManagedProcessType | EXISTS | `MultiSeat.Shared/Models/ManagedProcessType.cs` |
| IProcessTracker | EXISTS | `MultiSeat.Shared/IProcessTracker.cs` |
| WindowsProcessTracker | EXISTS | `MultiSeat.Service/ProcessTracking/WindowsProcessTracker.cs` |
| VibepolloManager integration | EXISTS | Register/Unregister in Start/Stop/Restart |
| SeatManager integration | EXISTS | Creates group in ProvisionSeatAsync |
| DI registration | EXISTS | Program.cs |
| Tests (22) | EXISTS | ProcessTrackerTests.cs |
| Documentation | EXISTS | P0-1-PROCESS-OWNERSHIP.md, P0-1-REVIEW.md |

**P0-1 is COMPLETE but UNCOMMITTED.**

---

## P0-2 Status: EXISTS (uncommitted)

| Component | Status | Location |
|-----------|--------|----------|
| IProcessGroup | EXISTS | `MultiSeat.Shared/IProcessGroup.cs` |
| IProcessGroupManager | EXISTS | `MultiSeat.Shared/IProcessGroupManager.cs` |
| WindowsProcessGroup | EXISTS | `MultiSeat.Service/ProcessTracking/WindowsProcessGroup.cs` |
| WindowsProcessGroupManager | EXISTS | `MultiSeat.Service/ProcessTracking/WindowsProcessGroupManager.cs` |
| SafeJobHandle | EXISTS | `MultiSeat.Service/Interop/SafeJobHandle.cs` |
| Kernel32 Job Object P/Invoke | EXISTS | `Kernel32.cs` |
| VibepolloManager integration | EXISTS | AssignProcess after Start |
| SeatManager integration | EXISTS | Create early, dispose last |
| DI registration | EXISTS | Program.cs |
| Tests (21) | EXISTS | ProcessGroupTests.cs |
| Documentation | EXISTS | P0-2-JOB-OBJECTS.md |

**P0-2 is COMPLETE but UNCOMMITTED.**

---

## Current Gaps (verified from source)

| Gap | Source Evidence | Priority |
|-----|----------------|----------|
| Game process tracking | No game PID tracking in codebase | P1 |
| Provider abstraction (IStreamingProvider) | VibepolloManager is provider-specific | P1 |
| Startup orphan scan | No orphan detection on service start | P1 |
| Process exit monitoring | No Process.Exited subscription | P1 |
| HDR support | EnableHdr is no-op (`AdvancedColorHelper.cs:33`) | P2 |
| KB/M session isolation | InputHookManager is no-op (`InputHookManager.cs:73`) | P2/P3 |
| Progressive crash backoff | MaxRestartAttempts=3 but no time-based backoff | P2 |
| Steam multi-instance | No implementation | P3 |

---

## Outdated Documentation

| Document | Claim | Current Reality | Action |
|----------|-------|-----------------|--------|
| `CURRENT-ARCHITECTURE.md` | "RDPWrap (TermsWrap)" | TermWrap (fork of RDPWrap) | UPDATE |
| `RDP-GAP-ANALYSIS.md` | "Uses RDPWrap/TermWrap" | Uses TermWrap only | UPDATE |
| `LICENSE-AUDIT.md` | "RDPWrap: MIT" | TermWrap: MIT | UPDATE |
| Multiple docs | "Apollo" references | Vibepollo | UPDATE |
| `AUDIO-ARCHITECTURE.md` | SharedHost + PerSession modes | PerSession only | UPDATE |
| `MASTER-CAPABILITY-MATRIX.md` | "RDP: RDPWrap" | TermWrap | UPDATE |
| `CURRENT-STATE-RESYNC.md` itself | Will need future update | — | PLAN |

**Note:** These are documentation drift issues, not code issues. The code is correct.

---

## Test Results

```
Before P0-1/P0-2: 262 tests (248 pass, 14 skip)
After P0-1/P0-2:  283 tests (269 pass, 14 skip)
New tests:          21 (P0-2 ProcessGroupTests)
```

All tests pass. No regressions.

---

## Revised Roadmap

### DONE (committed + uncommitted)

| Item | Status |
|------|--------|
| P0-1 Process Ownership | ✅ COMPLETE (uncommitted) |
| P0-2 Job Objects | ✅ COMPLETE (uncommitted) |
| Apollo→Vibepollo rename | ✅ COMMITTED |
| PerSession audio | ✅ COMMITTED |
| VB-CABLE/VoiceMeeter removal | ✅ COMMITTED |
| TermWrap migration | ✅ COMMITTED |
| HidHide session jail | ✅ COMMITTED |
| Health checks (5s) | ✅ COMMITTED |
| Crash recovery (MaxRestartAttempts) | ✅ COMMITTED |

### STILL REQUIRED

| Item | Priority | Effort |
|------|----------|--------|
| Game process tracking | P1 | Medium |
| Provider abstraction (IStreamingProvider) | P1 | High |
| Startup orphan scan | P1 | Low |
| Process exit monitoring | P1 | Medium |
| Progressive crash backoff | P2 | Low |
| HDR support | P2 | High (driver-level) |
| KB/M session isolation | P2/P3 | High (re-architect hooks) |

### NOT NEEDED

| Item | Reason |
|------|--------|
| Apollo research | Renamed to Vibepollo |
| RDPWrap research | Replaced by TermWrap |
| VB-CABLE/VoiceMeeter research | Removed |
| SharedHost audio research | Removed |

---

## Current Project Status

```
P0-1:  ✅ DONE (uncommitted) — process tracking with PID+StartTime identity
P0-2:  ✅ DONE (uncommitted) — Job Objects with KILL_ON_JOB_CLOSE
P0-3:  ❌ NOT STARTED — orphan scan, process exit monitoring, startup recovery
P1:    ❌ NOT STARTED — game tracking, provider abstraction, progressive backoff
P2:    ❌ NOT STARTED — HDR, KB/M isolation re-architecture
```

---

## DO NOT IMPLEMENT YET

P0-1 and P0-2 are implemented but UNCOMMITTED. Before any further implementation:

1. **Commit P0-1 + P0-2** — they are complete, tested, and reviewed
2. **Update outdated documentation** — Apollo→Vibepollo, RDPWrap→TermWrap, SharedHost removal
3. **Then** proceed to P0-3 (orphan scan, process exit monitoring)

---

## Recommended Next Task

**Commit P0-1 + P0-2**, then begin **P0-3: Process Exit Monitoring + Startup Orphan Scan**.

P0-3 would add:
- `Process.Exited` event subscription for provider processes
- On service start: scan for orphaned provider processes and clean up
- Integration with SessionHealthCheck for process liveness
