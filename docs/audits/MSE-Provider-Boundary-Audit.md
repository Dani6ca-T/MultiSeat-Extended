# Apollo Provider Boundary Audit

## Executive Summary

ApolloManager is NOT a provider abstraction — it is a concrete Apollo-specific infrastructure class that manages process lifecycle, log parsing, and display detection. However, its public API is surprisingly close to what a provider contract would need, and the coupling to Apollo is concentrated in identifiable methods. The largest architectural barrier is not ApolloManager itself, but the fact that SeatManager directly calls ApolloManager for display detection (log parsing) and config updates (ApolloConfigBuilder), which are provider-specific operations embedded in orchestration code.

**Decision: DEFER**

The provider boundary is architecturally important but the smallest useful first step is NOT creating IStreamingProvider. Instead, extract the display detection logic (log parsing) from SeatManager into ApolloManager, reducing SeatManager's direct coupling to Apollo-specific log formats. This is a prerequisite that makes a future provider boundary cleaner.

## Current Apollo Architecture

### Classes involved

| Class | Responsibility | Lines |
|-------|---------------|-------|
| `ApolloManager` | Process lifecycle, log path resolution, display UUID parsing, instance tracking | ~350 |
| `ApolloConfigBuilder` | sunshine.conf generation, config updates, credential management, ACL grants | ~1000 |
| `ApolloServerQuery` | HTTP health probe (serverinfo endpoint) | ~60 |
| `ApolloLogParser` | Static log parsing for resolution negotiation | ~50 |
| `OnConnectAppLauncher` | Log tailing for client connect/disconnect events | ~250 |
| `ClientResolutionFollower` | Log reading for resolution following | ~150 |

### ApolloManager Public API

| Method | Purpose | Callers | Provider-generic? |
|--------|---------|---------|-------------------|
| `StartAsync(SeatInfo, ct)` | Launch Apollo in seat session | SeatManager | **Yes** — generic lifecycle |
| `Stop(SeatInfo)` | Kill Apollo process tree | SeatManager | **Yes** — generic lifecycle |
| `KillForReconnect(SeatInfo)` | Kill before reconnect, preserve instance | SessionHealthCheck, SeatManager | **Yes** — generic lifecycle |
| `RestartAsync(SeatInfo, ct)` | Restart crashed instance | SessionHealthCheck | **Yes** — generic lifecycle |
| `IsAlive(Guid seatId)` | Check if process running | SeatManager | **Yes** — generic |
| `GetRestartCount(Guid seatId)` | Get crash count | SeatManager | **Yes** — generic |
| `RunningInstanceCount` | Count running instances | Diagnostic | **Yes** — generic |
| `GetLogPath(string account, string configDir)` | Resolve log file path | SeatManager, OnConnectAppLauncher, ClientResolutionFollower | **No** — Apollo-specific log layout |
| `GetConfigPath(Guid seatId)` | Get config file path | SeatManager | **No** — Apollo config path |
| `GetWebUiUrl(SeatInfo)` | Web UI URL | Unused externally | **No** — Apollo port layout |
| `ParseSudoVdaDisplayId(string logPath)` | Parse display UUID from Apollo log | SeatManager | **No** — Apollo log format |
| `ParseSudoVdaDisplayIdFromLogText(string)` | Static log text parser | SeatManager | **No** — Apollo log format |
| `ResolveLogPath(string seatDir)` | Static log path resolver | OnConnectAppLauncher, ClientResolutionFollower, tests | **No** — Apollo log layout |
| `IsApolloInstalled` | Check exe exists | SeatManager | **No** — Apollo path |

**Split: 7 generic, 7 Apollo-specific.**

### ApolloConfigBuilder Public API

| Method | Purpose | Callers | Provider-generic? |
|--------|---------|---------|-------------------|
| `BuildConfig(SeatInfo, configDir)` | Generate sunshine.conf | ApolloManager | **No** — Apollo config format |
| `UpdateDisplayOutput(path, displayId)` | Update output_name in config | SeatManager | **No** — Apollo config key |
| `CleanupConfig(account, configDir)` | Remove junction points | SeatManager | **No** — Apollo file layout |
| `GetPairedClients(account, configDir)` | Read sunshine_state.json | SeatManager | **No** — Apollo state format |
| `UnpairClient(account, configDir, name)` | Remove client from state | SeatManager | **No** — Apollo state format |
| `UnpairAllClients(account, configDir)` | Clear all pairings | SeatManager | **No** — Apollo state format |

**All 6 methods are Apollo-specific.**

### ApolloServerQuery

| Method | Purpose | Callers | Provider-generic? |
|--------|---------|---------|-------------------|
| `QueryAsync(int port, ct)` | HTTP serverinfo probe | SeatManager, HostApolloMonitor | **No** — Apollo-specific XML response format |

## Consumer Map

### SeatManager (17 Apollo-related call sites)

```
SeatManager
├── _apolloManager.StartAsync()           — provisioning, StartApollo, RestartApollo
├── _apolloManager.Stop()                 — teardown, StopApollo, RestartApollo, display restart
├── _apolloManager.KillForReconnect()     — SetNvencPreset, SetResolution
├── _apolloManager.IsAlive()              — GetSeatServices
├── _apolloManager.GetRestartCount()      — GetSeatServices
├── _apolloManager.GetLogPath()           — provisioning, TryLateDisplayDetection
├── _apolloManager.GetConfigPath()        — provisioning, StartApollo, RestartApollo, ResetDisplay
├── _apolloManager.ParseSudoVdaDisplayId() — provisioning
├── ApolloManager.ParseSudoVdaDisplayIdFromLogText() — TryLateDisplayDetection
├── _configBuilder.UpdateDisplayOutput()   — provisioning, StartApollo, RestartApollo, ResetDisplay, TryLateDisplay
├── _configBuilder.BuildConfig()           — SetResolution
├── _configBuilder.CleanupConfig()         — teardown
├── _configBuilder.GetPairedClients()      — API (GetPairedClients)
├── _configBuilder.UnpairClient()          — API (UnpairClient)
├── _configBuilder.UnpairAllClients()      — API (UnpairAllClients)
├── _serverQuery.QueryAsync()              — GetSeatServices
```

### SessionHealthCheck (3 call sites)

```
SessionHealthCheck
├── _apolloManager.KillForReconnect()     — sleep reconnect
├── _apolloManager.RestartAsync()          — crash recovery, sleep reconnect
```

### OnConnectAppLauncher (1 call site)

```
OnConnectAppLauncher
├── _apollo.GetLogPath()                  — log tailing for connect/disconnect
```

### ClientResolutionFollower (1 call site)

```
ClientResolutionFollower
├── _apollo.GetLogPath()                  — log reading for resolution
```

### HostApolloMonitor (1 call site)

```
HostApolloMonitor
├── _serverQuery.QueryAsync()             — host-wide health monitoring
```

## Lifecycle Trace

```
Seat requested
    ↓
Account prepared                    [IAccountManager ✅]
    ↓
Port allocated                      [PortAllocator — concrete]
    ↓
Session created                     [ISessionLauncher ✅]
    ↓
RustDesk config suppressed          [inline in SeatManager]
    ↓
HidHide pre-write                   [HidHideConfigurator — concrete]
    ↓
Display created                     [IVirtualDisplayManager ✅]
    ↓
Firewall opened                     [FirewallManager — concrete]
    ↓
Audio assigned                      [AudioRouter — concrete]
    ↓
Emulator configs seeded             [IEmulatorConfigSeeder ✅]
    ↓
Apollo configured + started         [ApolloManager + ApolloConfigBuilder — concrete]
    ↓                                    ↳ SeatManager calls _configBuilder.BuildConfig
    ↓                                    ↳ SeatManager calls _apolloManager.StartAsync
    ↓                                    ↳ StartAsync internally calls _configBuilder.BuildConfig
    ↓
Display UUID detected               [ApolloManager.ParseSudoVdaDisplayId — Apollo-specific]
    ↓                                    ↳ SeatManager reads Apollo log directly
    ↓                                    ↳ SeatManager calls _configBuilder.UpdateDisplayOutput
    ↓                                    ↳ SeatManager restarts Apollo
    ↓
Display isolation applied           [SessionLauncher.RunHelperInSeatSession]
    ↓
Controller + Input assigned         [ControllerManager, InputRouter — concrete]
    ↓
HidHide cloaking                    [HidHideConfigurator — concrete]
    ↓
Ready
    ↓
Client connects                     [OnConnectAppLauncher tails Apollo log]
    ↓
Resolution following                [ClientResolutionFollower reads Apollo log]
    ↓
Health monitoring                   [SessionHealthCheck checks Apollo process]
    ↓                                    ↳ _apolloManager.KillForReconnect
    ↓                                    ↳ _apolloManager.RestartAsync
    ↓                                    ↳ _seatManager.ApplyDisplayIsolationAsync
    ↓
Teardown                            [SeatManager — reverse order]
                                        ↳ _apolloManager.Stop
                                        ↳ _configBuilder.CleanupConfig
```

**Provider leakage points in SeatManager:**
1. **Provisioning step 6.5** — SeatManager parses Apollo's log for display UUID (`ParseSudoVdaDisplayId`)
2. **Provisioning step 6.5** — SeatManager updates Apollo config (`_configBuilder.UpdateDisplayOutput`)
3. **Provisioning step 6.5** — SeatManager restarts Apollo after display detection
4. **TryLateDisplayDetectionAsync** — SeatManager reads Apollo log text, parses display UUID
5. **StartApolloAsync / RestartApolloAsync** — SeatManager updates Apollo config after start
6. **SetNvencPresetAsync / SetResolutionAsync** — SeatManager rebuilds Apollo config
7. **Teardown** — SeatManager calls `_configBuilder.CleanupConfig`
8. **GetSeatServices** — SeatManager queries Apollo HTTP endpoint via `_serverQuery`
9. **API methods** — SeatManager reads/writes Apollo state files via `_configBuilder`

## ApolloConfigBuilder Analysis

**Classification: Provider-specific configuration utility**

- **Inputs**: SeatInfo, MultiSeatOptions, file system (Apollo install dir, ProgramData)
- **Outputs**: sunshine.conf file paths, config file modifications
- **Apollo-specific fields**: sunshine_name, port, output_name, resolutions, fps, dd_*, headless_mode, encoder, nvenc_*, virtual_sink, keep_sink_default, auto_capture_sink, stream_mic, controller, gamepad, file_state, credentials_file, pkey, cert, log_path, min_log_level, hevc_mode, av1_mode, color_space, color_range, min_threads, fec_percentage
- **Consumers**: ApolloManager (BuildConfig), SeatManager (UpdateDisplayOutput, CleanupConfig, GetPairedClients, UnpairClient, UnpairAllClients, BuildConfig)
- **Could it be part of a provider contract?** Partially — `BuildConfig`, `UpdateDisplayOutput`, `CleanupConfig` are provider-specific operations. `GetPairedClients`, `UnpairClient`, `UnpairAllClients` are session management operations.

## ApolloServerQuery Analysis

**Classification: Provider health/status probe**

- Queries Apollo's `serverinfo` XML endpoint (same as Moonlight client)
- Returns `ApolloServerInfo` (hostname, version, streaming status)
- Used by SeatManager (per-seat status) and HostApolloMonitor (host-wide status)
- **Could it be part of a provider contract?** Yes — `QueryHealth` is already in PROVIDER-CONTRACT.md

## Provider Coupling Map

### Hard blockers for provider abstraction

1. **Apollo config format** — sunshine.conf is Apollo/Sunshine-specific. Vibepollo uses the same format (it's a fork), but a different provider would use a different format. ApolloConfigBuilder generates this directly.

2. **Apollo log format** — Display detection, client connect/disconnect detection, and resolution following all parse Apollo's log output. The log format is provider-specific.

3. **Apollo process arguments** — `--config`, `--oute` (output), working directory. Different providers may use different CLI arguments.

4. **Apollo HTTP API** — serverinfo XML response format. Different providers may use different health endpoints.

5. **Apollo file layout** — sunshine_state.json, apps.json, credentials directory, log directory structure.

### Soft coupling

1. **Port offsets** — Constants define Apollo's port layout (GFE HTTP, Web UI, Video, Audio, etc.). A different provider may use different offsets.

2. **Display detection strategy** — Parsing provider logs for display UUID. A different provider may use a different mechanism.

3. **Config file naming** — sunshine.conf, sunshine_state.json. Different providers use different names.

### Cosmetic naming

1. Class names: `ApolloManager`, `ApolloConfigBuilder`, `ApolloServerQuery`, `ApolloLogParser`
2. Log messages referencing "Apollo"
3. Config keys like `ApolloConfigDir`, `ApolloExePath`, `ApolloLogLevel`

### Already provider-neutral code

1. `ProcessInjector` — generic process launcher (already used by ApolloManager)
2. `SessionLauncher` — generic session management (already behind ISessionLauncher)
3. `VirtualDisplayManager` — generic display management (already behind IVirtualDisplayManager)
4. `PortAllocator` — generic port allocation
5. `FirewallManager` — generic firewall management
6. `AudioRouter` — generic audio routing
7. `ControllerManager` — generic ViGEm management

## Vibepollo Readiness

If we wanted to support Vibepollo as another provider tomorrow:

**Hard blockers:**
1. `ApolloConfigBuilder.BuildConfig` generates sunshine.conf — Vibepollo uses the same format (it's a Sunshine fork), so this actually WORKS for Vibepollo too. But the config format details (dd_*, headless_mode) are Apollo-specific knobs.
2. `ApolloManager.ParseSudoVdaDisplayId` parses Apollo's log format — Vibepollo may log differently.
3. `ApolloManager.GetLogPath` / `ResolveLogPath` knows Apollo's log file layout — Vibepollo writes to `logs/` subdirectory (already handled in ResolveLogPath).
4. `ApolloServerQuery.QueryAsync` parses Apollo's XML serverinfo — Vibepollo may use a different format.

**Soft coupling:**
1. Process arguments (`--config`, `--oute`) — Vibepollo may use different args.
2. Port offsets — Vibepollo may use different port layout.
3. File layout — sunshine_state.json path conventions.

**Key insight:** Apollo and Vibepollo are both Sunshine forks. They share the same config format (sunshine.conf), the same state file format (sunshine_state.json), and roughly the same HTTP API. The differences are in process arguments, log format details, and some config keys. This means a provider abstraction is valuable but the "two providers" scenario is actually simpler than a fully generic provider contract would suggest.

## RFC-MSE-0001 Comparison

| Concept | Status | Notes |
|---------|--------|-------|
| `IStreamingProvider` | **Missing** | No interface exists. ApolloManager is concrete. |
| `Provider lifecycle (Start/Stop/Restart)` | **Partial** | ApolloManager has Start/Stop/Restart but they're concrete methods, not interface members. |
| `Provider configuration (GenerateConfig)` | **Partial** | ApolloConfigBuilder.BuildConfig exists but is Apollo-specific. |
| `Provider health (QueryHealth)` | **Partial** | ApolloServerQuery.QueryAsync exists but is Apollo-specific. |
| `Provider session management` | **Missing** | No provider-level session management — Apollo doesn't manage sessions. |
| `Provider capabilities` | **Missing** | No capabilities query — provider features are hardcoded. |
| `Provider-specific metadata` | **Missing** | No metadata about the provider (name, version, protocol). |
| `Provider process management` | **Partial** | ApolloManager tracks processes, but it's provider-specific. |
| `Provider display handling` | **Leaking** | SeatManager directly parses Apollo logs for display UUID — should be provider's job. |
| `Provider log handling` | **Leaking** | SeatManager, OnConnectAppLauncher, ClientResolutionFollower all parse Apollo logs. |

## Candidate Provider Boundary

The correct abstraction is NOT `IApolloManager` (that would just wrap the concrete class). The correct abstraction is `IStreamingProvider` that encapsulates:

```
IStreamingProvider
├── StartAsync(SeatInfo, ct) → int pid
├── Stop(SeatInfo)
├── KillForReconnect(SeatInfo)
├── RestartAsync(SeatInfo, ct) → int pid
├── IsAlive(Guid seatId) → bool
├── GetRestartCount(Guid seatId) → int
├── GetLogPath(string account, string configDir) → string
├── DiscoverDisplayId(string logPath) → string?
├── UpdateConfig(SeatInfo, string configPath, string displayId)
├── BuildConfig(SeatInfo, string configDir) → string configPath
├── CleanupConfig(string account, string configDir)
├── GetPairedClients(string account, string configDir) → IReadOnlyList<string>
├── UnpairClient(string account, string configDir, string name) → bool
├── UnpairAllClients(string account, string configDir)
└── QueryHealth(int port, ct) → ProviderHealthInfo?
```

But this is a LARGE interface — 15+ methods mixing lifecycle, config, display, and session concerns.

## Smallest Useful Abstraction

The provider boundary should be split into focused concerns:

### Step 1: Extract display detection from SeatManager into ApolloManager

**Current:** SeatManager directly calls `_apolloManager.ParseSudoVdaDisplayId(logPath)` and `ApolloManager.ParseSudoVdaDisplayIdFromLogText(text)`.

**Proposed:** Add `DiscoverDisplayId(string logPath)` and `DiscoverDisplayIdFromLogText(string text)` to ApolloManager as the sole entry points. SeatManager calls these through ApolloManager, not as static methods.

**Value:** Removes 2 direct Apollo-specific call sites from SeatManager. Makes SeatManager less coupled to Apollo log format.

### Step 2: Extract config management from SeatManager into a provider-owned boundary

**Current:** SeatManager calls `_configBuilder.UpdateDisplayOutput()`, `_configBuilder.BuildConfig()`, `_configBuilder.CleanupConfig()`, `_configBuilder.GetPairedClients()`, etc.

**Proposed:** These operations move behind a provider boundary so SeatManager doesn't know about sunshine.conf.

### Step 3: Create IStreamingProvider with lifecycle + config + display + health

**Current:** No interface. All concrete.

**Proposed:** Interface with lifecycle (Start/Stop/Restart/IsAlive), config (Build/Update/Cleanup), display (Discover), health (Query), and session management (Pair/Unpair).

## Risks

1. **Scope creep** — Provider abstraction can grow to 20+ methods if not carefully bounded.
2. **Apollo-specific methods leak through** — Some methods (ParseSudoVdaDisplayId) are Apollo-specific even behind an interface.
3. **Premature abstraction** — Only one provider exists today (Apollo). Vibepollo is a fork with the same config format.
4. **Config format coupling** — sunshine.conf is shared between Apollo and Vibepollo, so the "provider boundary" for config is thin.
5. **Testing complexity** — Mocking a provider requires implementing all 15+ methods.

## Decision

**DEFER**

The provider boundary is the right long-term architecture, but NOW is not the right time because:

1. **Only one provider exists** — Apollo. Vibepollo is a Sunshine fork with the same config format.
2. **The config format is shared** — sunshine.conf works for both Apollo and Vibepollo. The "provider boundary" for config is thin.
3. **The smallest useful step is display detection extraction** — not a full IStreamingProvider.
4. **ProcessTracking is incomplete** — fixing it first provides more value than provider abstraction.
5. **The 15+ method interface is too large for a first extraction** — previous successful extractions (ISessionLauncher, IVirtualDisplayManager, IAccountManager) had 3-8 methods each.

## Recommended Next Step

**Extract display detection from SeatManager into ApolloManager** as a focused, small change:

1. Add `DiscoverDisplayId(string logPath)` instance method to ApolloManager (wraps existing `ParseSudoVdaDisplayId`)
2. Add `DiscoverDisplayIdFromLogText(string text)` static method to ApolloManager (wraps existing `ParseSudoVdaDisplayIdFromLogText`)
3. Update SeatManager to call these through ApolloManager instead of using static methods directly
4. This reduces SeatManager's Apollo-specific call sites by 2 and is a prerequisite for a cleaner provider boundary

This is the same pattern as previous extractions: small, focused, no behavior change, prepares for future architecture.

## Implementation Result — Display Detection Extraction

**Completed**: 2026-09-01

### What was extracted

The late-detection display UUID slicing logic was moved from `SeatManager.TryLateDisplayDetectionAsync` into `ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText`. Previously, SeatManually manually located the `"Currently available display devices:"` marker in Apollo's log text, sliced from the last occurrence, and passed the slice to `ParseSudoVdaDisplayIdFromLogText`. Now ApolloManager owns this entirely.

### Why it belongs in ApolloManager

The marker string, slice-from-last semantics, and the distinction between provisioning (first block) vs. late detection (last block) are Apollo log-format knowledge. SeatManager should orchestrate "detect display" without knowing Apollo's log markers or block-selection rules.

### Files changed

| File | Change |
|------|--------|
| `src/MultiSeat.Service/Streaming/ApolloManager.cs` | Added `ParseLatestSudoVdaDisplayIdFromLogText(string)` — static method that slices from last marker, delegates to existing parser |
| `src/MultiSeat.Service/Sessions/SeatManager.cs` | `TryLateDisplayDetectionAsync` now calls `ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(text)` instead of manual slicing |
| `src/MultiSeat.Tests/Streaming/StreamingTests.cs` | Added 3 tests: picks from last block, works with single block, returns nothing when no block |

### Behavior preserved

- Provisioning: `_apolloManager.ParseSudoVdaDisplayId(logPath)` reads file, finds FIRST display block — **unchanged**
- Late detection: now calls `ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(text)` — **same semantics** as the previous manual slice-from-last
- Both paths still delegate to `ParseSudoVdaDisplayIdFromLogText` for the actual JSON parsing
- No Apollo restart behavior changed
- No display isolation behavior changed

### Test / build result

- Build: 0 errors (9 pre-existing errors from untracked ProcessTracking files excluded for verification)
- Tests: 390 passed, 0 failed, 17 skipped (baseline was 387 passed — 3 new tests added)
- ProcessTracking: pre-existing untracked files temporarily excluded for build verification, then restored exactly

### Next recommended architectural step

Step 2 from the "Smallest Useful Abstraction" section: extract Apollo config management (`UpdateDisplayOutput`, `BuildConfig`, `CleanupConfig`) from SeatManager into a provider-owned boundary. This reduces the remaining 6 ApolloConfigBuilder call sites in SeatManager.
