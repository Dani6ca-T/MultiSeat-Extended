# ProcessTracking Read-Only Audit

**Date**: 2026-09-01
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: a8be232

---

## 1. Executive Summary

ProcessTracking is a **significant body of unfinished architectural work** — 20 untracked files (9 shared interfaces/models, 5 service implementations, 6 tests) totaling ~5,200 lines of code and documentation. It provides centralized process ownership tracking (PID + start time), Job Object cleanup guarantees, and event-driven lifecycle monitoring. The work is architecturally sound and solves real gaps in the current codebase (no PID reuse protection, no cleanup guarantee, polling-based crash detection). However, it **cannot compile on master** because it was developed against the traycer branch (references `VibepolloExePath` and `Kernel32` Job Object P/Invoke declarations that don't exist on master). It is **completely disconnected from production code** — zero references from any production class. The recommended next step is **DEFER with a fix plan**, not integration.

---

## 2. Repository State

```text
Branch:           master
HEAD:             a8be232 (docs(audit): add automated test verification results)
origin/master:    a8be232 (in sync)
Working tree:     Clean (only untracked ProcessTracking + docs)

ProcessTracking:  ALL UNTRACKED — not committed to any branch
traycer:          Branch traycer/multiseat-extended-polite-squid exists locally (HEAD: efa62dc)
                  ProcessTracking does NOT exist on traycer either
```

**Key observation**: ProcessTracking was never committed to any branch. It exists only as untracked files in the working tree. No git history exists for these files.

---

## 3. ProcessTracking Inventory

### Shared Interfaces & Models (9 files, 427 lines)

| Path | Lines | Purpose |
|------|-------|---------|
| `src/MultiSeat.Shared/IProcessTracker.cs` | 66 | Process ownership tracking interface |
| `src/MultiSeat.Shared/IProcessMonitor.cs` | 93 | Event-driven lifecycle monitoring interface |
| `src/MultiSeat.Shared/IProcessGroup.cs` | 37 | Job Object cleanup guarantee interface |
| `src/MultiSeat.Shared/IProcessGroupManager.cs` | 30 | Per-seat group management interface |
| `src/MultiSeat.Shared/IProviderLifecycleConsumer.cs` | 33 | Provider exit notification contract |
| `src/MultiSeat.Shared/Models/ProcessIdentity.cs` | 49 | PID + StartedAt value object (PID reuse protection) |
| `src/MultiSeat.Shared/Models/ManagedProcess.cs` | 39 | Tracked process record (identity + owner + type) |
| `src/MultiSeat.Shared/Models/ManagedProcessType.cs` | 36 | Provider/Game/Helper/Other enum |
| `src/MultiSeat.Shared/Models/ProcessExitInfo.cs` | 44 | Exit event data record |

### Service Implementations (5 files, 839 lines)

| Path | Lines | Purpose |
|------|-------|---------|
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessTracker.cs` | 166 | IProcessTracker implementation (ConcurrentDictionary) |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessMonitor.cs` | 267 | IProcessMonitor implementation (Process.Exited events) |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessGroup.cs` | 135 | IProcessGroup implementation (Job Object + KILL_ON_JOB_CLOSE) |
| `src/MultiSeat.Service/ProcessTracking/WindowsProcessGroupManager.cs` | 79 | IProcessGroupManager implementation |
| `src/MultiSeat.Service/ProcessTracking/StartupOrphanDetector.cs` | 192 | Startup orphan process scanner |

### Interop (1 file, 28 lines)

| Path | Lines | Purpose |
|------|-------|---------|
| `src/MultiSeat.Service/Interop/SafeJobHandle.cs` | 28 | SafeHandle wrapper for Job Object handles |

### Tests (6 files, 2,213 lines)

| Path | Lines | Test Count | Coverage |
|------|-------|------------|----------|
| `src/MultiSeat.Tests/ProcessTracking/ProcessTrackerTests.cs` | 575 | ~22 | ProcessIdentity, ManagedProcess, WindowsProcessTracker |
| `src/MultiSeat.Tests/ProcessTracking/ProcessMonitorTests.cs` | 377 | ~16 | WindowsProcessMonitor lifecycle |
| `src/MultiSeat.Tests/ProcessTracking/ProcessGroupTests.cs` | 331 | ~21 | WindowsProcessGroup (Job Object) |
| `src/MultiSeat.Tests/ProcessTracking/GameProcessTrackingTests.cs` | 392 | ~19 | Game process tracking |
| `src/MultiSeat.Tests/ProcessTracking/GameExitIsolationTests.cs` | 298 | ~10 | Game exit isolation |
| `src/MultiSeat.Tests/ProcessTracking/RecoveryGateTests.cs` | 240 | ~6 | Recovery gate atomicity |

**Total**: 20 files, ~5,268 lines (code + tests + documentation)

---

## 4. Architecture

### Current MultiSeat Architecture

```
MultiSeatWorker (BackgroundService)
    ├── MultiSeatWorker.KillOrphanedApolloProcesses() — orphan cleanup at startup
    └── MultiSeatWorker.GetManagedApolloPids() — WMI-based PID discovery

SeatManager
    ├── ApolloManager — concrete, manages Apollo process lifecycle
    │     ├── _instances: ConcurrentDictionary<Guid, ApolloInstance>
    │     │     ├── SeatId, ProcessId (int), StartedAt, RestartCount
    │     ├── StartAsync() → PID
    │     ├── Stop() → Process.Kill(entireProcessTree)
    │     ├── RestartAsync() → stop + start
    │     ├── IsAlive() → process check
    │     └── KillForReconnect() → stop for sleep/wake
    ├── ProcessInjector — concrete, launches processes in sessions
    │     └── Returns raw int PID
    └── SessionHealthCheck — polling-based crash detection (5s interval)

SeatInfo
    └── ApolloProcessId (int) — raw PID, no start time

OnConnectAppLauncher
    └── _states: List<int> LaunchedPids — raw PIDs
```

### ProcessTracking Architecture

```
Domain Layer (MultiSeat.Shared)
    ├── ProcessIdentity (PID + StartedAt) — PID reuse protection
    ├── ManagedProcess (Identity + OwnerSeatId + ProcessType)
    ├── ManagedProcessType (Provider/Game/Helper/Other)
    ├── ProcessExitInfo (exit event data)
    ├── IProcessTracker — ownership tracking interface
    ├── IProcessMonitor — event-driven lifecycle interface
    ├── IProcessGroup — cleanup guarantee interface
    ├── IProcessGroupManager — per-seat group management
    └── IProviderLifecycleConsumer — provider exit notification

Infrastructure Layer (MultiSeat.Service)
    ├── WindowsProcessTracker : IProcessTracker
    │     └── ConcurrentDictionary<ProcessIdentity, ManagedProcess>
    ├── WindowsProcessMonitor : IProcessMonitor
    │     └── Process.Exited event-driven (no polling)
    ├── WindowsProcessGroup : IProcessGroup
    │     └── Job Object + KILL_ON_JOB_CLOSE
    ├── WindowsProcessGroupManager : IProcessGroupManager
    │     └── Per-seat group lifecycle
    ├── StartupOrphanDetector
    │     └── WMI-based orphan scan at startup
    └── SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
          └── RAII wrapper for Job Object handles
```

### Overlap

| Concern | Current Implementation | ProcessTracking Solution | Overlap? |
|---------|----------------------|-------------------------|----------|
| Process ownership | `SeatInfo.ApolloProcessId` (int) + `ApolloManager._instances` (private dict) | `IProcessTracker.Register(ProcessIdentity, SeatId, ManagedProcessType)` | **YES** — different granularity |
| Orphan cleanup | `MultiSeatWorker.KillOrphanedApolloProcesses()` (WMI-based) | `StartupOrphanDetector.DetectOrphans()` (WMI-based, read-only) | **YES** — different approach |
| Crash detection | `SessionHealthCheck` (polling 5s) | `IProcessMonitor.ProcessExited` (event-driven) | **YES** — different mechanism |
| Process kill | `Process.Kill(entireProcessTree)` in ApolloManager.Stop | `IProcessGroup` (Job Object KILL_ON_JOB_CLOSE) | **YES** — safety net vs primary |
| PID reuse | None — raw `int` PID | `ProcessIdentity` (PID + StartedAt) | **NO** — ProcessTracking adds new capability |

---

## 5. Usage / Integration

### For every major type:

**IProcessTracker**
- Used by: **ZERO production consumers**
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: 22+ tests in ProcessTrackerTests.cs
- Status: **Completely unused**

**IProcessMonitor**
- Used by: **ZERO production consumers**
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: 16+ tests in ProcessMonitorTests.cs
- Status: **Completely unused**

**IProcessGroup / IProcessGroupManager**
- Used by: **ZERO production consumers**
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: 21+ tests in ProcessGroupTests.cs
- Status: **Completely unused**

**IProviderLifecycleConsumer**
- Used by: **ZERO production consumers**
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: Referenced in GameExitIsolationTests.cs
- Status: **Completely unused**

**SafeJobHandle**
- Used by: **WindowsProcessGroup only** (within ProcessTracking)
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: Indirectly via ProcessGroupTests.cs
- Status: **Internal to ProcessTracking**

**StartupOrphanDetector**
- Used by: **ZERO production consumers**
- DI: **NOT registered**
- Runtime: **Never instantiated**
- Tests: **NO tests**
- Status: **Completely unused**

**ProcessIdentity, ManagedProcess, ProcessExitInfo, ManagedProcessType**
- Used by: **ZERO production consumers** (only within ProcessTracking itself)
- DI: N/A (value types)
- Runtime: **Never instantiated**
- Tests: Extensive coverage in ProcessTrackerTests.cs
- Status: **Internal to ProcessTracking**

### Summary

| Type | Production References | DI Registration | Status |
|------|----------------------|-----------------|--------|
| IProcessTracker | 0 | None | **Disconnected** |
| IProcessMonitor | 0 | None | **Disconnected** |
| IProcessGroup | 0 | None | **Disconnected** |
| IProcessGroupManager | 0 | None | **Disconnected** |
| IProviderLifecycleConsumer | 0 | None | **Disconnected** |
| ProcessIdentity | 0 | N/A | **Disconnected** |
| ManagedProcess | 0 | N/A | **Disconnected** |
| SafeJobHandle | 0 (internal) | None | **Disconnected** |
| StartupOrphanDetector | 0 | None | **Disconnected** |

**ProcessTracking is 100% disconnected from the production codebase.**

---

## 6. Build Impact

```text
Build:            FAILS (9 unique errors, 18 with duplicates)
Errors:           9 unique compiler errors
ProcessTracking:  ALL 9 errors originate from ProcessTracking files
Unrelated:        0 errors from tracked production code
cdee422-related:  0 errors
Environment:      0 errors
```

### Error Details

| File | Error | Root Cause |
|------|-------|------------|
| `StartupOrphanDetector.cs:53` | CS1061: `MultiSeatOptions` does not contain `VibepolloExePath` | Traycer branch property name — master uses `ApolloExePath` |
| `WindowsProcessGroup.cs:37` | CS0117: `Kernel32` does not contain `CreateJobObjectW` | Missing Job Object P/Invoke in Kernel32.cs |
| `WindowsProcessGroup.cs:82` | CS0117: `Kernel32` does not contain `AssignProcessToJobObject` | Missing Job Object P/Invoke |
| `WindowsProcessGroup.cs:110` | CS0426: `Kernel32.JobObjectExtendedLimitInformation` does not exist | Missing struct definition |
| `WindowsProcessGroup.cs:112` | CS0426: `Kernel32.JobObjectBasicLimitInformation` does not exist | Missing struct definition |
| `WindowsProcessGroup.cs:114` | CS0117: `Kernel32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` does not exist | Missing constant |
| `WindowsProcessGroup.cs:118` | CS0117: `Kernel32.SetInformationJobObject` does not exist | Missing P/Invoke |
| `WindowsProcessGroup.cs:120` | CS0117: `Kernel32.JobObjectInfoClassExtendedLimitInformation` does not exist | Missing enum |
| `WindowsProcessGroup.cs:122` | CS0426: `Kernel32.JobObjectExtendedLimitInformation` (duplicate) | Missing struct |

**Classification**: ProcessTracking was developed against the traycer branch which likely had extended `Kernel32.cs` with Job Object declarations. These declarations are missing from master's `Kernel32.cs`.

---

## 7. Existing Process Management Comparison

| Problem | Current Implementation | ProcessTracking Solution | Duplicate? | Better? |
|---------|----------------------|-------------------------|------------|---------|
| **Process ownership** | `SeatInfo.ApolloProcessId` (int) + `ApolloManager._instances` (private dict, Guid→ApolloInstance with PID+StartedAt+RestartCount) | `IProcessTracker` with `ProcessIdentity` (PID+StartedAt) + `ManagedProcess` (Identity+OwnerSeatId+ProcessType) | **YES** — overlapping | ProcessTracking is **more general** (handles Provider+Game+Helper+Other, not just Provider) |
| **PID reuse protection** | **NONE** — raw `int` PID stored in SeatInfo | `ProcessIdentity` (PID + StartedAt composite key) with `IsAlive()` checking both PID existence AND start time | **NO** — new capability | ProcessTracking **adds** critical safety |
| **Orphan cleanup** | `MultiSeatWorker.KillOrphanedApolloProcesses()` — WMI scan, matches by exe path + config path, kills managed orphans | `StartupOrphanDetector.DetectOrphans()` — WMI scan, matches by config path, READ-ONLY (no killing) | **YES** — overlapping | Current is **more aggressive** (kills); ProcessTracking is **diagnostic only** |
| **Crash detection** | `SessionHealthCheck` — 5s polling, checks `Process.HasExited` + Apollo HTTP endpoint | `IProcessMonitor` — event-driven via `Process.Exited`, PID reuse protection, expected/unexpected exit classification | **YES** — overlapping | ProcessTracking is **more efficient** (no polling) and **more correct** (PID reuse protection) |
| **Cleanup guarantee** | `Process.Kill(entireProcessTree: true)` — primary kill, can fail (access denied, stale handle) | `IProcessGroup` (Job Object KILL_ON_JOB_CLOSE) — safety net when Kill fails or service crashes | **YES** — overlapping | ProcessTracking **adds** safety net; current has no fallback |
| **Game process tracking** | **NONE** — `OnConnectAppLauncher._states: List<int> LaunchedPids` (raw PIDs, no ownership) | `IProcessTracker` + `IProcessMonitor` + `IProcessGroup` — full ownership + lifecycle + cleanup for Game processes | **NO** — new capability | ProcessTracking **adds** game process management |
| **Provider lifecycle** | `ApolloManager` — concrete, manages Start/Stop/Restart/IsAlive/KillForReconnect | `IProviderLifecycleConsumer` — contract for provider exit notification, decouples from specific provider | **NO** — new abstraction | ProcessTracking **prepares** for provider abstraction |

---

## 8. Test Assessment

### What ProcessTracking tests tell us

The tests are **thorough and well-structured**:

1. **ProcessIdentityTests** (7 tests): Value object correctness — constructor, rejection, equality, comparison, PID reuse detection
2. **ManagedProcessTests** (2 tests): Record correctness — required properties, defaults
3. **WindowsProcessTrackerTests** (13 tests): Ownership — register/unregister, seat isolation, concurrent access, PID reuse
4. **WindowsProcessMonitorTests** (16 tests): Lifecycle — event-driven detection, expected/unexpected exit, disposal, PID reuse suppression
5. **WindowsProcessGroupTests** (21 tests): Cleanup — Job Object creation, process assignment, KillOnClose, seat isolation
6. **GameProcessTrackingTests** (19 tests): Game-specific — ownership, multiple games per seat, cross-seat isolation
7. **GameExitIsolationTests** (10 tests): Isolation — game exits don't trigger provider recovery
8. **RecoveryGateTests** (6 tests): Concurrency — atomic recovery gate prevents duplicate restarts

### Test quality assessment

| Aspect | Assessment |
|--------|------------|
| Behavior tested | Meaningful invariants (PID reuse, seat isolation, expected/expected exit) |
| Production code exists | **YES** — implementations exist but are disconnected |
| Compiles | **NO** — tests cannot compile due to build errors in production code |
| Tests meaningful requirements | **YES** — each test proves a specific architectural invariant |
| Obsolete? | **NO** — tests are forward-looking, covering requirements not yet in production |
| Ahead of production? | **YES** — tests cover requirements that production code doesn't yet implement |

### Classification

ProcessTracking tests indicate: **unfinished implementation with test-first design** — the tests were written alongside or before the implementations, covering the full intended behavior. The tests are not prototypes; they represent the target behavior.

---

## 9. Security / Stability Findings

| Finding | Severity | Evidence |
|---------|----------|----------|
| **Job Object KILL_ON_JOB_CLOSE handles service crash** | Low (informational) | `WindowsProcessGroup.Dispose()` closes Job handle → Windows terminates assigned processes. This is a **safety improvement**, not a risk. |
| **SafeJobHandle prevents handle leaks** | Low (informational) | Extends `SafeHandleZeroOrMinusOneIsInvalid` — RAII pattern prevents orphaned kernel handles. |
| **ProcessIdentity prevents PID reuse** | Low (informational) | Composite key (PID + StartedAt) prevents stale entries from affecting new processes. This is a **safety improvement**. |
| **WindowsProcessMonitor uses Process.Exited** | Low (informational) | `Process.Exited` internally uses `WaitForSingleObject` on process handle. The handle is held for the monitoring duration. If the service crashes, the handle is released by Windows. No leak risk. |
| **StartupOrphanDetector reads WMI command lines** | Informational | WMI `Win32_Process.CommandLine` requires sufficient privileges. Running as SYSTEM — no issue. |
| **No race condition in RecoveryGate** | Informational | Uses `ConcurrentDictionary<Guid, bool>.TryAdd` for atomic check-and-set. Only one caller per seat can acquire the gate. |
| **Process.Kill fallback to Job Object** | Informational | Primary kill is `Process.Kill(entireProcessTree)`. If that fails, `KILL_ON_JOB_CLOSE` catches stragglers. Two-layer defense. |

**No critical or high severity findings.** The ProcessTracking code is well-designed for security and stability.

---

## 10. Architecture Fit

### Does ProcessTracking fit the current architecture?

**YES, with qualifications.**

1. **Fits the domain model**: Process ownership, lifecycle, and cleanup are genuine domain concerns for a multi-seat streaming system.

2. **Does NOT introduce a second process-management architecture**: ProcessTracking is designed to **complement** the existing architecture, not replace it. The current architecture has no centralized process ownership — ProcessTracking fills a real gap.

3. **Should belong to the provider layer**: `IProcessTracker`, `IProcessMonitor`, `IProcessGroup` are provider-agnostic (they work with any process, not just Apollo). They belong in the **seat/session infrastructure layer**, not the provider layer. The implementations (`WindowsProcessTracker`, etc.) are Windows-specific infrastructure.

4. **`IProviderLifecycleConsumer` is correctly placed**: It sits in `MultiSeat.Shared` as a contract that provider managers (ApolloManager, future VibepolloManager) would implement. It decouples the lifecycle monitor from the specific provider.

5. **Does NOT create unnecessary abstraction**: The three interfaces (`IProcessTracker`, `IProcessMonitor`, `IProcessGroup`) represent genuinely different concerns (ownership, lifecycle, cleanup). This is not interface proliferation — it's clean separation.

6. **Improves the Apollo/Vibepollo architecture**: By providing a provider-agnostic process ownership model, ProcessTracking makes it easier to introduce Vibepollo or any future provider. The provider only needs to register/unregister processes with the tracker.

7. **Makes future Vibepollo support EASIER**: ProcessTracking is provider-agnostic by design. A VibepolloManager would use the same `IProcessTracker` and `IProcessMonitor` interfaces.

### Does it create architectural problems?

**One concern**: The documentation references `VibepolloManager` (traycer naming), not `ApolloManager` (master naming). This indicates the work was developed against a different codebase state. The core interfaces and implementations are provider-agnostic, but the integration documentation needs updating.

---

## 11. Readiness Score

```text
Architecture:     4/5 — Well-designed, clean separation, provider-agnostic interfaces
                  (deducted 1 because it was developed against wrong branch naming)

Implementation:   3/5 — Solid implementations, but cannot compile on master
                  (deducted 2 because of missing Kernel32 P/Invoke and wrong property name)

Integration:      1/5 — Zero production consumers, no DI registration, completely disconnected
                  (deducted 4 because nothing wires it in)

Testing:          4/5 — Thorough test coverage, meaningful invariants tested
                  (deducted 1 because tests can't compile without fixing build errors)

Stability:        4/5 — Thread-safe, PID reuse protection, Job Object safety net
                  (deducted 1 because untested at runtime in production context)

Total:            16/25 — Integration candidate (with fixes)
```

**Classification: Partially ready — needs compilation fixes before integration**

---

## 12. Decision Matrix

| Option | Advantages | Risks | Effort | Recommendation |
|--------|-----------|-------|--------|----------------|
| **Integrate now** | Immediate value (PID reuse protection, cleanup guarantee, event-driven monitoring) | Cannot compile; would require fixing Kernel32 + renaming Vibepollo→Apollo references + wiring DI + modifying ApolloManager + modifying SeatManager + modifying MultiSeatWorker | HIGH | **NO** — too much scope for a single step |
| **Fix first** | Enables compilation; preserves all existing work; minimal risk | Still disconnected after fix; requires Kernel32 additions + property rename | LOW-MEDIUM | **YES — RECOMMENDED** |
| **Refactor first** | Could improve architecture before integration | Risk of breaking well-tested code; adds scope; ProcessTracking architecture is already sound | MEDIUM | **NO** — architecture is fine |
| **Defer** | No risk; keeps options open | Accumulates more technical debt; ProcessTracking already causes build failures | ZERO | **POSSIBLE** but less valuable than fix |
| **Remove** | Eliminates build noise; clean working tree | Loses ~5,200 lines of well-designed work; would need to be recreated later | ZERO | **NO** — work has genuine value |

---

## 13. Recommendation

### DEFER with a FIX PLAN

**Do NOT integrate now.** ProcessTracking is architecturally valuable but needs compilation fixes before it can be integrated. The recommended approach is:

1. **Fix compilation** (small, focused)
2. **Then defer integration** until the provider boundary is ready
3. **Then integrate as part of provider abstraction** (natural fit)

### Why not integrate now?

1. **Cannot compile** — 9 errors from missing Kernel32 P/Invoke + wrong property name
2. **Zero production consumers** — no code references ProcessTracking types
3. **No DI registration** — services are never instantiated
4. **Integration would require modifying 5+ production files** — ApolloManager, SeatManager, MultiSeatWorker, Program.cs, Kernel32.cs — too much scope
5. **ProcessTracking is designed for a future state** — it references `VibepolloManager` and `IProviderLifecycleConsumer`, concepts that don't exist on master yet

### Why not remove?

1. **~5,200 lines of well-designed, well-tested code** — represents significant architectural investment
2. **Solves real problems** — PID reuse protection, cleanup guarantee, event-driven monitoring
3. **Provider-agnostic design** — makes future Vibepollo support easier
4. **Thorough test coverage** — 94+ tests covering meaningful invariants

---

## 14. Next Steps (if fixing)

### Step 1: Fix Kernel32.cs Job Object declarations

Add to `src/MultiSeat.Service/Interop/Kernel32.cs`:

```csharp
// Job Object constants and structs
public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

public const int JobObjectInfoClassExtendedLimitInformation = 9;

[StructLayout(LayoutKind.Sequential)]
public struct JobObjectBasicLimitInformation
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public IntPtr MinimumWorkingSetSize;
    public IntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public IntPtr AffinityPriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
public struct JobObjectExtendedLimitInformation
{
    public JobObjectBasicLimitInformation BasicLimitInformation;
    public IntPtr IoInfo;
    public IntPtr ProcessMemoryLimit;
    public IntPtr JobMemoryLimit;
    public IntPtr PeakProcessMemoryUsed;
    public IntPtr PeakJobMemoryUsed;
}

[LibraryImport(Lib, SetLastError = true)]
public static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

[LibraryImport(Lib, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass, ref JobObjectExtendedLimitInformation lpJobObjectInformation, uint cbJobObjectInformationLength);

[LibraryImport(Lib, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
```

### Step 2: Fix StartupOrphanDetector.cs property name

Change `VibepolloExePath` to `ApolloExePath` in `StartupOrphanDetector.cs` line 53.

### Step 3: Verify build passes

After fixes, ProcessTracking should compile (though still disconnected from production).

### Step 4: Defer integration

Leave ProcessTracking as untracked files until:
- Provider boundary (`IStreamingProvider`) is implemented
- ApolloManager is refactored to use `IProcessTracker` / `IProcessMonitor` / `IProcessGroup`
- SeatManager delegates process lifecycle to the tracker

---

## 15. Repository Safety

```text
Production files modified:   NO
Tests modified:              NO
ProcessTracking modified:    NO
csproj modified:             NO
Documentation modified:      NO
Commit created:              NO
Push performed:              NO
```

---

## Final Verdict

```
PROCESS TRACKING SHOULD BE DEFERRED
```

ProcessTracking is architecturally sound, well-tested, and solves real problems. But it was developed against the wrong branch, cannot compile on master, and is completely disconnected from production code. The correct next step is to **fix compilation** (small, focused), then **defer integration** until the provider boundary is ready. The work has genuine value and should not be removed.

---

## Evidence Classification

| Section | Source | Classification |
|---------|--------|---------------|
| Repository state | `git status`, `git log`, `git branch` | FACT |
| File inventory | `find`, `ls`, `wc -l` | FACT |
| Build errors | `dotnet build` output | FACT |
| Production references | `code_search` for ProcessTracking types | FACT |
| Architecture comparison | Code inspection + documentation analysis | OBSERVATION |
| Test assessment | Test file inspection + compilation check | OBSERVATION |
| Security findings | Code inspection of Handle management, PID reuse | OBSERVATION |
| Architecture fit | Comparison against current architecture principles | RECOMMENDATION |
| Readiness score | Based on evidence gathered | RECOMMENDATION |
| Decision matrix | Based on evidence gathered | RECOMMENDATION |
| Next steps | Based on evidence gathered | RECOMMENDATION |
