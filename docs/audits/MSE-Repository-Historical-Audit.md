# MultiSeat-Extended — Historical & Current Architecture Audit

**Date**: 2026-08-31
**Auditor**: Buffy (Codebuff)
**Branch**: `master`
**HEAD**: `5f03be0` (refactor(display): extract IVirtualDisplayManager)

---

## 1. Executive Summary

MultiSeat-Extended is a Windows-first multi-seat orchestration platform forked from [vibesoftwarecoder/MultiSeat](https://github.com/vibesoftwarecoder/MultiSeat). It enables multiple simultaneous Moonlight game-streaming sessions on a single Windows host.

The project has evolved through **two parallel development tracks** that have diverged:

1. **master** (current HEAD: `5f03be0`) — focused on security hardening, testing infrastructure, MoonlightWeb integration, and interface extraction
2. **traycer/multiseat-extended-polite-squid** (HEAD: `efa62dc`) — focused on Apollo→Vibepollo rename, audio architecture changes, and TermWrap migration

The codebase contains **extensive untracked architectural documentation** (35 architecture docs, 55 research docs) and **untracked ProcessTracking subsystem work** (interfaces + implementations + tests) that has not been committed.

**Current state**: The project is functional on master with 383 passing tests, but has significant architectural debt and uncommitted work that represents the intended direction of development.

---

## 2. Current Git State

### Branches

| Branch | HEAD | Divergence |
|--------|------|------------|
| `master` (local + origin) | `5f03be0` | 20 commits ahead of divergence point |
| `traycer/multiseat-extended-polite-squid` | `efa62dc` | 5 commits ahead of divergence point |
| Divergence point | `067a2d0` | fix(input): the jail probe reads which failure it was |

### master commits (after divergence)

Focused on:
- **Security**: TLS identity, credential locking, permission hardening, API authentication
- **Testing**: Smoke tests, unit tests for presets/health/HidHide/firewall/library
- **Integration**: MoonlightWeb fixes, session reconnect improvements
- **Interfaces**: ISessionLauncher extraction, IVirtualDisplayManager extraction
- **Cleanup**: Shared port alias removal

### traycer branch commits (after divergence)

Focused on:
- **Phase 1+2**: Apollo→Vibepollo rename + PerSession audio (no drivers needed)
- **Phase 3**: Vibepollo advanced features (Playnite, RTSS, Lossless Scaling, HDR)
- **Phase 5**: Remove VB-CABLE/VoiceMeeter legacy (PerSession only)
- **Phase 6**: RDPWrap→TermWrap migration

### Untracked Work

| Category | Files | Status |
|----------|-------|--------|
| **ProcessTracking interfaces** | IProcessGroup.cs, IProcessGroupManager.cs, IProcessMonitor.cs, IProcessTracker.cs, IProviderLifecycleConsumer.cs | Untracked, in MultiSeat.Shared |
| **ProcessTracking implementations** | WindowsProcessGroup.cs, WindowsProcessGroupManager.cs, WindowsProcessMonitor.cs, WindowsProcessTracker.cs, StartupOrphanDetector.cs | Untracked, in ProcessTracking/ |
| **ProcessTracking models** | ManagedProcess.cs, ManagedProcessType.cs, ProcessExitInfo.cs, ProcessIdentity.cs | Untracked, in Models/ |
| **ProcessTracking tests** | 6 test files | Untracked, in Tests/ProcessTracking/ |
| **Architecture docs** | 35 documents | Untracked, in docs/architecture/ |
| **Research docs** | 55 documents | Untracked, in docs/research/ |
| **Interop** | SafeJobHandle.cs | Untracked, in Interop/ |

---

## 3. Repository Structure

### Solution

```
src/MultiSeat.slnx (3 projects)
├── MultiSeat.Shared      — Domain layer (.NET 9 class library)
├── MultiSeat.Service     — Infrastructure layer (.NET 9 Windows Service)
└── MultiSeat.Tests       — xUnit test suite (.NET 9)
```

Plus optional:
- `MultiSeat.Dashboard` — React + TypeScript (Vite build)
- `MultiSeat.InputHook` — C++/CMake DLL (optional, currently inert)

### Project Purposes

| Project | Purpose | Dependencies |
|---------|---------|--------------|
| **MultiSeat.Shared** | Domain models, constants, interfaces | None (pure .NET) |
| **MultiSeat.Service** | Windows Service, API, all infrastructure | MultiSeat.Shared + Windows APIs |
| **MultiSeat.Tests** | Unit + integration tests | Both projects + xUnit |

### Key Source Folders

| Folder | Purpose |
|--------|---------|
| `Sessions/` | SeatManager, SessionLauncher, ProcessInjector, RDP management |
| `Streaming/` | ApolloManager, ApolloConfigBuilder, FirewallManager, PortAllocator |
| `Display/` | VirtualDisplayManager, AdvancedColorHelper, ResolutionNegotiator |
| `Audio/` | AudioRouter, AudioCaptureHelper, VoiceMeeterConfigurator |
| `Input/` | ControllerManager, InputRouter, HidHideConfigurator, InputHookManager |
| `Monitoring/` | SessionHealthCheck, ApolloServerQuery, GpuMonitor |
| `Accounts/` | AccountManager (Windows local account CRUD) |
| `Configuration/` | MultiSeatOptions, SeatPresetStore |
| `Api/` | ASP.NET Core Minimal API endpoints |
| `Interop/` | Windows P/Invoke declarations |
| `ProcessTracking/` | **UNTRACKED** — Job Objects, process monitoring |
| `Storage/` | SharedLibraryProvisioner, SecureFile |
| `Diagnostics/` | HidHideInspector, LogFilterInspector |
| `Emulators/` | IEmulatorConfigSeeder, RetroArchConfigSeeder |

---

## 4. Historical Architecture Evolution

### Phase Timeline

Based on git history, the project evolved through these phases:

1. **Original MultiSeat** — Basic multi-seat with Apollo, VB-CABLE, VoiceMeeter
2. **Per-session audio** (`8962efb`) — RDP Remote Audio endpoint isolation (#10, #12)
3. **Display isolation** (`25ce1b5`, `094d51b`) — SudoVDA virtual display management (#15, #16)
4. **Input isolation** (`5a58a6d`) — HidHide session jail for gamepad isolation
5. **Security hardening** (`949eeb0`, `dae3cc7`, `c97b033`) — Standard users, credential locking, API auth
6. **Testing infrastructure** (`0a182f8` onwards) — CI, unit tests, smoke tests
7. **MoonlightWeb integration** (`cadc08c`) — Four fixes for MoonlightWeb compatibility
8. **Interface extraction** (`8fefd3c`, `5f03be0`) — ISessionLauncher, IVirtualDisplayManager

### Key Architectural Decisions (from git history)

| Decision | Commit | Rationale |
|----------|--------|-----------|
| Per-session audio (no VAC) | `8962efb` | RDP Remote Audio endpoint gives true isolation without VB-CABLE/VoiceMeeter |
| SudoVDA for virtual displays | `25ce1b5` | Vibepollo manages display lifecycle via output_name in config |
| HidHide session jail | `5a58a6d` | Undocumented HidHide feature: blacklist entries suffixed with !sessionId |
| Standard user seats | `949eeb0` | Seats don't need admin — SudoVDA IPC works with user-level tokens |
| API key authentication | `243462c` | WebSocket broadcasts seat state — must be authenticated |
| RDP loopback (127.0.0.2) | Original | mstsc connects to loopback for session creation |

---

## 5. Existing Interfaces

### Tracked Interfaces

| Interface | Location | Purpose | Implementor | Introduced |
|-----------|----------|---------|-------------|------------|
| `ISessionLauncher` | MultiSeat.Service/Sessions/ | RDP session lifecycle | SessionLauncher | `8fefd3c` |
| `IVirtualDisplayManager` | MultiSeat.Service/Display/ | Display lifecycle | VirtualDisplayManager | `5f03be0` |
| `IEmulatorConfigSeeder` | MultiSeat.Service/Emulators/ | Emulator config seeding | RetroArchConfigSeeder | Original |

### Untracked Interfaces (ProcessTracking work)

| Interface | Location | Purpose | Implementor | Status |
|-----------|----------|---------|-------------|--------|
| `IProcessGroup` | MultiSeat.Shared/ | Job Object abstraction | WindowsProcessGroup | Untracked |
| `IProcessGroupManager` | MultiSeat.Shared/ | Per-seat group lifecycle | WindowsProcessGroupManager | Untracked |
| `IProcessMonitor` | MultiSeat.Shared/ | Process exit monitoring | WindowsProcessMonitor | Untracked |
| `IProcessTracker` | MultiSeat.Shared/ | Ownership tracking | WindowsProcessTracker | Untracked |
| `IProviderLifecycleConsumer` | MultiSeat.Shared/ | Provider crash recovery | SeatManager | Untracked |

### Interface Extraction Status

**ISessionLauncher** — Extracted but **partially wired**:
- ✅ Interface created
- ✅ SessionLauncher implements it
- ✅ ApiServer registers it in DI
- ✅ SeatEndpoints uses it in 2 lambdas
- ❌ SeatManager still uses concrete `SessionLauncher` (field + constructor)
- ❌ SessionHealthCheck still uses concrete `SessionLauncher`

**IVirtualDisplayManager** — Extracted and **fully wired**:
- ✅ Interface created
- ✅ VirtualDisplayManager implements it
- ✅ SeatManager uses it (field + constructor)
- ✅ SystemEndpoints uses it
- ✅ ApiServer and Program register it

---

## 6. Existing Services

### SeatManager Dependencies (18 total)

| Dependency | Type | Has Interface | Consumers |
|------------|------|---------------|-----------|
| `AccountManager` | Concrete | ❌ | SeatManager, MultiSeatWorker |
| `SessionLauncher` | Concrete | ✅ ISessionLauncher (partial) | SeatManager, SessionHealthCheck, SeatEndpoints, ProcessInjector |
| `ProcessInjector` | Concrete | ❌ | SeatManager |
| `VirtualDisplayManager` | Concrete | ✅ IVirtualDisplayManager | SeatManager, SystemEndpoints |
| `ApolloManager` | Concrete | ❌ | SeatManager, SessionHealthCheck |
| `ApolloConfigBuilder` | Concrete | ❌ | SeatManager |
| `PortAllocator` | Concrete | ❌ | SeatManager |
| `FirewallManager` | Concrete | ❌ | SeatManager, MultiSeatWorker |
| `AudioRouter` | Concrete | ❌ | SeatManager |
| `ControllerManager` | Concrete | ❌ | SeatManager |
| `InputRouter` | Concrete | ❌ | SeatManager, MultiSeatWorker |
| `InputHookManager` | Concrete | ❌ | SeatManager, MultiSeatWorker |
| `HidHideConfigurator` | Concrete | ❌ | SeatManager, MultiSeatWorker |
| `OnConnectAppLauncher` | Concrete | ❌ | SeatManager, SessionHealthCheck |
| `ApolloServerQuery` | Concrete | ❌ | SeatManager |
| `IEmulatorConfigSeeder` | Interface | ✅ | SeatManager (IEnumerable) |

### Service Responsibilities

| Service | Responsibility | Windows Dependencies |
|---------|---------------|---------------------|
| **SeatManager** | Top-level orchestrator — provision, teardown, recovery | None directly (delegates) |
| **SessionLauncher** | RDP loopback session creation via CreateProcessAsUser | WTS API, Kernel32, User32 |
| **ProcessInjector** | Process creation inside seat sessions | CreateProcessAsUser, WTS API |
| **VirtualDisplayManager** | SudoVDA display tracking + diagnostics | PnP registry, ProcessInjector |
| **ApolloManager** | Apollo process lifecycle (start/stop/restart) | Process lifecycle |
| **ApolloConfigBuilder** | sunshine.conf generation per seat | File I/O |
| **PortAllocator** | 30-port block allocation | None |
| **FirewallManager** | Windows Firewall rules | netsh |
| **AudioRouter** | VAC cable assignment (SharedHost mode) | Audio endpoints |
| **ControllerManager** | ViGEm virtual controller lifecycle | ViGEmBus driver |
| **InputRouter** | XInput physical → ViGEm assignment | XInput API |
| **HidHideConfigurator** | HidHide cloak/uncloak | HidHideCLI.exe |
| **InputHookManager** | Keyboard/mouse hook management | InputHook DLL |
| **SessionHealthCheck** | 5s periodic health probe | Process alive checks |
| **ApolloServerQuery** | HTTP serverinfo query | HTTP client |
| **AccountManager** | Windows local account CRUD | NetApi32, AdvApi32 |

---

## 7. Existing Tests

### Test Baseline (tracked code only)

```
Build:     ✅ 0 errors, 1 warning (pre-existing CS1998)
Tests:     383 passed, 17 skipped, 0 failed
```

### Test Distribution

| Category | Count | Notes |
|----------|-------|-------|
| Sessions | ~40 | SeatManager, SessionLauncher, RdpFileBuilder, PortAllocator |
| Streaming | ~20 | ApolloLogParser, StreamingTests, ApolloConfigBuilder |
| Input | ~20 | HidHide parser, session jail, controller, InputRouter |
| Accounts | ~10 | AccountManager |
| Storage | ~5 | SharedLibraryProvisioner |
| Configuration | ~10 | SeatPresetStore, MultiSeatOptions |
| Diagnostics | ~5 | HidHideInspector, LogFilterInspector |
| Emulators | ~5 | RetroArchConfigSeeder |
| Api | ~10 | ApiServer, SeatEndpoints |
| Integration | ~15 | End-to-end (mostly skipped) |
| Other | ~250+ | Various unit tests |

### Skipped Tests (17)

All require live Windows environment (real sessions, real HidHide, real ViGEmBus, real GPU). Properly marked as integration tests.

### Test Quality Observations

- Good coverage of configuration parsing (ApolloLogParser, SeatPresetStore)
- Good coverage of input isolation (HidHide session jail, controller routing)
- Good coverage of security (credential store, permission checks)
- Missing: ProcessTracking tests (untracked), provider lifecycle mocking
- Missing: SeatManager unit tests with mocked dependencies

---

## 8. ProcessTracking Historical State

### What Exists (untracked)

The ProcessTracking subsystem is a significant body of work that has NOT been committed:

**Interfaces** (in MultiSeat.Shared):
- `IProcessGroup` — Job Object abstraction with invariant documentation
- `IProcessGroupManager` — Per-seat group lifecycle management
- `IProcessMonitor` — Event-driven process exit monitoring (replaces polling)
- `IProcessTracker` — Centralized process ownership tracking
- `IProviderLifecycleConsumer` — Contract for provider crash recovery

**Implementations** (in MultiSeat.Service/ProcessTracking):
- `WindowsProcessGroup` — Job Object wrapper with KILL_ON_JOB_CLOSE
- `WindowsProcessGroupManager` — Per-seat group dictionary
- `WindowsProcessMonitor` — WaitForSingleObject-based exit detection
- `WindowsProcessTracker` — ConcurrentDictionary-based ownership store
- `StartupOrphanDetector` — WMI-based residual process detection

**Models** (in MultiSeat.Shared/Models):
- `ProcessIdentity` — PID + StartedAt value object (PID reuse protection)
- `ManagedProcess` — Ownership record with identity, seat, type
- `ManagedProcessType` — Enum: Provider, Game, Helper, Other
- `ProcessExitInfo` — Exit event value object with identity + exit code

**Interop**:
- `SafeJobHandle` — SAFE_HANDLE for Job Objects

**Tests** (in MultiSeat.Tests/ProcessTracking):
- 6 test files covering ProcessGroup, ProcessMonitor, ProcessTracker, RecoveryGate, GameProcessTracking, GameExitIsolation

### Build Status with ProcessTracking

**Compilation errors** (9 errors):
1. `WindowsProcessGroup.cs` — References `Kernel32.CreateJobObjectW`, `AssignProcessToJobObject`, `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, etc. — these types/members do NOT exist in the current tracked `Kernel32.cs`
2. `StartupOrphanDetector.cs` — References `MultiSeatOptions.VibepolloExePath` — this property does NOT exist in the current tracked `MultiSeatOptions` (uses `ApolloExePath`)

### Root Cause

The ProcessTracking work was developed against a **different version of the codebase** — likely the traycer branch which has `VibepolloExePath` and may have extended `Kernel32.cs`. The work was never rebased onto the current master HEAD.

### Historical Intent

The ProcessTracking subsystem was designed to:
1. Replace polling-based crash detection with event-driven monitoring
2. Provide per-seat process ownership tracking with PID reuse protection
3. Guarantee cleanup via Job Objects (KILL_ON_JOB_CLOSE)
4. Support residual process adoption after service restart

This aligns with the untracked architecture docs which describe:
- Process Recovery with progressive backoff
- State machines for ProviderInstance, GameProcess
- Invariant I15: "Every managed process has an owner"

---

## 9. Documentation State

### Tracked Documentation

| Document | Status | Notes |
|----------|--------|-------|
| `README.md` | Current | Comprehensive, references Apollo (not Vibepollo) |
| `REQUIREMENTS.md` | Current | Hardware/software requirements |
| `SPEC.md` | Current | Phase status, roadmap (references Phase 1-6 on traycer) |
| `TODO.md` | Current | Task tracking |
| `CLAUDE.md` | Current | Codebase guide for AI agents |
| `LICENSE.md` | Current | MIT |
| `docs/security-posture.md` | Current | Security analysis |

### Untracked Documentation

| Category | Count | Status |
|----------|-------|--------|
| `docs/architecture/` | 35 files | FROZEN status, dated 2026-08-30 |
| `docs/research/` | 55 files | Research and analysis |
| `docs/design/` | 2 files | Per-session audio design |
| `docs/audits/` | 1 file | Previous audit (stale) |

### Documentation Quality

The architecture docs are **comprehensive but aspirational** — they describe target architecture (Clean Architecture layers, Provider SDK, Provider Host) that does not yet exist in code. Key documents:

- **ARCHITECTURE-BASELINE.md** — Describes 5-layer architecture (Shared → Application → Infrastructure → Provider.SDK → Provider.Host) — only Shared and Infrastructure exist
- **SEAT-AGGREGATE.md** — Well-documented aggregate with 16 invariants — mostly enforced by code
- **STATE-MACHINES.md** — Detailed state machines for Seat, Provider, Session, Game, Display — current code has simpler state enums
- **PROVIDER-CONTRACT.md** — Conceptual provider contract — no C# interface exists
- **PROCESS-RECOVERY.md** — Recovery design with backoff, orphan detection — partially implemented

### Documentation vs Code Contradictions

| Document Claims | Code Reality | Status |
|-----------------|--------------|--------|
| "VibepolloManager" | "ApolloManager" (master) | Contradiction — traycer renamed |
| "VibepolloConfigBuilder" | "ApolloConfigBuilder" (master) | Contradiction |
| "5-layer architecture" | 2-layer (Shared + Service) | Aspirational |
| "IStreamingProvider" | Does not exist | Aspirational |
| "ProcessTracker (target)" | Untracked, not compiled | In progress |
| "Job Objects (target)" | Untracked, not compiled | In progress |

### Previous Audit Staleness

The previous audit (`MSE-Initial-Repository-Audit.md`) was committed at HEAD `8fefd3c` but references `efa62dc` (traycer branch) as HEAD. It describes VibepolloManager/VibepolloConfigBuilder which don't exist on master. The audit is **factually incorrect for the current master branch**.

---

## 10. RFC-MSE-0001 Gap Analysis

| RFC Concept | Current Project | Status | Notes |
|-------------|----------------|--------|-------|
| **SeatSpec** | `SeatRequest` | PARTIAL | Mutable init-only DTO, not immutable specification. No separation of user input from resolved defaults. |
| **SessionRequest** | Embedded in `SeatRequest` | PARTIAL | Session params (resolution, fps) mixed into seat request. No separate session specification. |
| **ApplicationLaunchRequest** | `LaunchAppRequest` | EXISTS | Clean value object with ExecutablePath, Arguments, WorkingDirectory. |
| **ClientSessionProfile** | — | MISSING | No per-client profiles or capability negotiation. |
| **VirtualDisplayHandle** | `seat.DisplayDevicePath` (string) | PARTIAL | Raw string, not typed value object. No handle semantics. |
| **ProviderInstanceId** | `seat.ApolloProcessId` (int) | PARTIAL | Raw int PID. `ProcessIdentity` exists but unused for provider instances. |
| **Capability model** | — | MISSING | No feature discovery. Provider capabilities hardcoded in config builder. |
| **Desired State** | Implicit in `SeatRequest` | PARTIAL | No explicit desired-state model. Resolution/fps are desired but not separated. |
| **Observed State** | `SeatInfo` + `SeatServices` | EXISTS | Rich observable state with per-subsystem health booleans. |
| **Reconciler** | `SessionHealthCheck` | PARTIAL | Ad-hoc reconciliation in health check loop. Not a formal Reconciler pattern. |
| **Provider lifecycle** | `ApolloManager` (concrete) | EXISTS (concrete) | Functional but not abstracted. Single provider, no interface. |
| **Provider contract** | Documented in PROVIDER-CONTRACT.md | DOCUMENTED ONLY | Conceptual contract in docs, no C# interface. |
| **Provider Host** | — | MISSING | No hosting abstraction. Provider runs in-service. |
| **Failure domains** | — | PARTIAL | Seat isolation exists (separate sessions/port blocks). No formal failure domain boundaries. |
| **Recovery** | `SessionHealthCheck` + `ApolloManager` | PARTIAL | Auto-restart exists. No progressive backoff, no orphan adoption, no game crash recovery. |

### Summary

The project has strong **runtime capabilities** (session management, display/audio/input isolation, health checks) but lacks formal **domain contracts** (SeatSpec, capability model, provider interface). The untracked ProcessTracking work addresses process ownership and cleanup but is not integrated.

---

## 11. Duo / Vibepollo / Provider Readiness

### Existing MultiSeat-Extended Functionality

| Capability | Status | Notes |
|------------|--------|-------|
| Multi-seat provisioning | ✅ Working | Up to MaxSeats concurrent sessions |
| Per-session audio | ✅ Working | RDP Remote Audio, no VAC needed |
| Virtual display | ✅ Working | SudoVDA via Vibepollo/Apollo |
| Gamepad isolation | ✅ Working | HidHide session jail |
| Firewall management | ✅ Working | Per-seat port rules |
| Health monitoring | ✅ Working | 5s interval, auto-restart |
| Dashboard | ✅ Working | React/TypeScript web UI |
| API authentication | ✅ Working | API key + WebSocket auth |
| Credential security | ✅ Working | DPAPI, ACL hardening |
| Shared game library | ✅ Working | Cross-seat Steam/ROM access |
| Emulator netplay | ✅ Working | Per-seat RetroArch ports |

### Vibepollo Integration (from traycer branch)

The traycer branch has already renamed Apollo→Vibepollo and added:
- Vibepollo advanced features (Playnite, RTSS, Lossless Scaling, HDR)
- Per-seat config generation (sunshine.conf)
- Log parsing for display detection
- Server health queries

**What would be needed for full Vibepollo integration on master**:
1. Merge traycer branch changes (Phase 1-6)
2. Or cherry-pick the rename + feature commits
3. Update all references from Apollo→Vibepollo
4. Update install scripts for Vibepollo download URLs
5. Test multi-instance Vibepollo behavior

### TermWrap Readiness

The traycer branch (Phase 6) has already migrated from RDPWrap to TermWrap:
- Downloads TermWrap v0.6 release zip
- Deploys TermWrap.dll, UmWrap.dll, EndpWrap.dll, Zydis.dll
- Imports registry entries to repoint ServiceDll
- Restarts TermService

**On master**: RDPWrap is still used. TermWrap integration would require merging Phase 6 from traycer.

### VDD / SudoVDA Display Abstraction

Current state on master:
- `VirtualDisplayManager` tracks SudoVDA displays per seat
- `IVirtualDisplayManager` interface extracted
- Display lifecycle managed by Apollo/Vibepollo via output_name config
- No separate VDD integration needed — Vibepollo handles it

**What exists for display abstraction**:
- `VirtualDisplay` record (SeatId, DevicePath, Width, Height, Fps, CreatedAt)
- `ResolutionNegotiator` for resolution/fps negotiation
- `AdvancedColorHelper` for HDR diagnostics
- `DisplayEnumeratorHelper` for display enumeration
- `IVirtualDisplayManager` interface for dependency inversion

---

## 12. Architectural Debt

### A — Critical (Blocks development)

1. **ProcessTracking not integrated** — 9 compilation errors from untracked files referencing missing Kernel32 types and non-existent `VibepolloExePath` property. Cannot build with ProcessTracking files present.

2. **ISessionLauncher partially wired** — Interface exists but SeatManager and SessionHealthCheck still depend on concrete `SessionLauncher`. The extraction is incomplete.

3. **Branch divergence** — master and traycer have diverged by 25+ commits. Key architectural changes (Vibepollo rename, audio architecture, TermWrap) are only on traycer.

### B — Important (Should fix before next major phase)

4. **13 concrete service dependencies in SeatManager** — SeatManager depends on 13 concrete classes without interfaces. This makes unit testing impossible without integration tests.

5. **No provider abstraction** — ApolloManager is the sole provider, tightly coupled. The PROVIDER-CONTRACT.md describes the target but no C# interface exists.

6. **Stale audit documentation** — Previous audit references traycer branch state (Vibepollo names) but was committed to master (Apollo names).

7. **Missing game crash recovery** — ProcessMonitor exists conceptually but game exit handling is observational only (no auto-restart).

8. **No progressive backoff** — Crash recovery uses immediate restart without backoff. PROCESS-RECOVERY.md documents the target but it's not implemented.

### C — Future (Does not block current work)

9. **No seat state persistence** — SeatInfo is in-memory only. Service restart loses all seat state.

10. **No Clean Architecture layers** — ARCHITECTURE-BASELINE.md describes 5 layers but only 2 exist (Shared + Service).

11. **No Provider SDK/Host** — Target architecture has Provider.SDK and Provider.Host projects. Not yet created.

12. **RdpGeometry in Service layer** — Value object lives in MultiSeat.Service, preventing interfaces from being defined in Shared.

13. **Audio architecture mismatch** — Code has both SharedHost (VAC) and PerSession modes. Traycer removed SharedHost. Master still has it.

---

## 13. Previous Work / Intended Direction

### What the Previous Developer/Agent Was Building

Based on git history, untracked files, and architecture docs, the intended direction was:

1. **Process Tracking Subsystem** (untracked) — The most significant uncommitted work:
   - Event-driven process monitoring (replacing polling)
   - Per-seat ownership tracking with PID reuse protection
   - Job Object cleanup guarantees
   - Residual process adoption after restart
   - Provider lifecycle consumer pattern

2. **Provider Abstraction** (documented, not implemented):
   - `IStreamingProvider` interface (from PROVIDER-CONTRACT.md)
   - Provider adapter pattern (VibepolloAdapter, ApolloAdapter, SunshineAdapter)
   - Provider Host project for hosting abstraction

3. **Clean Architecture Migration** (documented, not implemented):
   - MultiSeat.Application layer (use cases)
   - MultiSeat.Provider.SDK (contract)
   - MultiSeat.Provider.Host (hosting)

4. **Vibepollo Integration** (on traycer branch):
   - Apollo→Vibepollo rename
   - Advanced features (Playnite, RTSS, Lossless Scaling, HDR)
   - TermWrap migration

5. **Security Hardening** (on master):
   - Credential locking
   - Permission hardening
   - API authentication

### Evidence of Intended Next Steps

- **ProcessTracking interfaces** define the contract for process ownership and monitoring
- **Architecture docs** describe state machines and recovery patterns
- **IProviderLifecycleConsumer** interface exists but has no implementation path yet
- **PROCESS-RECOVERY.md** documents backoff, orphan detection, game crash recovery
- **STATE-MACHINES.md** defines Degraded/Recovering/Failed states not yet in code

---

## 14. Recommended Next Small Step

### Complete the ISessionLauncher Wiring

**Why this is the safest next step:**

1. **Already started** — ISessionLauncher interface exists, SessionLauncher implements it, ApiServer/SeatEndpoints use it. Only SeatManager and SessionHealthCheck need updating.

2. **Trivial change** — 4 lines total (2 field declarations + 2 constructor parameters)

3. **No logic changes** — Pure type substitution, same pattern as IVirtualDisplayManager

4. **No external dependencies** — No drivers, no providers, no TermWrap

5. **Follows established pattern** — Exactly what IVirtualDisplayManager extraction did

6. **Reduces architectural debt** — Moves from 13 to 12 concrete dependencies in SeatManager

7. **Enables future testing** — SeatManager can be tested with mocked ISessionLauncher

**What NOT to do:**
- Do NOT merge the traycer branch
- Do NOT integrate ProcessTracking
- Do NOT create new interfaces beyond completing ISessionLauncher wiring
- Do NOT refactor SeatManager
- Do NOT change behavior

---

## 15. Explicitly Out of Scope

This audit does NOT:

- Implement RFC-MSE-0001
- Create Provider SDK or Provider Host
- Merge the traycer branch
- Integrate ProcessTracking
- Add new interfaces beyond completing ISessionLauncher
- Change existing behavior
- Modify untracked files
- Fix ProcessTracking compilation errors
- Redesign the state machine
- Add seat state persistence
- Create Clean Architecture layers

---

## Evidence

| Section | Source | Status |
|---------|--------|--------|
| Git history | `git log --graph --all` | FACT |
| Branch divergence | `git log master --not traycer` | FACT |
| Project structure | File system exploration | FACT |
| Interface inventory | `find -name "I*.cs"` | FACT |
| Service dependencies | SeatManager constructor | FACT |
| Test baseline | `dotnet test` output | FACT |
| ProcessTracking state | File inspection + build errors | FACT |
| Documentation state | File reading + comparison | FACT |
| RFC gaps | Code vs RFC concept comparison | ANALYSIS |
| Debt assessment | Pattern analysis | RECOMMENDATION |
