# SeatManager Architecture Audit

**Date**: 2026-08-31
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: d27ad1f
**Source modified**: NO

---

## Executive Summary

`SeatManager` is the top-level orchestrator for the entire seat lifecycle. It holds 17 constructor dependencies (14 concrete classes, 2 interfaces, 1 interface collection), orchestrating every subsystem from account validation through streaming to teardown. The class is ~700 lines and performs 15+ distinct responsibilities — far beyond its intended role as a lifecycle coordinator. The largest coupling problems are: (1) ApolloManager is concrete, blocking provider abstraction, (2) display detection logic is inlined in SeatManager rather than delegated to the display subsystem, and (3) RustDesk audio suppression is embedded in provisioning as ad-hoc system configuration. The highest-value next small change is **extracting the SudoVDA display detection loop** (currently duplicated across provisioning and `TryLateDisplayDetectionAsync`) into the display subsystem, reducing SeatManager by ~80 lines and removing its direct dependence on `ApolloManager.GetLogPath`/`ParseSudoVdaDisplayId` for display UUID discovery.

---

## Constructor Dependencies

```
SeatManager(
    ILogger<SeatManager> logger,                        // logging
    IOptions<MultiSeatOptions> options,                 // configuration
    AccountManager accounts,                            // concrete — Windows account CRUD
    ISessionLauncher sessionLauncher,                   // interface — RDP session lifecycle
    ProcessInjector processInjector,                    // concrete — process creation in sessions
    IVirtualDisplayManager displayManager,              // interface — SudoVDA create/destroy
    ApolloManager apolloManager,                        // concrete — streaming server lifecycle
    ApolloConfigBuilder configBuilder,                  // concrete — sunshine.conf generation
    PortAllocator portAllocator,                        // concrete — port block allocation
    FirewallManager firewall,                           // concrete — netsh firewall rules
    AudioRouter audioRouter,                            // concrete — VAC cable assignment
    ControllerManager controllerManager,                // concrete — ViGEm virtual controllers
    InputRouter inputRouter,                            // concrete — XInput→ViGEm routing
    InputHookManager inputHookManager,                  // concrete — keyboard/mouse hooks
    HidHideConfigurator hidHide,                        // concrete — gamepad session jail
    OnConnectAppLauncher onConnectApps,                 // concrete — app launch on client connect
    Monitoring.ApolloServerQuery serverQuery,            // concrete — Apollo HTTP health probe
    IEnumerable<IEmulatorConfigSeeder> emulatorSeeders  // interface collection — emulator config
)
```

**Count**: 18 parameters, 17 stored fields.

| Category | Count |
|----------|-------|
| Interfaces (ISessionLauncher, IVirtualDisplayManager) | 2 |
| Interface collections (IEnumerable\<IEmulatorConfigSeeder\>) | 1 |
| Concrete classes | 14 |
| Options (IOptions\<MultiSeatOptions\>) | 1 |

---

## Fields

| Field | Type | Purpose |
|-------|------|---------|
| `_seats` | `ConcurrentDictionary<Guid, SeatInfo>` | In-memory seat registry — the only state store |
| `_logger` | `ILogger<SeatManager>` | Structured logging |
| `_options` | `MultiSeatOptions` | Snapshot of bound configuration |
| `_accounts` | `AccountManager` | Windows local account CRUD |
| `_sessionLauncher` | `ISessionLauncher` | RDP loopback session creation |
| `_processInjector` | `ProcessInjector` | Launch processes in target sessions |
| `_displayManager` | `IVirtualDisplayManager` | SudoVDA display attach/detach |
| `_apolloManager` | `ApolloManager` | Apollo process lifecycle |
| `_configBuilder` | `ApolloConfigBuilder` | sunshine.conf generation |
| `_portAllocator` | `PortAllocator` | Port block allocation |
| `_firewall` | `FirewallManager` | Windows Firewall rules |
| `_audioRouter` | `AudioRouter` | VAC cable assignment |
| `_controllerManager` | `ControllerManager` | ViGEm virtual controllers |
| `_inputRouter` | `InputRouter` | XInput→ViGEm routing |
| `_inputHookManager` | `InputHookManager` | Keyboard/mouse session hooks |
| `_hidHide` | `HidHideConfigurator` | Gamepad session jail |
| `_onConnectApps` | `OnConnectAppLauncher` | App launch on CLIENT CONNECTED |
| `_serverQuery` | `Monitoring.ApolloServerQuery` | Apollo HTTP serverinfo probe |
| `_emulatorSeeders` | `IEnumerable<IEmulatorConfigSeeder>` | Per-seat emulator config seeding |

---

## Public Methods

| Method | Lines | Purpose |
|--------|-------|---------|
| `ProvisionSeatAsync` | ~210 | Full provisioning pipeline (9 steps) |
| `TeardownSeatAsync` | 6 | Remove one seat from registry + teardown |
| `TeardownAllAsync` | 3 | Remove all seats (service shutdown) |
| `LaunchAppInSeatAsync` | 12 | Launch an app inside an active seat session |
| `GetSeatServicesAsync` | 30 | Per-subsystem health check for API |
| `StopApollo` | 6 | Kill Apollo without full teardown |
| `StartApolloAsync` | 14 | Start Apollo for existing seat |
| `RestartApolloAsync` | 16 | Stop + start Apollo |
| `TryLateDisplayDetectionAsync` | 45 | Second-chance SudoVDA display discovery |
| `ApplyDisplayIsolationAsync` | 35 | Make SudoVDA primary + shrink RDP |
| `ResetAudio` | 18 | Release + re-assign audio cable |
| `ApplyAudioDefaults` (public+private) | 20 | Re-run capture default in session |
| `SetNvencPresetAsync` | 20 | Change NVENC quality preset |
| `SetResolutionAsync` | 30 | Change seat resolution (reconnect session) |
| `ResetDisplayAsync` | 10 | Destroy + recreate virtual display |
| `ResetController` | 16 | Destroy + recreate ViGEm controller |
| `GetPairedClients` | 4 | List Apollo paired clients |
| `UnpairClient` | 4 | Remove one paired client |
| `UnpairAllClients` | 4 | Remove all paired clients |

**Properties**: `ActiveSeatCount`, `AllSeats`, `ControllerRoutingEnabled`, `InputRouter`, `InputHookManager`, `ApolloManager`

---

## Private Methods

| Method | Purpose |
|--------|---------|
| `TeardownSeatInternalAsync` | Reverse-order teardown with best-effort per step |
| `ApplyAudioDefaults(SeatInfo)` | Private overload for audio capture default |
| `UnassignControllersForSeat` | Remove all XInput assignments for a seat |
| `BroadcastState` | Static wrapper around WebSocketHub.BroadcastSeatUpdateAsync |

---

## Responsibility Map

### Domain (core seat lifecycle)
- Seat creation/deletion/teardown pipeline
- Seat state tracking (`_seats` dictionary)
- Active seat count enforcement
- Seat status transitions (Provisioning → Configuring → Ready → Streaming → TearingDown → Error)

### Application/orchestration
- **ProvisionSeatAsync**: 9-step provisioning pipeline with ordered dependencies
- **TeardownSeatInternalAsync**: Reverse-order teardown
- **LaunchAppInSeatAsync**: In-session app launch
- **SetNvencPresetAsync**: Config change + Apollo restart
- **SetResolutionAsync**: Session reconnect at new geometry
- **ResetDisplayAsync**: Display lifecycle
- **ResetController**: Controller lifecycle
- **ResetAudio**: Audio reassignment
- Broadcast state to WebSocket clients after every mutation

### Infrastructure
- **Port allocation**: `_portAllocator.Allocate()` / `Release()`
- **Firewall**: `_firewall.OpenPortsAsync()` / `ClosePortsAsync()`
- **Config generation**: `_configBuilder.BuildConfig()` / `UpdateDisplayOutput()` / `CleanupConfig()`

### Windows-specific
- **Session creation**: `_sessionLauncher.LaunchSessionAsync()`
- **Process injection**: `_processInjector.LaunchInSessionAsync()`
- **Display isolation**: `ApplyDisplayIsolationAsync()` via helper CLI
- **Account management**: `_accounts.AccountExists()` / `ApplySeatGroupMembership()`
- **RustDesk audio suppression**: File I/O + Process.Kill in ProvisionSeatAsync (ad-hoc)
- **HidHide gamepad jail**: `_hidHide.PreWriteRules()` / `CloakForSession()` / `UncloakForSession()`
- **Input hooks**: `_inputHookManager.InstallForSession()` / `Uninstall()`

### Provider-specific (Apollo)
- **Apollo lifecycle**: `_apolloManager.StartAsync()` / `Stop()` / `IsAlive()` / `RestartAsync()`
- **SudoVDA display detection**: `_apolloManager.ParseSudoVdaDisplayId()` / log reading
- **Apollo config**: `_configBuilder.UpdateDisplayOutput()` / `BuildConfig()`
- **Server health**: `_serverQuery.QueryAsync()` in `GetSeatServicesAsync`
- **Client pairing**: `_configBuilder.GetPairedClients()` / `UnpairClient()`

### Monitoring/recovery
- **SeatServices health**: `GetSeatServicesAsync()` — checks Apollo alive, reachable, streaming, display, audio, controller, hooks, firewall, session
- **Late display detection**: `TryLateDisplayDetectionAsync()` — reads Apollo log for SudoVDA UUID
- **Apollo restart**: `StopApollo()` / `StartApolloAsync()` / `RestartApolloAsync()`

### API concern
- Exposes `InputRouter`, `InputHookManager`, `ApolloManager` properties for API access
- `GetPairedClients` / `UnpairClient` / `UnpairAllClients` — direct pass-through to config builder

---

## Lifecycle Trace

### ProvisionSeatAsync — Full Provisioning Flow

```
┌─ ProvisionSeatAsync ──────────────────────────────────────────┐
│                                                               │
│  [Implemented] Validate capacity (ActiveSeatCount >= Max)      │
│  [Implemented] Validate account exists                         │
│  [Implemented] ApplySeatGroupMembership (idempotent)           │
│  [Implemented] Create SeatInfo, add to _seats, broadcast       │
│                                                               │
│  ── Step 1: Allocate ports ────────────────────────────────── │
│  [Implemented] PortAllocator.Allocate()                        │
│  [Implemented] Set RetroArchNetplayPort if enabled             │
│                                                               │
│  ── Step 2: Launch RDP session ────────────────────────────── │
│  [Implemented] SessionLauncher.LaunchSessionAsync()            │
│  [Implemented] RdpGeometry.ForClient(width, height)            │
│                                                               │
│  ── Step 2.5: RustDesk audio suppression ──────────────────── │
│  [Implemented] File.WriteAllText(RustDesk2.toml)              │
│  [Implemented] Process.GetProcessesByName("RustDesk").Kill()   │
│  [Implemented] Best-effort with catch                           │
│                                                               │
│  ── Step 2.7: HidHide pre-write ───────────────────────────── │
│  [Implemented] HidHideConfigurator.PreWriteRules(seat)         │
│  [Implemented] Best-effort with catch                           │
│                                                               │
│  ── Step 3: Virtual display ────────────────────────────────── │
│  [Implemented] displayManager.CreateDisplayAsync()             │
│                                                               │
│  ── Step 4: Firewall ──────────────────────────────────────── │
│  [Implemented] firewall.OpenPortsAsync()                       │
│                                                               │
│  ── Step 5: Audio routing ─────────────────────────────────── │
│  [Implemented] PerSession path: skip (no VAC needed)           │
│  [Implemented] SharedHost path: AudioRouter.AssignCable()     │
│  [Implemented] Set session capture default if device known     │
│                                                               │
│  ── Step 5.7: Emulator config seeding ─────────────────────── │
│  [Implemented] foreach seeder.SeedAsync() (best-effort)        │
│                                                               │
│  ── Step 6: Apollo streaming ──────────────────────────────── │
│  [Implemented] ApolloManager.StartAsync()                      │
│  [Implemented] Task.Delay(5000) for SudoVDA IPC init           │
│  [Implemented] ParseSudoVdaDisplayId from Apollo log           │
│  [Implemented] If UUID found: restart Apollo with UUID,        │
│                 apply display isolation                         │
│  [Implemented] If not found: LogDebug (expected at this point) │
│                                                               │
│  ── Step 7: Controller + Input ────────────────────────────── │
│  [Implemented] If EnableViGEmController:                      │
│    [Implemented] ControllerManager.CreateController()          │
│    [Implemented] Auto-assign XInput if enabled                 │
│  [Implemented] Else: log "native" mode                        │
│                                                               │
│  ── Step 8: HidHide + Hooks ──────────────────────────────── │
│  [Implemented] HidHideConfigurator.CloakForSession()          │
│  [Implemented] InputHookManager.InstallForSession()            │
│                                                               │
│  ── Step 9: Ready ────────────────────────────────────────── │
│  [Implemented] Status = Ready, ReadyAt = now                  │
│  [Implemented] BroadcastState                                  │
│                                                               │
│  ── On error ────────────────────────────────────────────── │
│  [Implemented] Status = Error, ErrorMessage = ex.Message       │
│  [Implemented] Best-effort TeardownSeatInternalAsync           │
│  [Implemented] throw                                           │
└───────────────────────────────────────────────────────────────┘
```

### TeardownSeatInternalAsync — Reverse-Order Cleanup

```
[Implemented] OnConnectAppLauncher.Forget()
[Implemented] InputHookManager.Uninstall()
[Implemented] HidHideConfigurator.UncloakForSession()
[Implemented] UnassignControllersForSeat()
[Implemented] ControllerManager.DestroyController()
[Implemented] ApolloManager.Stop()
[Implemented] AudioRouter.ReleaseCable()
[Implemented] FirewallManager.ClosePortsAsync()
[Implemented] VirtualDisplayManager.DestroyDisplayAsync()
[Implemented] SessionLauncher.DisconnectSession()
[Implemented] SessionLauncher.LogoffSession()
[Implemented] PortAllocator.Release()
[Implemented] ApolloConfigBuilder.CleanupConfig()
```

All steps are individually try-caught — best effort, no step failure blocks the next.

### Observations

- **RustDesk audio suppression** (step 2.5) is ~30 lines of inline system configuration embedded in provisioning. This is ad-hoc cross-application configuration, not a SeatManager responsibility.
- **SudoVDA display detection** (step 6) is ~50 lines inlined in provisioning, including the same logic duplicated in `TryLateDisplayDetectionAsync`. The provisioning path restarts Apollo after detecting the UUID; the health-check path does not.
- **Audio capture default** (step 5.5) shells out to the service's own EXE via `RunHelperInSeatSession` — this is correct but couples SeatManager to the helper EXE path.
- **Step delays** (5000ms for SudoVDA init, 2000ms before display isolation, 500ms before Hz clamp) are hardcoded magic numbers with no progress feedback.

---

## Dependency Map

| Dependency | Type | Used for | Windows-specific? | Abstraction exists? | Candidate boundary? |
|------------|------|----------|-------------------|---------------------|---------------------|
| ILogger\<SeatManager\> | Framework | Structured logging | No | Yes (ILogger) | No |
| IOptions\<MultiSeatOptions\> | Framework | Configuration | No | Yes (IOptions) | No |
| AccountManager | Concrete | Windows account CRUD, group membership, credentials | **Yes** (NetApi32 P/Invoke) | No | **Yes** — 4 call sites, all Windows-specific |
| ISessionLauncher | Interface | RDP session create/disconnect/logoff/helper | **Yes** (RDP loopback) | **Yes** | Already extracted |
| ProcessInjector | Concrete | Launch processes in target sessions | **Yes** (CreateProcessAsUser, token manipulation) | No | **Yes** — used for Apollo, apps, helpers, RustDesk kill |
| IVirtualDisplayManager | Interface | SudoVDA display create/destroy | **Yes** (kernel driver) | **Yes** | Already extracted |
| ApolloManager | Concrete | Apollo process lifecycle, log parsing, display UUID | No (process management) | No | **Yes** — PROVIDER-CONTRACT.md defines the boundary |
| ApolloConfigBuilder | Concrete | sunshine.conf generation, config update | No | No | **Yes** — part of provider boundary |
| PortAllocator | Concrete | Port block allocation/release | No | No | Low value — simple, stable, 2 call sites |
| FirewallManager | Concrete | netsh firewall rules | **Yes** (Windows Firewall) | No | **Yes** — system-level, could be interface |
| AudioRouter | Concrete | VAC cable assignment, VoiceMeeter | **Yes** (WASAPI, VoiceMeeter) | No | Moderate — only used in SharedHost mode |
| ControllerManager | Concrete | ViGEm virtual Xbox 360 controllers | **Yes** (ViGEmBus driver) | No | Moderate — only when EnableViGEmController on |
| InputRouter | Concrete | XInput polling, controller assignment | **Yes** (XInput P/Invoke) | No | Low — only when EnableViGEmController on |
| InputHookManager | Concrete | Keyboard/mouse hooks via DLL | **Yes** (WH_KEYBOARD_LL) | No | Low — currently a no-op by design |
| HidHideConfigurator | Concrete | Gamepad session jail via HidHide CLI | **Yes** (HidHide driver CLI) | No | Moderate — only when EnableHidHideCloaking on |
| OnConnectAppLauncher | Concrete | App launch on CLIENT CONNECTED log | No (process management) | No | Low — clean, small, focused |
| ApolloServerQuery | Concrete | Apollo HTTP serverinfo probe | No | No | Low — clean, focused |
| IEnumerable\<IEmulatorConfigSeeder\> | Interface collection | Per-seat emulator config | No | **Yes** | Already abstracted |

---

## Responsibility Classification

### Should conceptually belong to SeatManager (core orchestration)

| Responsibility | Evidence |
|----------------|----------|
| Seat lifecycle coordination | ProvisionSeatAsync, TeardownSeatInternalAsync |
| Active seat count enforcement | ActiveSeatCount, MaxSeats check |
| Seat state transitions | Status property management |
| WebSocket broadcast | BroadcastState after mutations |
| Per-seat service health | GetSeatServicesAsync |

### Accidental historical coupling (should be delegated)

| Responsibility | Current location | Suggested owner |
|----------------|------------------|-----------------|
| SudoVDA display UUID detection from Apollo log | ProvisionSeatAsync steps 6-6.7, TryLateDisplayDetectionAsync | Display subsystem or provider boundary |
| RustDesk audio suppression (file I/O + process kill) | ProvisionSeatAsync step 2.5 | Dedicated helper or session setup |
| Audio capture default setting via helper EXE | ProvisionSeatAsync step 5.5, ApplyAudioDefaults | AudioRouter |
| HidHide pre-write rules | ProvisionSeatAsync step 2.7 | HidHideConfigurator (already partially delegated) |
| Apollo config update on restart | StartApolloAsync, RestartApolloAsync | ApolloManager or provider boundary |
| Apollo client pairing (GetPairedClients, UnpairClient) | Pass-through methods | Should be on the provider/config boundary |
| Controller assignment logic | ResetController, ProvisionSeatAsync step 7 | InputRouter |
| Display isolation (helper CLI invocation) | ApplyDisplayIsolationAsync | Display subsystem |

---

## Existing Abstractions

| Interface | Implemented by | Methods | Wired in DI? |
|-----------|---------------|---------|--------------|
| ISessionLauncher | SessionLauncher | LaunchSessionAsync, DisconnectSession, LogoffSession, IsSessionAlive, IsSessionActive, RunHelperInSeatSession | ✅ Yes |
| IVirtualDisplayManager | VirtualDisplayManager | CreateDisplayAsync, DestroyDisplayAsync, IsDriverAvailable, EnumerateAllConnectedPaths | ✅ Yes |
| IEnumerable\<IEmulatorConfigSeeder\> | RetroArchConfigSeeder (etc.) | SeedAsync, IsEnabled, EmulatorName | ✅ Yes |

**Remaining concrete dependencies**: AccountManager, ProcessInjector, ApolloManager, ApolloConfigBuilder, PortAllocator, FirewallManager, AudioRouter, ControllerManager, InputRouter, InputHookManager, HidHideConfigurator, OnConnectAppLauncher, ApolloServerQuery.

---

## RFC-MSE-0001 Comparison

The RFC-MSE-0001 was documented across the initial and historical audits. There is no standalone RFC file — the gap analysis was part of those audit documents.

| RFC Concept | Current Status | Location in code | Notes |
|-------------|---------------|------------------|-------|
| **SeatSpec** | PARTIAL | `SeatRequest` (MultiSeat.Shared) | Init-only DTO, not immutable. No separation of "desired state" from user input. Width/Height/Fps are on both SeatRequest and SeatInfo. |
| **SessionRequest** | PARTIAL | Embedded in `SeatRequest` | Session params (resolution, fps) mixed into the seat request. No separate RDP session specification. `RdpGeometry` exists but is in Service layer. |
| **ProcessLaunchPlan** | NOT APPLICABLE | — | ProcessInjector takes raw exe path + args. No formal launch plan. |
| **ClientSessionProfile** | MISSING | — | No per-client profiles or capability negotiation. Apollo handles this internally. |
| **VirtualDisplayHandle** | PARTIAL | `seat.DisplayDevicePath` (string) | Raw string, not typed. No handle semantics (no dispose, no lifetime). |
| **ProviderInstanceId** | PARTIAL | `seat.ApolloProcessId` (int) | Raw int PID. `ProcessIdentity` model exists in Shared but unused. |
| **Seat lifecycle** | PARTIAL | ProvisionSeatAsync / TeardownSeatInternalAsync | Implemented as linear pipeline, not state-machine transitions. SeatStatus enum has states (Idle/Provisioning/Configuring/Ready/Streaming/TearingDown/Error) but transitions are implicit. |
| **Reconciler** | PARTIAL | SessionHealthCheck | Ad-hoc: combines detection + recovery in one class. No formal reconciliation loop. No desired-vs-observed comparison. |
| **Control Plane** | PARTIAL | SeatManager itself | All orchestration is in SeatManager. No separate control plane concept. |
| **Provider boundary** | DOCUMENTED ONLY | PROVIDER-CONTRACT.md | Contract exists in docs. No C# interface. ApolloManager is concrete. |
| **Capability model** | MISSING | — | No feature discovery. Apollo features hardcoded in ApolloConfigBuilder. |
| **Observed State** | EXISTS | `SeatInfo` + `SeatServices` | Rich observable state with per-subsystem health booleans. |
| **Failure domains** | PARTIAL | Seat isolation (separate sessions/port blocks) | No formal failure domain boundaries. No progressive backoff. |

---

## Vibepollo Readiness

### What would have to change before Vibepollo can become an IStreamingProvider

Based on the current SeatManager architecture, these concrete dependencies would block Vibepollo integration without coupling Core/SeatManager directly to Vibepollo:

1. **ApolloManager (concrete)** — SeatManager calls `StartAsync`, `Stop`, `IsAlive`, `RestartAsync`, `GetLogPath`, `ParseSudoVdaDisplayId`, `GetConfigPath`, `KillForReconnect`, `GetRestartCount`. An `IStreamingProvider` interface would need to encompass all of these. `ParseSudoVdaDisplayId` and `ResolveLogPath` are provider-specific (Vibepollo writes different log formats), which makes them natural boundary methods.

2. **ApolloConfigBuilder (concrete)** — SeatManager calls `BuildConfig`, `UpdateDisplayOutput`, `CleanupConfig`, `GetPairedClients`, `UnpairClient`, `UnpairAllClients`. The config format is provider-specific (sunshine.conf for Apollo/Vibepollo). A provider adapter would own config generation entirely.

3. **ApolloServerQuery (concrete)** — Queries the serverinfo HTTP endpoint. The response format is provider-specific (XML tags `state`, `currentgame`, `hostname`, `appversion`). A provider adapter would encapsulate this.

4. **Display UUID detection inlined in SeatManager** — Steps 6-6.7 in ProvisionSeatAsync parse the provider's log file to find the SudoVDA display UUID. This is provider-specific logic (Vibepollo writes different log layouts). It should be part of the provider boundary, not SeatManager.

5. **ProcessInjector (concrete)** — Used to launch Apollo inside the seat session via `LaunchApolloInSessionAsync`. This method is Apollo-specific (configures Apollo's command line). A provider adapter would own its own launch mechanism.

6. **Apollo restart count** — `ApolloManager.GetRestartCount` is exposed via `GetSeatServicesAsync`. The provider boundary would need a health/status query method.

### What could stay unchanged

- AccountManager, SessionLauncher, VirtualDisplayManager, PortAllocator, FirewallManager, AudioRouter, ControllerManager, InputRouter, InputHookManager, HidHideConfigurator, OnConnectAppLauncher — these are provider-agnostic infrastructure.

---

## Architectural Problems

### Problem 1: Display detection logic duplicated across provisioning and health check

**Provisioning path** (ProvisionSeatAsync steps 6-6.7):
```
Task.Delay(5000)
ParseSudoVdaDisplayId(logPath)
If found: update config, restart Apollo, apply display isolation
If not found: LogDebug (expected — retry from health check)
```

**Health check path** (TryLateDisplayDetectionAsync):
```
Read entire Apollo log file
Find last "Currently available display devices:" marker
ParseSudoVdaDisplayIdFromLogText from last block
If found: update config, apply display isolation (no restart)
```

These are **two different implementations** of the same conceptual operation with subtly different behavior: provisioning restarts Apollo with the UUID; the health check does not. The provisioning path uses `ApolloManager.ParseSudoVdaDisplayId` (instance method, reads file); the health check uses `ApolloManager.ParseSudoVdaDisplayIdFromLogText` (static method, parses text). The health check also needs to read the log itself (rather than delegating) because it must find the *last* display block, not the first.

### Problem 2: SeatManager exposes internal dependencies as public properties

```csharp
public InputRouter InputRouter => _inputRouter;
public InputHookManager InputHookManager => _inputHookManager;
public ApolloManager ApolloManager => _apolloManager;
```

These are used by the API layer to reach into SeatManager's dependencies. This creates a "Service Locator" pattern — the API asks SeatManager for its guts rather than having them injected directly. It makes SeatManager a god object that knows about everything and exposes everything.

### Problem 3: RustDesk audio suppression is system configuration, not seat orchestration

~30 lines of inline code in ProvisionSeatAsync that:
- Creates directories under `C:\Users\{account}\AppData\Roaming\RustDesk\config\`
- Writes a TOML config file
- Enumerates and kills RustDesk processes in the seat session

This is cross-application configuration that happens to run during provisioning. It has nothing to do with seat lifecycle and would be better as a pre-session-setup step or a helper class.

### Problem 4: No concurrency control on ProvisionSeatAsync

`_seats` is a `ConcurrentDictionary`, so two concurrent provisions won't corrupt the dictionary. But nothing prevents:
- Two provisions from allocating the same port (PortAllocator uses a lock — safe)
- Two provisions from trying to create sessions for the same account simultaneously
- A teardown running concurrently with a provision for the same seat

The `ActiveSeatCount` check at the top is not atomic with the `TryAdd` later — a race between two concurrent provisions could both pass the count check and both succeed (creating more seats than MaxSeats allows), though the port allocator and session launcher would serialize at a lower level.

### Problem 5: SeatInfo is a mutable bag with no invariant enforcement

`SeatInfo` has 20+ mutable properties. Any code with a reference to a `SeatInfo` can set any property at any time. The provisioning pipeline sets properties in a specific order, but nothing enforces that `SessionId` is set before `ApolloProcessId`, or that `DisplayDevicePath` is set before display isolation runs. The properties are loosely coupled — the only invariant is the provisioning pipeline's sequential execution.

---

## Top 3 Next Small Changes

### Candidate 1: Extract display detection into the display subsystem

**What changes**: Move the SudoVDA UUID detection logic out of SeatManager and into a dedicated method/class owned by the display subsystem (or a new `DisplayDiagnostics` helper). Consolidate the two different detection implementations (provisioning vs health-check) into one well-defined operation.

**Why it matters**:
- **Highest coupling reduction**: Removes SeatManager's direct dependence on `ApolloManager.GetLogPath` and `ApolloManager.ParseSudoVdaDisplayId` for display UUID discovery (~80 lines removed from SeatManager)
- **Eliminates duplication**: Two different implementations of the same operation become one
- **Provider boundary ready**: Display detection is provider-specific (log format differs between Apollo and Vibepollo). Making it a delegated responsibility prepares for IStreamingProvider.
- **Reduces SeatManager LOC by ~15%** (~80 of ~700 lines)

**Estimated scope**: 2-3 files. Extract a `DisplayUuidResolver` class that takes `ApolloManager` and returns the UUID. SeatManager calls it instead of inlining the logic.

**Risk**: Low. The logic already works; this is pure extraction with no behavior change.

**What it enables later**: The display subsystem can independently adapt to Vibepollo's different log format without touching SeatManager.

### Candidate 2: Replace SeatManager property accessors with direct API DI

**What changes**: Instead of `SeatManager.InputRouter`, `SeatManager.InputHookManager`, and `SeatManager.ApolloManager` as public properties (used by the API layer), inject these dependencies directly into the API endpoints that need them.

**Why it matters**:
- **Eliminates Service Locator anti-pattern**: The API layer currently reaches through SeatManager to get at its dependencies. Direct injection makes the dependency graph explicit.
- **Reduces SeatManager's API surface**: Fewer public members means less coupling surface.
- **Enables independent testability**: API endpoints can be tested with their own mocks, independent of SeatManager.

**Estimated scope**: 3-4 files. Modify `SeatEndpoints.cs` (or whichever API class uses these properties) to receive `InputRouter`, `InputHookManager`, `ApolloManager` directly. Remove the 3 public properties from SeatManager. Update DI registration.

**Risk**: Low. Pure wiring change. No behavior change.

**What it enables later**: SeatManager can be refactored (split, reorganized) without breaking the API layer.

### Candidate 3: Extract RustDesk audio suppression into a helper class

**What changes**: Move the ~30 lines of RustDesk config writing and process killing from ProvisionSeatAsync into a small `RustDeskConfigurator` or `PreSessionSetup` helper class.

**Why it matters**:
- **Separation of concerns**: Cross-application configuration (RustDesk) is not a seat lifecycle responsibility.
- **Reusability**: If other pre-session setup steps are needed, there's a clear place for them.
- **Testability**: The RustDesk logic can be tested independently.

**Estimated scope**: 1-2 files. New helper class, update ProvisionSeatAsync to call it.

**Risk**: Very low. Pure extraction.

**What it enables later**: A clean `PreSessionSetup` phase in the provisioning pipeline that can accumulate other cross-cutting concerns without bloating SeatManager.

---

## Recommended Next Step

**Extract display detection into the display subsystem.** This is the highest-value single change because it:

1. Removes the most code from SeatManager (~80 lines, ~15%)
2. Eliminates real duplication between provisioning and health-check paths
3. Removes SeatManager's coupling to ApolloManager's log parsing methods
4. Prepares for the provider boundary (display detection is provider-specific)
5. Is a pure extraction with no behavior change — minimal risk

The concrete change:
- Create `src/MultiSeat.Service/Display/DisplayUuidResolver.cs` with a `ResolveAsync(SeatInfo, ApolloManager, string configDir)` method
- Consolidate the provisioning path's file-read + parse + last-block detection into one call
- SeatManager's ProvisionSeatAsync step 6.5 calls `DisplayUuidResolver` instead of inlining the logic
- SeatManager's `TryLateDisplayDetectionAsync` calls the same resolver
- Tests pass identically — no behavior change

---

## Final Report

```
SeatManager responsibilities:  15+ (orchestration, display, audio, input, provider, monitoring, API, system config)
Dependency count:              17 (14 concrete, 2 interfaced, 1 interface collection)

Already abstracted:            ISessionLauncher, IVirtualDisplayManager, IEnumerable<IEmulatorConfigSeeder>
Still concrete:                AccountManager, ProcessInjector, ApolloManager, ApolloConfigBuilder,
                               PortAllocator, FirewallManager, AudioRouter, ControllerManager,
                               InputRouter, InputHookManager, HidHideConfigurator,
                               OnConnectAppLauncher, ApolloServerQuery

Largest coupling problem:      ApolloManager is concrete, blocking provider abstraction.
                               Display detection inlined in SeatManager with duplicated logic.
                               SeatManager exposes internal deps as public properties (Service Locator).

Most valuable small change:    Extract display detection (DisplayUuidResolver) — removes ~15% of SeatManager,
                               eliminates duplication, prepares provider boundary.

Vibepollo blocker:             ApolloManager (concrete, 9+ call sites), ApolloConfigBuilder (concrete,
                               6+ call sites), ApolloServerQuery (concrete, 1 call site), and inline
                               display UUID detection (provider-specific log parsing).

Audit:                         docs/audits/MSE-SeatManager-Audit.md
Source modified:               NO
ProcessTracking modified:      NO
traycer modified:              NO
Commit:                        NONE
Push:                          NONE
```