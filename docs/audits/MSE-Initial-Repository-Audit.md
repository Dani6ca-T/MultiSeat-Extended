# MultiSeat-Extended — Initial Repository Audit

**Date**: 2026-08-31
**Auditor**: Buffy (Codebuff)
**Branch**: `master`
**HEAD**: `efa62dc` (feat: Phase 6 - migrate RDPWrap to TermWrap)

---

## 1. Current Architecture

MultiSeat-Extended is a **Windows-first multi-seat orchestration platform** that enables multiple simultaneous Moonlight game-streaming sessions on a single Windows host. Each seat gets an isolated Windows user account, RDP session, virtual display (SudoVDA), per-session audio endpoint, and a dedicated Vibepollo streaming instance.

### Architecture Style

- **Single-process Windows Service** (SYSTEM) with embedded ASP.NET Core Minimal API
- **No formal Clean Architecture layers** — Shared/Service split exists but no Application layer
- **Concrete class dependencies** — SeatManager depends on 18+ concrete services (no abstractions)
- **Event-driven recovery** — ProcessMonitor raises events, SeatManager handles them
- **Best-effort teardown** — Job Objects as safety net cleanup

### Key Design Decisions

1. Per-session audio (RDP Remote Audio endpoint) — no VAC drivers needed
2. Vibepollo (Sunshine fork) as the streaming provider — single provider, no abstraction
3. RDP loopback (mstsc → 127.0.0.2) for session creation
4. TermWrap for multi-session RDP patching
5. HidHide session jail for gamepad isolation (undocumented feature)
6. SudoVDA (IddCx) for virtual displays — one per seat

---

## 2. Project Structure

```
src/
├── MultiSeat.slnx                    — Solution (3 projects)
├── MultiSeat.Shared/                  — Domain layer (.NET 9 class library)
│   ├── Constants.cs                   — Port layout, paths, limits
│   ├── Models/                        — Domain entities & value objects
│   │   ├── SeatInfo.cs               — Seat aggregate (runtime state)
│   │   ├── SeatRequest.cs            — Provisioning input
│   │   ├── SeatServices.cs           — Per-subsystem health status
│   │   ├── SeatPreset.cs             — Persisted autostart config
│   │   ├── AccountInfo.cs            — Windows account metadata
│   │   ├── HostVibepolloInfo.cs      — Standalone Vibepollo detection
│   │   ├── ManagedProcess.cs         — Process ownership record
│   │   ├── ManagedProcessType.cs     — Process role enum
│   │   ├── ProcessExitInfo.cs        — Exit event value object
│   │   ├── ProcessIdentity.cs        — PID + StartedAt (PID reuse protection)
│   │   ├── SeatAppProfile.cs         — Per-app launch config
│   │   └── SystemStatus.cs           — Host-level status
│   ├── IProcessGroup.cs              — Job Object abstraction (interface)
│   ├── IProcessGroupManager.cs       — Per-seat group management (interface)
│   ├── IProcessMonitor.cs            — Process exit monitoring (interface)
│   ├── IProcessTracker.cs            — Ownership tracking (interface)
│   └── IProviderLifecycleConsumer.cs — Provider crash recovery (interface)
│
├── MultiSeat.Service/                 — Infrastructure layer (.NET 9 Windows Service)
│   ├── Program.cs                     — DI composition root + CLI helper modes
│   ├── MultiSeatWorker.cs            — BackgroundService (health loop, auto-provision)
│   ├── Accounts/
│   │   └── AccountManager.cs         — Windows local account CRUD
│   ├── Api/
│   │   ├── ApiServer.cs              — ASP.NET Core Minimal API composition
│   │   ├── SeatEndpoints.cs          — Seat CRUD + management endpoints
│   │   ├── AccountEndpoints.cs       — Account management endpoints
│   │   ├── SystemEndpoints.cs        — System status endpoints
│   │   ├── HostEndpoints.cs          — Host info endpoints
│   │   ├── InputEndpoints.cs         — Controller routing endpoints
│   │   ├── WebSocketHub.cs           — Real-time seat state broadcasting
│   │   ├── ApiAuthState.cs           — API key authentication state
│   │   └── ApiInputValidation.cs     — Input validation helpers
│   ├── Configuration/
│   │   ├── MultiSeatOptions.cs       — Configuration POCO (appsettings)
│   │   └── SeatPresetStore.cs        — JSON-based seat preset persistence
│   ├── Diagnostics/
│   │   ├── HidHideInspector.cs       — HidHide state diagnostics
│   │   └── LogFilterInspector.cs     — Vibepollo log filter diagnostics
│   ├── Display/
│   │   ├── VirtualDisplayManager.cs  — SudoVDA display attach/detach
│   │   ├── AdvancedColorHelper.cs    — HDR/Advanced Color diagnostics
│   │   ├── DisplayEnumeratorHelper.cs — Display enumeration
│   │   └── ResolutionNegotiator.cs   — Resolution negotiation
│   ├── Emulators/
│   │   ├── IEmulatorConfigSeeder.cs  — Emulator config seeding interface
│   │   └── RetroArchConfigSeeder.cs  — RetroArch netplay config
│   ├── Input/
│   │   ├── ControllerManager.cs      — ViGEm virtual controller lifecycle
│   │   ├── InputRouter.cs            — XInput physical → ViGEm assignment
│   │   ├── InputHookManager.cs       — Keyboard/mouse hook management
│   │   ├── HidHideConfigurator.cs    — HidHide cloak/uncloak
│   │   ├── HidHideCli.cs             — HidHideCLI.exe wrapper
│   │   ├── HidHideDevice.cs          — Device info parsing
│   │   └── HidHideSessionJail.cs     — Session-specific jail rules
│   ├── Interop/                      — Windows P/Invoke declarations
│   │   ├── AdvApi.cs, CredApi.cs, Kernel32.cs, User32.cs, etc.
│   │   └── SafeJobHandle.cs, SafeTokenHandle.cs (SAFE_HANDLE types)
│   ├── Monitoring/
│   │   ├── SessionHealthCheck.cs     — 5s periodic seat health probe
│   │   ├── VibepolloServerQuery.cs   — HTTP serverinfo query
│   │   ├── HostVibepolloMonitor.cs   — Standalone Vibepollo detection
│   │   ├── GpuMonitor.cs             — NVIDIA GPU utilization
│   │   └── MetricsCollector.cs       — System metrics
│   ├── ProcessTracking/
│   │   ├── WindowsProcessGroup.cs    — Job Object wrapper
│   │   ├── WindowsProcessGroupManager.cs — Per-seat group manager
│   │   ├── WindowsProcessMonitor.cs  — Process exit event monitoring
│   │   ├── WindowsProcessTracker.cs  — Ownership tracking store
│   │   └── StartupOrphanDetector.cs  — Pre-existing process detection
│   ├── Sessions/
│   │   ├── SeatManager.cs            — Top-level seat lifecycle orchestrator
│   │   ├── SessionLauncher.cs        — RDP session creation via CreateProcessAsUser
│   │   ├── ProcessInjector.cs        — Process creation inside seat sessions
│   │   ├── SessionLauncher.cs        — RDP loopback session management
│   │   ├── SeatState.cs              — State transition validation
│   │   ├── RdpFileBuilder.cs         — .rdp file generation
│   │   ├── RdpCredentialStore.cs     — DPAPI credential storage
│   │   └── DialogClickHelper.cs      — Mstsc dialog automation
│   ├── Storage/
│   │   ├── SharedLibraryProvisioner.cs — Shared game library setup
│   │   └── SecureFile.cs             — ACL hardening
│   └── Streaming/
│       ├── VibepolloManager.cs       — Vibepollo process lifecycle
│       ├── VibepolloConfigBuilder.cs — sunshine.conf generation
│       ├── VibepolloLogParser.cs     — Log file parsing
│       ├── FirewallManager.cs        — Windows Firewall rules
│       ├── PortAllocator.cs          — Port block allocation
│       ├── OnConnectAppLauncher.cs   — App launch on client connect
│       ├── ClientResolutionFollower.cs — Resolution follow
│       ├── SeatAppManager.cs         — Per-seat app profiles
│       └── SeatAppManagerAccessor.cs — DI accessor
│
├── MultiSeat.Tests/                   — xUnit test suite (.NET 9)
│   ├── Accounts/, Api/, Diagnostics/, Emulators/, Input/
│   ├── Integration/                   — End-to-end tests (mostly skipped on non-Windows)
│   ├── ProcessTracking/              — Process tracking unit tests
│   ├── Sessions/                     — Session + seat lifecycle tests
│   ├── Storage/                      — Storage tests
│   └── Streaming/                    — Streaming + config tests
│
├── MultiSeat.Dashboard/              — React + TypeScript (Vite build)
└── MultiSeat.InputHook/              — C++/CMake DLL (optional, currently inert)

docs/
├── architecture/                     — 35 architecture documents (FROZEN)
├── design/                           — Per-session audio design docs
├── research/                         — 55 research documents
├── audits/                           — This audit (new)
└── security-posture.md               — Security analysis

scripts/                             — PowerShell operational scripts
prerequisites/                       — Installer scripts
```

---

## 3. Existing Contracts

### Interfaces in MultiSeat.Shared

| Interface | Purpose | Status |
|-----------|---------|--------|
| `IProcessGroup` | Job Object abstraction | Implemented (WindowsProcessGroup) |
| `IProcessGroupManager` | Per-seat group lifecycle | Implemented (WindowsProcessGroupManager) |
| `IProcessMonitor` | Event-driven process exit | Implemented (WindowsProcessMonitor) |
| `IProcessTracker` | Ownership tracking | Implemented (WindowsProcessTracker) |
| `IProviderLifecycleConsumer` | Provider crash recovery | Implemented (SeatManager) |

### Interfaces in MultiSeat.Service

| Interface | Purpose | Status |
|-----------|---------|--------|
| `IEmulatorConfigSeeder` | Emulator config seeding | Implemented (RetroArchConfigSeeder) |

### Concrete Services (no interfaces)

| Service | Used By | Missing Interface |
|---------|---------|-------------------|
| `SessionLauncher` | SeatManager, SessionHealthCheck, SeatEndpoints, ProcessInjector | ✅ `ISessionLauncher` (fully wired, except ProcessInjector) |
| `VirtualDisplayManager` | SeatManager, SeatEndpoints | ✅ `IVirtualDisplayManager` (extracted) |
| `VibepolloManager` | SeatManager, SessionHealthCheck, HostEndpoints | `IVibepolloManager` |
| `PortAllocator` | SeatManager | `IPortAllocator` |
| `FirewallManager` | SeatManager, MultiSeatWorker | `IFirewallManager` |
| `ControllerManager` | SeatManager | `IControllerManager` |
| `InputRouter` | SeatManager, MultiSeatWorker | `IInputRouter` |
| `InputHookManager` | SeatManager, MultiSeatWorker | `IInputHookManager` |
| `HidHideConfigurator` | SeatManager, MultiSeatWorker | `IHidHideConfigurator` |
| `AccountManager` | SeatManager, MultiSeatWorker | `IAccountManager` |
| `VibepolloConfigBuilder` | SeatManager | `IVibepolloConfigBuilder` |
| `OnConnectAppLauncher` | SeatManager, SessionHealthCheck | `IOnConnectAppLauncher` |
| `ProcessInjector` | SeatManager | `IProcessInjector` |

### Domain Models

| Model | Type | Location |
|-------|------|----------|
| `SeatInfo` | Aggregate root | MultiSeat.Shared |
| `SeatRequest` | Input DTO | MultiSeat.Shared |
| `SeatServices` | Health status | MultiSeat.Shared |
| `SeatPreset` | Persisted config | MultiSeat.Shared |
| `SeatStatus` | Status enum | MultiSeat.Shared |
| `NvencQualityPreset` | Preset enum | MultiSeat.Shared |
| `ProcessIdentity` | Value object (PID + StartedAt) | MultiSeat.Shared |
| `ManagedProcess` | Domain record | MultiSeat.Shared |
| `ProcessExitInfo` | Event value object | MultiSeat.Shared |
| `RdpGeometry` | Value object | MultiSeat.Service |

---

## 4. Existing Providers

### Streaming Provider

| Provider | Status | Notes |
|----------|--------|-------|
| Vibepollo (Sunshine fork) | **Active, sole provider** | v1.18.4-stable.3 |
| Apollo (predecessor) | Renamed to Vibepollo | Phase 1 complete |
| Sunshine (upstream) | Not used | Vibepollo is the active fork |

### Provider Abstraction

**There is NO provider abstraction interface.** `VibepolloManager` is a concrete class directly used by `SeatManager`. The conceptual provider contract is documented in `docs/architecture/PROVIDER-CONTRACT.md` but has no corresponding C# interface.

The `IProviderLifecycleConsumer` interface exists but is a consumer contract (for crash handling), not a provider contract.

---

## 5. Current Lifecycle

### Seat Provisioning Pipeline

```
SeatManager.ProvisionSeatAsync(SeatRequest)
    │
    ├── 1. Validate capacity + account exists
    ├── 2. Allocate port block (PortAllocator)
    ├── 3. Launch RDP session (SessionLauncher.LaunchSessionAsync)
    ├── 2.5. Suppress RustDesk audio (file write + process kill)
    ├── 2.7. Pre-write HidHide jail rules (best-effort)
    ├── 4. Create virtual display (VirtualDisplayManager.CreateDisplayAsync)
    ├── 5. Open firewall ports (FirewallManager.OpenPortsAsync)
    ├── 5. Audio: PerSession (no-op — Windows creates endpoint)
    ├── 5.7. Seed emulator configs (best-effort)
    ├── 6. Start Vibepollo (VibepolloManager.StartAsync)
    ├── 6.5. Discover SudoVDA UUID from Vibepollo log
    ├── 6.6/6.7. Apply display isolation + refresh-rate clamp
    ├── 7. Create ViGEm controller (if enabled)
    ├── 8. HidHide cloaking + keyboard/mouse hooks
    └── 9. Mark Ready, broadcast state
```

### Seat Teardown Pipeline (reverse order, best-effort)

```
TeardownSeatInternalAsync(SeatInfo)
    ├── Forget launched apps
    ├── Stop monitoring all game processes
    ├── Unregister all tracked processes
    ├── Uninstall input hooks
    ├── Uncloak HidHide
    ├── Unassign controllers
    ├── Destroy controllers
    ├── Stop Vibepollo
    ├── Close firewall ports
    ├── Destroy virtual display
    ├── Disconnect session
    ├── Logoff session
    ├── Release port block
    ├── Cleanup Vibepollo config
    └── Dispose process group (KILL_ON_JOB_CLOSE)
```

### Health Check Loop (every 5s)

```
SessionHealthCheck.CheckAllSeatsAsync
    ├── Check 1: Is Windows session alive?
    ├── Check 1b: Is session Active (not Disconnected)?
    │       └── If Disconnected: reconnect + restart Vibepollo
    ├── Check 2: RECONCILIATION — Is Vibepollo still running?
    │       └── If not: delegate to SeatManager.HandleProviderExitedAsync
    ├── Check 3: Launch-on-connect (tail Vibepollo log)
    ├── Follow client resolution (if enabled)
    └── Late SudoVDA detection (if DisplayDevicePath unset)
```

### Provider Crash Recovery

```
ProcessMonitor.ProcessExited (event-driven)
    → VibepolloManager.ProviderExited event
    → SeatManager.OnProviderExited
    → SeatManager.HandleProviderExitedAsync
        ├── Guard: state check (Ready/Streaming only)
        ├── Guard: PID ownership check
        ├── Recovery gate (ConcurrentDictionary)
        └── VibepolloManager.RestartAsync + re-apply display isolation
```

---

## 6. Test Baseline

```
dotnet build src/MultiSeat.slnx
  Result: ✅ SUCCESS (0 errors, 0 warnings)

dotnet test src/MultiSeat.Tests/MultiSeat.Tests.csproj
  Result: 338 passed, 14 skipped, 0 failed
```

### Test Distribution

| Test Category | Count | Notes |
|---------------|-------|-------|
| Sessions | ~30 | SeatManager, SessionLauncher, RdpFileBuilder, etc. |
| Streaming | ~15 | VibepolloLogParser, StreamingTests |
| Input | ~15 | HidHide parser, session jail, controller |
| ProcessTracking | ~30 | ProcessGroup, ProcessMonitor, ProcessTracker |
| Accounts | ~10 | AccountManager |
| Storage | ~5 | SharedLibraryProvisioner |
| Integration | ~10 | End-to-end (mostly skipped) |
| Other | ~220+ | Various unit tests |

### Skipped Tests (14)

All skipped tests require a live Windows environment (real sessions, real HidHide, real ViGEmBus, real GPU). They are properly marked as integration tests.

### Uncommitted Work in Progress

The working tree contains uncommitted ProcessTracking subsystem work:
- **Modified**: Kernel32.cs, SessionHealthCheck.cs, MultiSeatWorker.cs, Program.cs, SeatManager.cs, OnConnectAppLauncher.cs, VibepolloManager.cs
- **Untracked**: ProcessTracking/ (5 implementations), SafeJobHandle.cs, IProcessGroup.cs, IProcessGroupManager.cs, IProcessMonitor.cs, IProcessTracker.cs, IProviderLifecycleConsumer.cs, ManagedProcess.cs, ManagedProcessType.cs, ProcessExitInfo.cs, ProcessIdentity.cs, ProcessTracking tests (6 files)
- **Untracked docs**: docs/architecture/ (35 files), docs/research/ (55 files)

---

## 7. RFC-MSE-0001 Gap Analysis

| RFC Concept | Current Project | Status | Notes |
|-------------|----------------|--------|-------|
| **SeatSpec** | `SeatRequest` | PARTIAL | SeatRequest serves similar purpose but is mutable init-only, not immutable. No separation between "desired state" and "user input". |
| **SessionRequest** | Embedded in `SeatRequest` | PARTIAL | No separate session specification — session params (resolution, fps) are mixed into the seat request. |
| **ApplicationLaunchRequest** | `LaunchAppRequest` | EXISTS | Clean value object with ExecutablePath, Arguments, WorkingDirectory. |
| **ClientSessionProfile** | — | MISSING | No concept of per-client profiles or capabilities. |
| **VirtualDisplayHandle** | `seat.DisplayDevicePath` (string) | PARTIAL | Raw string, not a typed value object. |
| **ProviderInstanceId** | `seat.VibepolloProcessId` (int) | PARTIAL | Raw int PID, not a typed identity. ProcessIdentity exists but is not used for provider instances. |
| **Capability model** | — | MISSING | No capability/provider feature discovery. Vibepollo features are hardcoded in VibepolloConfigBuilder. |
| **Desired State** | Implicit in `SeatRequest` | PARTIAL | No explicit desired-state model separate from the request. |
| **Observed State** | `SeatInfo` + `SeatServices` | EXISTS | Rich observable state with per-subsystem health. |
| **Reconciler** | `SessionHealthCheck` | PARTIAL | Ad-hoc reconciliation logic, not a formal Reconciler pattern. Combines detection + recovery in one class. |
| **Provider lifecycle** | `VibepolloManager` (concrete) | EXISTS (concrete) | Functional but not abstracted. Direct dependency, not behind an interface. |
| **Provider contract** | Documented in PROVIDER-CONTRACT.md | DOCUMENTED ONLY | Conceptual contract exists in docs but no C# interface. |

### Summary

The project has strong **runtime** capabilities (observed state, provider lifecycle, crash recovery) but lacks formal **domain contracts** (SeatSpec, capability model, provider interface). The untracked ProcessTracking work is closing some gaps (IProcessGroup, IProcessMonitor, IProcessTracker) but the provider-level abstraction is still missing.

---

## 8. Recommended First Small Change

### Choice: Extract `ISessionLauncher` interface from `SessionLauncher`

**Rationale**:
- **Priority 1**: Fixes an architectural boundary — 4 services depend on the concrete `SessionLauncher`
- **Established pattern**: The codebase already has 5 interfaces in Shared (IProcessGroup, IProcessGroupManager, IProcessMonitor, IProcessTracker, IProviderLifecycleConsumer) following this exact pattern
- **Low risk**: Pure interface extraction, no logic changes
- **No external dependencies**: No Windows P/Invoke changes needed
- **Testable**: Enables mocking SessionLauncher in seat lifecycle tests
- **Small diff**: ~10 line changes across 6 existing files + 1 new interface file
- **Completable**: One commit

### Implementation Plan

```
Goal: Extract ISessionLauncher interface to enable dependency inversion
Files:
  - NEW: src/MultiSeat.Service/Sessions/ISessionLauncher.cs
  - MODIFY: src/MultiSeat.Service/Sessions/SessionLauncher.cs (add : ISessionLauncher)
  - MODIFY: src/MultiSeat.Service/Sessions/SeatManager.cs (field + constructor)
  - MODIFY: src/MultiSeat.Service/Monitoring/SessionHealthCheck.cs (field + constructor)
  - MODIFY: src/MultiSeat.Service/Api/SeatEndpoints.cs (DI parameters)
  - MODIFY: src/MultiSeat.Service/Api/ApiServer.cs (DI registration)
  - MODIFY: src/MultiSeat.Service/Program.cs (DI registration)
Changes: Interface extraction (no logic changes)
Tests: 338 passed, 14 skipped, 0 failed (identical to baseline)
Documentation: This audit document
Risk: Low — pure interface extraction
Status: ✅ IMPLEMENTED
```

---

## 9. Implementation Results

### ISessionLauncher Interface Extraction

**Status**: ✅ Fully Wired

**What was done**:
1. Created `ISessionLauncher` interface in `src/MultiSeat.Service/Sessions/ISessionLauncher.cs`
2. Made `SessionLauncher` implement `ISessionLauncher`
3. Updated `SeatEndpoints` DI parameters to use `ISessionLauncher`
4. Updated `ApiServer.cs` and `Program.cs` DI registrations to expose both concrete and interface
5. Updated `SeatManager` to depend on `ISessionLauncher` instead of concrete `SessionLauncher`
6. Updated `SessionHealthCheck` to depend on `ISessionLauncher` instead of concrete `SessionLauncher`

**Interface methods**:
- `LaunchSessionAsync(string, CancellationToken, RdpGeometry) → Task<int>`
- `DisconnectSession(int)`
- `LogoffSession(int)`
- `IsSessionAlive(int) → bool`
- `IsSessionActive(int) → bool`
- `RunHelperInSeatSession(int, string, string)`

**What was NOT changed**:
- `ProcessInjector` still depends on concrete `SessionLauncher` (needs `GetSessionToken()` which returns Windows-specific `SafeTokenHandle`)
- No logic changes — pure interface extraction
- No new tests needed — all existing tests pass

**Test results**:
- Before: 383 passed, 17 skipped, 0 failed
- After:  383 passed, 17 skipped, 0 failed

**Follow-up opportunities** (not implemented, documented for future):
- Extract `IProviderManager` interface from `ApolloManager`
- Move `RdpGeometry` to `MultiSeat.Shared` to enable interfaces in the domain layer

### IVirtualDisplayManager Interface Extraction

**Status**: ✅ Implemented

**What was done**:
1. Created `IVirtualDisplayManager` interface in `src/MultiSeat.Service/Display/IVirtualDisplayManager.cs`
2. Made `VirtualDisplayManager` implement `IVirtualDisplayManager`
3. Updated `SeatManager` to depend on `IVirtualDisplayManager` instead of concrete `VirtualDisplayManager`
4. Updated `SystemEndpoints` DI parameter to use `IVirtualDisplayManager`
5. Updated `ApiServer.cs` and `Program.cs` DI registrations to expose both concrete and interface

**Interface methods**:
- `CreateDisplayAsync(SeatInfo, CancellationToken) → Task`
- `DestroyDisplayAsync(SeatInfo, CancellationToken) → Task`
- `IsDriverAvailable { get; } → bool`
- `EnumerateAllConnectedPaths() → IReadOnlyList<object>`

**What was NOT changed**:
- `VirtualDisplay` record stays with the concrete class (internal tracking)
- `GetDisplay(Guid)` and `ActiveDisplayCount` not exposed (no external consumers)
- No logic changes — pure interface extraction
- This is NOT the future `IDisplayProvider` boundary

**Test results**:
- Before: 383 passed, 17 skipped, 0 failed
- After:  383 passed, 17 skipped, 0 failed

**Follow-up opportunities** (not implemented, documented for future):
- Extract `IProviderManager` interface from `VibepolloManager`/`ApolloManager`
- Move `RdpGeometry` to `MultiSeat.Shared` to enable interfaces in the domain layer

---

## Evidence

| Section | Source | Status |
|---------|--------|--------|
| Architecture | Source code analysis | FACT |
| Project structure | File system + solution | FACT |
| Existing contracts | Interface search + grep | FACT |
| Provider status | VibepolloManager + config | FACT |
| Lifecycle | SeatManager + SessionHealthCheck | FACT |
| Test baseline | dotnet test output | FACT |
| RFC gaps | SeatRequest vs SeatSpec comparison | ANALYSIS |
| Recommended change | Pattern analysis + risk assessment | RECOMMENDATION |
