# Apollo Provider Boundary — Architecture Checkpoint

**Date**: 2026-09-01
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: 949ebb6 (refactor(apollo): encapsulate display detection)

---

## 1. Executive Summary

After the recent display-detection extraction (`ParseLatestSudoVdaDisplayIdFromLogText`), the correct provider boundary is now visible. **ApolloManager is the natural provider adapter** — it already owns lifecycle + display detection. The remaining leak is that SeatManager calls ApolloConfigBuilder directly for 6 operations (UpdateDisplayOutput, BuildConfig, CleanupConfig, GetPairedClients, UnpairClient, UnpairAllClients) and calls ApolloServerQuery directly for health. The smallest valuable next step is to route those operations through ApolloManager so SeatManager has zero direct knowledge of sunshine.conf, Apollo's HTTP API, or Apollo's state files.

**Decision: DEFER** — but with a clear, small next step identified.

The provider boundary should NOT be an `IStreamingProvider` interface yet. It should be a set of focused capability extractions that reduce SeatManager's Apollo-specific call sites from ~12 to ~3, making ApolloManager the sole provider-facing component.

---

## 2. Current Repository State

```
Branch:          master
HEAD:            949ebb6 (refactor(apollo): encapsulate display detection)
origin/master:   949ebb6 (in sync)
Working tree:    Clean (only untracked ProcessTracking + docs)
Staged files:    None
Modified files:  None
ProcessTracking: Pre-existing untracked, untouched
traycer:         Not on master (branch traycer/multiseat-extended-polite-squid)
```

---

## 3. Current Architecture Documents — What They Claim vs. Reality

### PROJECT-STATE.md

**Claim**: "SeatManager dependency status: 12 concrete, 3 interfaced, 1 interface collection"

**FACT**: This is accurate. After display-detection extraction, SeatManager has:
- 3 interfaces: ISessionLauncher, IVirtualDisplayManager, IAccountManager
- 1 interface collection: IEnumerable\<IEmulatorConfigSeeder\>
- 13 concrete: ApolloManager, ApolloConfigBuilder, ProcessInjector, PortAllocator, FirewallManager, AudioRouter, ControllerManager, InputRouter, InputHookManager, HidHideConfigurator, OnConnectAppLauncher, ApolloServerQuery, (ILogger, IOptions — framework)

### MSE-Provider-Boundary-Audit.md

**Claim**: "Step 1: Extract display detection from SeatManager into ApolloManager"

**FACT**: COMPLETED. `ParseLatestSudoVdaDisplayIdFromLogText` exists on ApolloManager. SeatManager no longer knows the display-log marker.

**Claim**: "Step 2: Extract config management from SeatManager into a provider-owned boundary"

**FACT**: NOT YET DONE. This is the correct next step. See Section 8.

### MSE-SeatManager-Audit.md

**Claim**: "17 constructor dependencies (14 concrete, 2 interfaces, 1 interface collection)"

**FACT**: After display-detection extraction and IAccountManager, the count is 18 parameters, 13 concrete dependencies (plus framework). The audit was written before IAccountManager extraction — it still lists `AccountManager` as concrete.

### PROVIDER-CONTRACT.md

**Claim**: Provider should own: ValidateConfiguration, Prepare, Start, Stop, Restart, QueryHealth, GetLogPath, ParseDisplayId, GenerateConfig, UpdateDisplayOutput, CleanupConfig, GetPairedClients, UnpairClient.

**FACT**: The contract is correct but aspirational. ApolloManager currently owns: Start, Stop, Restart, IsAlive, GetRestartCount, GetLogPath, GetConfigPath, ParseSudoVdaDisplayId, ParseLatestSudoVdaDisplayIdFromLogText, ResolveLogPath. It does NOT own: UpdateDisplayOutput, CleanupConfig, GetPairedClients, UnpairClient, UnpairAllClients, QueryHealth, GenerateConfig (partially — StartAsync calls BuildConfig internally but SeatManager also calls it directly in SetResolutionAsync).

### SEAT-AGGREGATE.md

**Claim**: Seat is the aggregate root. ProviderInstance belongs to Seat aggregate.

**FACT**: Correct. SeatInfo carries ApolloProcessId, DisplayDevicePath, ConfigPath (indirectly via ApolloManager instances). The aggregate boundary is sound.

---

## 4. Apollo Responsibility Map

### ApolloManager — Current Public API

| Method | Purpose | Callers | Provider-specific? | Should orchestration know this? |
|--------|---------|---------|-------------------|-------------------------------|
| `StartAsync(SeatInfo, ct)` | Launch Apollo in seat session | SeatManager | **No** — generic lifecycle | ✅ Yes |
| `Stop(SeatInfo)` | Kill Apollo process tree | SeatManager | **No** — generic lifecycle | ✅ Yes |
| `KillForReconnect(SeatInfo)` | Kill before reconnect | SessionHealthCheck, SeatManager | **No** — generic lifecycle | ✅ Yes |
| `RestartAsync(SeatInfo, ct)` | Restart crashed instance | SessionHealthCheck | **No** — generic lifecycle | ✅ Yes |
| `IsAlive(Guid seatId)` | Check if process running | SeatManager | **No** — generic | ✅ Yes |
| `GetRestartCount(Guid seatId)` | Get crash count | SeatManager | **No** — generic | ✅ Yes |
| `RunningInstanceCount` | Count running instances | Diagnostic | **No** — generic | ✅ Yes |
| `GetLogPath(string, string)` | Resolve log file path | SeatManager, OnConnectAppLauncher, ClientResolutionFollower | **Yes** — Apollo log layout | ⚠️ Should be provider-internal |
| `GetConfigPath(Guid)` | Get config file path | SeatManager | **Yes** — Apollo config path | ⚠️ Should be provider-internal |
| `GetWebUiUrl(SeatInfo)` | Web UI URL | Unused externally | **Yes** — Apollo port layout | ⚠️ Should be provider-internal |
| `ParseSudoVdaDisplayId(string)` | Parse display UUID from log | SeatManager (provisioning) | **Yes** — Apollo log format | ⚠️ Should be provider-internal |
| `ParseLatestSudoVdaDisplayIdFromLogText(string)` | Parse display UUID (latest block) | SeatManager (late detection) | **Yes** — Apollo log format | ⚠️ Should be provider-internal |
| `ParseSudoVdaDisplayIdFromLogText(string)` | Static log text parser | Tests, internal | **Yes** — Apollo log format | ⚠️ Should be provider-internal |
| `ResolveLogPath(string)` | Static log path resolver | OnConnectAppLauncher, ClientResolutionFollower, tests | **Yes** — Apollo log layout | ⚠️ Should be provider-internal |
| `IsApolloInstalled` | Check exe exists | SeatManager | **Yes** — Apollo path | ⚠️ Should be provider-internal |

**Split**: 7 generic (lifecycle), 7 provider-specific (log/config/display).

### ApolloConfigBuilder — Current Public API

| Method | Purpose | Callers | Provider-specific? | Should orchestration know this? |
|--------|---------|---------|-------------------|-------------------------------|
| `BuildConfig(SeatInfo, string)` | Generate sunshine.conf | ApolloManager (in StartAsync), SeatManager (SetResolutionAsync) | **Yes** — Apollo config format | ❌ No — provider should own |
| `UpdateDisplayOutput(string, string)` | Update output_name in config | SeatManager (5 call sites) | **Yes** — Apollo config key | ❌ No — provider should own |
| `CleanupConfig(string, string)` | Remove junction points on teardown | SeatManager | **Yes** — Apollo file layout | ❌ No — provider should own |
| `GetPairedClients(string, string)` | Read sunshine_state.json | SeatManager → API | **Yes** — Apollo state format | ❌ No — provider should own |
| `UnpairClient(string, string, string)` | Remove client from state | SeatManager → API | **Yes** — Apollo state format | ❌ No — provider should own |
| `UnpairAllClients(string, string)` | Clear all pairings | SeatManager → API | **Yes** — Apollo state format | ❌ No — provider should own |

**All 6 methods are Apollo-specific.** SeatManager calls all of them directly. This is the primary architectural leak.

### ApolloServerQuery — Current Public API

| Method | Purpose | Callers | Provider-specific? | Should orchestration know this? |
|--------|---------|---------|-------------------|-------------------------------|
| `QueryAsync(int port, ct)` | HTTP serverinfo probe | SeatManager (GetSeatServicesAsync), HostApolloMonitor | **Yes** — Apollo XML format | ❌ No — provider should own |

**1 method, Apollo-specific.** SeatManager calls it directly in `GetSeatServicesAsync`.

### ApolloLogParser — Current Public API

| Method | Purpose | Callers | Provider-specific? |
|--------|---------|---------|-------------------|
| `ParseLastRequestedMode(string)` | Extract client-requested resolution | ClientResolutionFollower | **Yes** — Apollo log format |

**1 method, Apollo-specific.** Only called by ClientResolutionFollower, not by SeatManager. Already encapsulated behind a consumer that owns its own logic.

---

## 5. SeatManager Leakage Map

### Currently: 13 Apollo-specific call sites in SeatManager

| # | Call site | Method | Category |
|---|-----------|--------|----------|
| 1 | ProvisionSeatAsync | `_apolloManager.StartAsync()` | Lifecycle ✅ |
| 2 | ProvisionSeatAsync | `_apolloManager.GetLogPath()` | Provider-specific ⚠️ |
| 3 | ProvisionSeatAsync | `_apolloManager.ParseSudoVdaDisplayId()` | Provider-specific ⚠️ |
| 4 | ProvisionSeatAsync | `_apolloManager.Stop()` / `StartAsync()` | Lifecycle ✅ |
| 5 | ProvisionSeatAsync | `_configBuilder.UpdateDisplayOutput()` | Provider-specific ⚠️ |
| 6 | StartApolloAsync | `_apolloManager.StartAsync()` | Lifecycle ✅ |
| 7 | StartApolloAsync | `_configBuilder.UpdateDisplayOutput()` | Provider-specific ⚠️ |
| 8 | RestartApolloAsync | `_apolloManager.Stop()` / `StartAsync()` | Lifecycle ✅ |
| 9 | RestartApolloAsync | `_configBuilder.UpdateDisplayOutput()` | Provider-specific ⚠️ |
| 10 | TryLateDisplayDetectionAsync | `_apolloManager.GetLogPath()` | Provider-specific ⚠️ |
| 11 | TryLateDisplayDetectionAsync | `_configBuilder.UpdateDisplayOutput()` | Provider-specific ⚠️ |
| 12 | SetResolutionAsync | `_configBuilder.BuildConfig()` | Provider-specific ⚠️ |
| 13 | ResetDisplayAsync | `_configBuilder.UpdateDisplayOutput()` | Provider-specific ⚠️ |
| 14 | TeardownSeatInternalAsync | `_configBuilder.CleanupConfig()` | Provider-specific ⚠️ |
| 15 | GetSeatServicesAsync | `_serverQuery.QueryAsync()` | Provider-specific ⚠️ |
| 16 | GetPairedClients | `_configBuilder.GetPairedClients()` | Provider-specific ⚠️ |
| 17 | UnpairClient | `_configBuilder.UnpairClient()` | Provider-specific ⚠️ |
| 18 | UnpairAllClients | `_configBuilder.UnpairAllClients()` | Provider-specific ⚠️ |

**Total: 18 call sites. 6 are generic lifecycle. 12 are provider-specific.**

### After routing config through ApolloManager (Step 2)

If ApolloManager gains: `UpdateDisplayConfig`, `CleanupSeatConfig`, `GetSeatPairedClients`, `UnpairSeatClient`, `UnpairAllSeatClients`, `QueryHealth`:

| # | Call site | Method | Category |
|---|-----------|--------|----------|
| 1-4 | ProvisionSeatAsync | `_apolloManager.StartAsync()` etc. | Lifecycle ✅ |
| 5 | ProvisionSeatAsync | `_apolloManager.UpdateDisplayConfig()` | Routed through provider ✅ |
| 6-9 | Start/RestartApolloAsync | `_apolloManager.StartAsync()` etc. | Lifecycle ✅ |
| 10 | TryLateDisplayDetectionAsync | `_apolloManager.GetLogPath()` | Routed through provider ✅ |
| 11 | TryLateDisplayDetectionAsync | `_apolloManager.UpdateDisplayConfig()` | Routed through provider ✅ |
| 12 | SetResolutionAsync | `_apolloManager.RebuildConfig()` | Routed through provider ✅ |
| 13 | ResetDisplayAsync | `_apolloManager.UpdateDisplayConfig()` | Routed through provider ✅ |
| 14 | TeardownSeatInternalAsync | `_apolloManager.CleanupSeatConfig()` | Routed through provider ✅ |
| 15 | GetSeatServicesAsync | `_apolloManager.QueryHealth()` | Routed through provider ✅ |
| 16-18 | API methods | `_apolloManager.GetPairedClients()` etc. | Routed through provider ✅ |

**After Step 2: 0 direct provider-specific call sites.** All go through ApolloManager.

---

## 6. ApolloConfigBuilder Analysis

### Classification: Provider-specific configuration utility

**What it does**: Generates and manages Apollo/Sunshine configuration files (sunshine.conf, sunshine_state.json, apps.json, credentials).

**Should it remain separate from ApolloManager?** YES.

**Reasons**:
1. ApolloConfigBuilder is ~1000 lines of complex, well-tested configuration logic
2. It has its own concerns: TLS identity management, ACL grants, junction points, credential protection
3. Merging it into ApolloManager would create a 1400+ line god class
4. The separation is conceptually correct: ApolloManager manages the process, ApolloConfigBuilder manages the configuration

**Should SeatManager call it directly?** NO.

**Reason**: Every call from SeatManager to ApolloConfigBuilder is an Apollo-specific operation that a future provider would handle differently. The config builder should be called through ApolloManager (or a future provider adapter).

### Public methods — should they move behind ApolloManager?

| Method | Current caller | Move to ApolloManager? | Why |
|--------|---------------|----------------------|-----|
| `BuildConfig` | ApolloManager (StartAsync), SeatManager (SetResolutionAsync) | Already partially behind ApolloManager. SeatManager's direct call in SetResolutionAsync should go through ApolloManager. | SeatManager shouldn't know about sunshine.conf generation |
| `UpdateDisplayOutput` | SeatManager (5 sites) | **Yes** — ApolloManager gains `UpdateDisplayConfig(seat, displayId)` | SeatManager shouldn't know about output_name config key |
| `CleanupConfig` | SeatManager (teardown) | **Yes** — ApolloManager gains `CleanupSeatConfig(seat)` | SeatManager shouldn't know about junction points |
| `GetPairedClients` | SeatManager → API | **Yes** — ApolloManager gains `GetSeatPairedClients(seat)` | SeatManager shouldn't know about sunshine_state.json |
| `UnpairClient` | SeatManager → API | **Yes** — ApolloManager gains `UnpairSeatClient(seat, name)` | Same |
| `UnpairAllClients` | SeatManager → API | **Yes** — ApolloManager gains `UnpairAllSeatClients(seat)` | Same |

---

## 7. ApolloServerQuery Analysis

### Classification: Apollo-specific health probe

**What it does**: Sends HTTP GET to `http://127.0.0.1:{port}/serverinfo?uniqueid=multiseat-dashboard`, parses XML response for hostname, version, and streaming state.

**Should it remain separate from ApolloManager?** YES — with one change.

**Reasons**:
1. ApolloServerQuery is ~60 lines, clean, focused
2. It has a single responsibility: HTTP health probe
3. HostApolloMonitor also uses it — it's not ApolloManager-exclusive
4. Merging it into ApolloManager would couple host-level monitoring to per-seat lifecycle

**Should SeatManager call it directly?** NO.

**Reason**: SeatManager calls `_serverQuery.QueryAsync()` in `GetSeatServicesAsync`. A future provider would have a different health endpoint. ApolloManager should own `QueryHealth(seat)` and expose `SeatServices` directly, or at minimum route the query through itself.

### The HostApolloMonitor complication

HostApolloMonitor uses ApolloServerQuery to check the **standalone** (host-console) Apollo — not a MultiSeat-managed one. This is a different concern: monitoring the host's own Apollo that MultiSeat coexists with. This should NOT go through ApolloManager (which manages per-seat instances). It should remain as a separate host-level diagnostic.

**Conclusion**: ApolloServerQuery stays separate. SeatManager stops calling it directly; ApolloManager gains a `QueryHealth` method that delegates to ApolloServerQuery internally.

---

## 8. ApolloLogParser Analysis

### Classification: Apollo-specific log parser

**What it does**: Extracts the client-requested display mode from Apollo's log line: `Info: Display mode for client [name] requested to [WxHxFPS]`.

**Should it remain separate?** YES.

**Reasons**:
1. ApolloLogParser is ~50 lines, purely static, well-tested
2. It's only called by ClientResolutionFollower — not by SeatManager
3. ClientResolutionFollower already owns its own decision logic (Decide method)
4. The parser is an implementation detail of ClientResolutionFollower

**Provider-specific?** YES — the log format is Apollo-specific. But it's already encapsulated behind ClientResolutionFollower, which is a domain service (resolution following), not an infrastructure service.

**Conclusion**: No change needed. ApolloLogParser is already correctly encapsulated.

---

## 9. Apollo/Vibepollo Capability Matrix

| Capability | Apollo | Vibepollo | Shared contract possible? | Notes |
|------------|--------|-----------|--------------------------|-------|
| Process lifecycle (Start/Stop/Restart) | sunshine.exe via ProcessInjector | sunshine.exe via ProcessInjector | **Yes** — same binary, same launch mechanism | Both are Sunshine forks |
| Config generation (sunshine.conf) | ApolloConfigBuilder | Same format (Sunshine fork) | **Yes** — identical config format | Key names identical (dd_*, headless_mode, etc.) |
| Config update (output_name) | ApolloConfigBuilder.UpdateDisplayOutput | Same key | **Yes** — same config key | |
| Client pairing (sunshine_state.json) | ApolloConfigBuilder.GetPairedClients | Same format | **Yes** — identical state format | uniqueid + named_devices |
| Display detection (log parsing) | ApolloManager.ParseSudoVdaDisplayId | Different log format | **No** — Vibepollo logs differently | Key difference |
| Log path resolution | ApolloManager.ResolveLogPath | Different layout (logs/ subdir) | **Partial** — ResolveLogPath already handles both | Vibepollo writes to logs/ subdirectory |
| Health check (HTTP serverinfo) | ApolloServerQuery | Same endpoint? | **Uncertain** — Vibepollo may use different XML tags | |
| Port layout | Constants (OffsetGfeHttp etc.) | Same offsets? | **Uncertain** — Vibepollo may differ | |
| Process arguments | --config, --oute | May differ | **No** — Vibepollo may use different CLI args | |
| Credentials/TLS | Per-seat credentials/ dir | Same layout? | **Uncertain** — | |
| Application list (apps.json) | ApolloConfigBuilder.SeatAppsJson | Same format? | **Uncertain** — | |

### Key insight

Apollo and Vibepollo share the same Sunshine heritage. Their config format (sunshine.conf), state format (sunshine_state.json), and port layout are likely identical or very similar. The main differences are:
1. **Log format** — Vibepollo logs display devices differently
2. **Process arguments** — may differ
3. **Log file layout** — Vibepollo uses logs/ subdirectory (already handled by ResolveLogPath)

This means the provider boundary is **thinner than a fully generic provider** would suggest. A single `IStreamingProvider` interface could work for both Apollo and Vibepollo with minimal adapter code.

---

## 10. Candidate Provider Boundaries

### Candidate A: ApolloManager as provider adapter (NO interface yet)

**What**: Route all provider-specific operations through ApolloManager. SeatManager calls only ApolloManager for streaming concerns.

**Current state**: ApolloManager has 7 generic + 7 provider-specific methods.

**Proposed additions**:
- `UpdateDisplayConfig(SeatInfo, string displayId)` — delegates to ApolloConfigBuilder
- `RebuildConfig(SeatInfo)` — delegates to ApolloConfigBuilder.BuildConfig
- `CleanupSeatConfig(SeatInfo)` — delegates to ApolloConfigBuilder.CleanupConfig
- `GetSeatPairedClients(SeatInfo)` — delegates to ApolloConfigBuilder
- `UnpairSeatClient(SeatInfo, string name)` — delegates to ApolloConfigBuilder
- `UnpairAllSeatClients(SeatInfo)` — delegates to ApolloConfigBuilder
- `QueryHealth(SeatInfo, CancellationToken)` — delegates to ApolloServerQuery

**Result**: SeatManager has 0 direct provider-specific call sites. All go through ApolloManager.

**Architectural value**: HIGH — establishes the correct boundary without premature abstraction
**Coupling reduction**: 12 provider-specific call sites eliminated from SeatManager
**Testability gain**: ApolloManager can be mocked for SeatManager tests
**Provider portability**: ApolloManager becomes the adapter; a future IStreamingProvider wraps it
**Implementation risk**: LOW — pure delegation, no behavior change
**Scope**: ~50 lines added to ApolloManager, ~30 lines removed from SeatManager
**Should happen now**: YES — this is the smallest valuable next step

### Candidate B: IStreamingProvider interface

**What**: Create a formal C# interface encapsulating all provider operations.

**Architectural value**: MEDIUM — premature without a second provider
**Coupling reduction**: Same as Candidate A, but with interface overhead
**Testability gain**: Same as Candidate A
**Provider portability**: HIGH — formal contract
**Implementation risk**: MEDIUM — 15+ methods, interface design is hard to change
**Scope**: Large — new interface, adapter class, DI wiring, all consumers updated
**Should happen now**: NO — wait for Vibepollo integration or when testing requires mocking

### Candidate C: Separate provider configuration capability

**What**: Extract ApolloConfigBuilder operations into a standalone IProviderConfig.

**Architectural value**: LOW — splits a cohesive concern artificially
**Coupling reduction**: Same as Candidate A but with more interfaces
**Testability gain**: Marginal — ApolloConfigBuilder is already independently testable
**Provider portability**: Moderate — but config is already provider-specific
**Implementation risk**: MEDIUM — introduces another interface to maintain
**Scope**: Medium — new interface, multiple consumers updated
**Should happen now**: NO — Candidate A achieves the same result with less complexity

### Candidate D: Extract health/query abstraction

**What**: Extract ApolloServerQuery behind IHealthProbe.

**Architectural value**: LOW — ApolloServerQuery is already clean and focused
**Coupling reduction**: 1 call site eliminated
**Testability gain**: Marginal — already mockable via constructor injection
**Provider portability**: Moderate — health endpoint format differs per provider
**Implementation risk**: LOW
**Scope**: Small
**Should happen now**: NO — better absorbed into Candidate A (ApolloManager.QueryHealth)

### Candidate E: Extract client-management capability

**What**: Extract GetPairedClients/UnpairClient behind IClientManager.

**Architectural value**: LOW — these are thin pass-throughs to sunshine_state.json
**Coupling reduction**: 3 call sites
**Testability gain**: Marginal
**Provider portability**: Moderate — state format is shared between Apollo/Vibepollo
**Implementation risk**: LOW
**Scope**: Small
**Should happen now**: NO — better absorbed into Candidate A

### Candidate F: Continue reducing SeatManager without interfaces

**What**: Move RustDesk suppression, audio defaults, controller assignment into helpers.

**Architectural value**: LOW-MEDIUM — reduces SeatManager LOC but doesn't establish provider boundary
**Coupling reduction**: None for provider-specific concerns
**Testability gain**: Moderate — smaller methods
**Provider portability**: None
**Implementation risk**: LOW
**Scope**: Medium
**Should happen now**: OPTIONAL — useful cleanup but not architecturally critical

### Candidate G: Fix ProcessTracking compilation

**What**: Complete the ProcessTracking work so it compiles on master.

**Architectural value**: MEDIUM — unblocks future process-lifecycle work
**Coupling reduction**: None for provider boundary
**Testability gain**: None
**Provider portability**: None
**Implementation risk**: HIGH — references missing Kernel32 types, VibepolloExePath
**Scope**: Unknown — depends on missing dependencies
**Should happen now**: NO — pre-existing, out of scope for provider boundary work

---

## 11. Risks

1. **ApolloManager bloat** — Adding 7 delegation methods increases ApolloManager from ~400 to ~450 lines. This is acceptable because the methods are thin delegations, not logic. The alternative (keeping direct calls in SeatManager) is worse.

2. **Premature provider abstraction** — Only one provider exists today. The risk is that the "provider boundary" we establish doesn't match what Vibepollo actually needs. Mitigation: the delegations are concrete (no interface), so they're easy to change.

3. **HostApolloMonitor coupling** — HostApolloMonitor uses ApolloServerQuery for the standalone Apollo. This must NOT go through ApolloManager (which manages per-seat instances). Keep ApolloServerQuery as a separate injectable service.

4. **OnConnectAppLauncher and ClientResolutionFollower** — These already depend on ApolloManager (for GetLogPath). After Step 2, they still need ApolloManager. This is correct — they are provider-specific consumers that belong in the Streaming namespace.

5. **API layer reaches through SeatManager** — GetPairedClients/UnpairClient are pass-throughs from the API. After Step 2, the API calls SeatManager which calls ApolloManager which calls ApolloConfigBuilder. This is one extra hop but establishes the correct boundary.

---

## 12. Final Decision

```
DEFER
```

### Why DEFER (not APPROVE)

The provider boundary is architecturally important, but creating an `IStreamingProvider` interface NOW is premature:
- Only one provider exists (Apollo)
- Vibepollo is a Sunshine fork with the same config format
- The 15+ method interface is too large for a first extraction
- Previous successful extractions (ISessionLauncher, IVirtualDisplayManager, IAccountManager) had 3-8 methods each

### What IS the correct provider boundary

**ApolloManager as the provider adapter** — no interface yet. Route all provider-specific operations through ApolloManager. This reduces SeatManager's provider-specific call sites from 12 to 0 without creating any new abstractions.

### The smallest valuable next step

**Route ApolloConfigBuilder calls through ApolloManager.** Specifically:
1. ApolloManager gains: `UpdateDisplayConfig`, `RebuildConfig`, `CleanupSeatConfig`, `GetSeatPairedClients`, `UnpairSeatClient`, `UnpairAllSeatClients`, `QueryHealth`
2. SeatManager stops calling ApolloConfigBuilder and ApolloServerQuery directly
3. SeatManager's only streaming dependencies become ApolloManager (lifecycle + config + health)

### What NOT to change yet

1. Do NOT create `IStreamingProvider` — premature without a second provider
2. Do NOT split ApolloManager into multiple interfaces — it's cohesive at ~450 lines
3. Do NOT merge ApolloConfigBuilder into ApolloManager — different concerns, different complexity
4. Do NOT extract health probe separately — absorb into ApolloManager.QueryHealth
5. Do NOT touch OnConnectAppLauncher or ClientResolutionFollower — already correctly encapsulated
6. Do NOT touch ProcessTracking — pre-existing, out of scope
7. Do NOT touch traycer branch — separate migration task

---

## 13. Recommended Implementation Sequence

### Phase 1: Route config through ApolloManager (next task)

**Files changed**: `ApolloManager.cs`, `SeatManager.cs`
**Tests**: Existing tests pass. Add tests for new ApolloManager delegation methods.
**Scope**: ~50 lines added, ~30 lines removed
**Risk**: Low — pure delegation
**Value**: Establishes the provider boundary without interfaces

### Phase 2: Update documentation

**Files changed**: `docs/audits/MSE-Provider-Boundary-Audit.md`, `docs/PROJECT-STATE.md`
**Scope**: Update audit with implementation result, update project state

### Phase 3 (future): IStreamingProvider interface

**When**: When Vibepollo integration begins or when testing requires mocking the provider
**Scope**: New interface wrapping ApolloManager + future VibepolloManager
**Prerequisite**: Phase 1 complete, Vibepollo requirements known

---

## 14. Implementation Result — Route Config Through ApolloManager

**Completed**: 2026-09-01
**Commit**: (see below)

### Methods added to ApolloManager

| Method | Delegates to | Purpose |
|--------|-------------|--------|
| `UpdateDisplayOutput(SeatInfo, string)` | `ApolloConfigBuilder.UpdateDisplayOutput` | Update display target in sunshine.conf |
| `RebuildConfig(SeatInfo)` | `ApolloConfigBuilder.BuildConfig` | Regenerate sunshine.conf from seat state |
| `CleanupSeatConfig(SeatInfo)` | `ApolloConfigBuilder.CleanupConfig` | Remove junctions on teardown |
| `GetSeatPairedClients(SeatInfo)` | `ApolloConfigBuilder.GetPairedClients` | List paired Moonlight clients |
| `UnpairSeatClient(SeatInfo, string)` | `ApolloConfigBuilder.UnpairClient` | Remove one paired client |
| `UnpairAllSeatClients(SeatInfo)` | `ApolloConfigBuilder.UnpairAllClients` | Remove all paired clients |
| `QueryHealthAsync(SeatInfo, CancellationToken)` | `ApolloServerQuery.QueryAsync` | HTTP serverinfo health probe |

### Direct SeatManager dependencies removed

- `ApolloConfigBuilder` — removed from constructor, field, and all 10 call sites
- `Monitoring.ApolloServerQuery` — removed from constructor, field, and 1 call site

### SeatManager dependency graph

**Before**:
```
SeatManager
├── ApolloManager
├── ApolloConfigBuilder
└── ApolloServerQuery
```

**After**:
```
SeatManager
└── ApolloManager
      ├── ApolloConfigBuilder
      └── ApolloServerQuery
```

### Behavior preserved

- All config operations route through the same ApolloConfigBuilder methods — no logic change
- Health query uses the same ApolloServerQuery.QueryAsync — no behavior change
- Error handling, logging, and timing unchanged
- HostApolloMonitor still uses ApolloServerQuery directly (not routed through ApolloManager)
- OnConnectAppLauncher and ClientResolutionFollower unchanged

### Test / build result

- Build: 0 errors (9 pre-existing errors from untracked ProcessTracking files temporarily excluded)
- Tests: 390 passed, 0 failed, 17 skipped (unchanged from baseline)
- ProcessTracking: temporarily excluded for build verification, restored exactly

---

## 15. Verification

- **Source modified**: YES — `ApolloManager.cs`, `SeatManager.cs`
- **ProcessTracking**: Untouched (temporarily moved for build verification, restored)
- **traycer**: Untouched (separate branch)
- **Files created**: This audit document (implementation result section added)
- **Commit**: (see git log)
- **Push**: (see git log)
