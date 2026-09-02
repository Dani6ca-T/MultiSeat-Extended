# Display Detection Refactoring Proposal

**Date**: 2026-08-31
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: d27ad1f
**Source modified**: NO

---

## Executive Summary

The SeatManager audit proposed extracting a `DisplayUuidResolver` to consolidate "duplicated" display detection logic, claiming ~80 lines of duplication and ~15% of SeatManager. **This proposal is overstated.** After tracing every display detection code path in the repository, the actual shared orchestration code is ~3 lines, not 80. The two detection paths (provisioning vs late detection) have deliberately different semantics — different log block parsing, different Apollo restart behavior — and cannot be trivially unified. The highest-value next change is **not** display detection extraction. The audit document's recommendation should be revised.

---

## Current Implementation

### What "display detection" means in this codebase

Display detection is the process of discovering the SudoVDA virtual display's UUID (`device_id`) from Apollo's log file. Apollo enumerates all displays at startup and when a client connects, writing a JSON block to its log containing each display's `device_id`, `friendly_name`, `primary` flag, and `refresh_rate`. SudoVDA shows up as `"VDD by MTT"` in the `friendly_name`, or is identified by its 1000Hz refresh rate when the friendly name is empty.

The UUID is critical: Apollo's `output_name` config key must be set to the UUID (not the GDI path `\\.\DISPLAYx`) or Apollo falls back to the primary monitor.

### Code Path 1: Provisioning (SeatManager.ProvisionSeatAsync, step 6.5)

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines ~313-365

**What it does**:
1. Gets log path: `_apolloManager.GetLogPath(seat.AccountName, _options.ApolloConfigDir)`
2. Waits 5000ms for Apollo to initialize SudoVDA IPC
3. Parses FIRST display block: `_apolloManager.ParseSudoVdaDisplayId(logPath)` — instance method that reads the entire file, calls `ParseSudoVdaDisplayIdFromLogText(text)` on the full text
4. If UUID found:
   - Sets `seat.DisplayDevicePath = displayId`
   - Calls `_configBuilder.UpdateDisplayOutput(configPath, displayId)` — writes `output_name` to sunshine.conf
   - Restarts Apollo (`_apolloManager.Stop` → delay 2000ms → `_apolloManager.StartAsync`)
   - Calls `ApplyDisplayIsolationAsync(seat, ct)` — runs helper CLI to make SudoVDA primary
5. If not found: logs Debug "expected at this point" (Apollo hasn't created the display yet at startup)

**Key detail**: Parses the FIRST display block. Apollo's startup enumeration never contains the virtual display — it's created when a client connects. This path almost always finds nothing and relies on Path 2 (late detection) to succeed later.

### Code Path 2: Late Detection (SeatManager.TryLateDisplayDetectionAsync)

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines ~614-660

**What it does**:
1. Guard: skip if `DisplayDevicePath` already set or Apollo not running
2. Gets log path: `_apolloManager.GetLogPath(seat.AccountName, _options.ApolloConfigDir)`
3. Reads entire log file manually (does NOT use `ParseSudoVdaDisplayId`)
4. Finds LAST "Currently available display devices:" marker: `text.LastIndexOf(marker)`
5. Parses LAST display block: `ApolloManager.ParseSudoVdaDisplayIdFromLogText(text[last..])` — static method on text from last marker
6. If UUID found:
   - Sets `seat.DisplayDevicePath = result.DeviceId`
   - Calls `_configBuilder.UpdateDisplayOutput(configPath, result.DeviceId)`
   - Calls `ApplyDisplayIsolationAsync(seat, ct)`
   - Does NOT restart Apollo
7. Returns true/false

**Key detail**: Parses the LAST display block. This is the block Apollo writes when a client connects and creates the virtual display — the only time the UUID is actually present.

### Code Path 3: ApplyDisplayIsolationAsync (post-detection action)

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines ~677-720

**Not detection** — this runs after detection succeeds. Calls helper CLI to make SudoVDA primary and shrink RDP to 640×480. Called from:
- Provisioning (step 6.6/6.7) — after Path 1 detects UUID
- TryLateDisplayDetectionAsync — after Path 2 detects UUID
- StartApolloAsync / RestartApolloAsync — re-applies isolation (state doesn't survive restart)
- SessionHealthCheck — re-applies after sleep-reconnect or crash restart

### Code Path 4: Config update on Apollo restart

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines ~560-590

Not detection — re-applies `UpdateDisplayOutput` with the known `DisplayDevicePath` after Apollo restart. Used in `StartApolloAsync` and `RestartApolloAsync`.

### Code Path 5: ResetDisplayAsync

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines ~900-910

Not detection — destroys and recreates the display, then re-applies config. Called from API.

### Other display-related code in the repository

| File | What it does | Uses detection? |
|------|-------------|-----------------|
| `ApolloManager.ParseSudoVdaDisplayId` | Reads Apollo log, parses FIRST display block for SudoVDA UUID | Yes — the parsing logic lives here |
| `ApolloManager.ParseSudoVdaDisplayIdFromLogText` | Static: parses text for SudoVDA UUID (pure, testable) | Yes — the core parsing logic |
| `ApolloManager.ResolveLogPath` | Finds the newest non-empty `apollo*.log` file | No — log file resolution, not display detection |
| `VirtualDisplayManager` | Tracks display assignments, checks PnP registry for driver | No — different concern (driver detection, not UUID discovery) |
| `DisplayModeHelper.SetupDisplayIsolation` | Makes SudoVDA primary, shrinks RDP — runs in seat session | No — post-detection action, uses already-known UUID |
| `DisplayEnumeratorHelper.EnumerateAllPaths` | Enumerates all displays via QueryDisplayConfig | No — diagnostic enumeration, not UUID parsing |
| `OnConnectAppLauncher` | Tails Apollo log for CLIENT CONNECTED/DISCONNECTED | No — reads same log but for different markers |
| `ClientResolutionFollower` | Reads Apollo log for last requested resolution mode | No — reads same log but for different data |

---

## Repository-Wide Duplication

### Claimed duplication: "80 lines duplicated between provisioning and late detection"

**Verdict: NOT DUPLICATED. Overstated.**

Here is what is actually shared between Path 1 (provisioning) and Path 2 (late detection):

| Shared code | Provisioning | Late Detection | Actually identical? |
|-------------|-------------|----------------|---------------------|
| Get log path | `_apolloManager.GetLogPath(...)` | `_apolloManager.GetLogPath(...)` | Yes — 1 line |
| Read log file | Delegated to `ParseSudoVdaDisplayId` (instance method reads file) | Manual `FileStream` + `StreamReader` + `ReadToEndAsync` | **No** — different implementations |
| Find display block | First block (implicit in `ParseSudoVdaDisplayId`) | `text.LastIndexOf(marker)` then slice | **No** — deliberately different |
| Parse for UUID | `ParseSudoVdaDisplayIdFromLogText(text)` | `ParseSudoVdaDisplayIdFromLogText(text[last..])` | Same method, different input |
| Set DisplayDevicePath | `seat.DisplayDevicePath = displayId` | `seat.DisplayDevicePath = result.DeviceId` | Yes — 1 line |
| Update config | `_configBuilder.UpdateDisplayOutput(configPath, displayId)` | `_configBuilder.UpdateDisplayOutput(configPath, result.DeviceId)` | Yes — 1 line |
| Apply isolation | `ApplyDisplayIsolationAsync(seat, ct)` | `ApplyDisplayIsolationAsync(seat, ct)` | Yes — 1 line |
| Restart Apollo | Yes (Stop → delay → StartAsync) | **No** | **Different behavior** |
| Delay before detection | 5000ms | None | **Different behavior** |
| Guard conditions | None | `DisplayDevicePath` already set, Apollo not running | **Different behavior** |

**The genuinely shared post-detection sequence is 3 lines**:
```csharp
seat.DisplayDevicePath = uuid;
_configBuilder.UpdateDisplayOutput(configPath, uuid);
await ApplyDisplayIsolationAsync(seat, ct);
```

These appear at:
- Provisioning: line 340-341, 354
- Late detection: line 648, 651, 657

That is 3 lines × 2 call sites = 6 lines total. Not 80.

### What the ~80 lines actually contain

**Provisioning step 6.5** (~50 lines):
- 15 lines of comments explaining why detection happens here
- 1 line: get log path
- 1 line: get config path
- 1 line: Task.Delay(5000)
- 1 line: ParseSudoVdaDisplayId
- 3 lines: set path + update config
- 3 lines: log
- 5 lines: restart Apollo (Stop + delay + Start)
- 1 line: ApplyDisplayIsolationAsync
- 5 lines: else branch (log "expected at this point")

**TryLateDisplayDetectionAsync** (~45 lines):
- 15 lines of comments explaining retry semantics
- 2 lines: guard conditions
- 1 line: get log path
- 1 line: File.Exists check
- 4 lines: FileStream + StreamReader + ReadToEndAsync
- 1 line: LastIndexOf marker
- 1 line: ParseSudoVdaDisplayIdFromLogText on last block
- 3 lines: set path + update config
- 1 line: log
- 1 line: ApplyDisplayIsolationAsync

The "duplication" is in the comments and the 3-line post-detection sequence. The actual I/O and parsing are different.

### Semantic duplication?

**No.** The two paths serve different lifecycle phases:
- **Provisioning**: Runs once at seat creation. Parses the FIRST display block (startup enumeration). Almost always finds nothing. Restarts Apollo if found.
- **Late detection**: Runs on every health-check tick until the display is found. Parses the LAST display block (post-client-connect). Finds the display after a client connects.

These are two different operations that happen to share 3 lines of post-detection housekeeping.

---

## Responsibility Boundary

The audit proposed naming this `DisplayUuidResolver`. Let me evaluate the actual responsibility boundaries:

### What the proposed resolver would need to do

```
Input: SeatInfo, Apollo log path, ApolloManager, ApolloConfigBuilder, session launcher
Output: bool (found or not)
Side effects: Sets seat.DisplayDevicePath, updates Apollo config, applies display isolation
```

### Which component already owns the parsing?

**ApolloManager** already owns:
- `ParseSudoVdaDisplayId(string logPath)` — reads file, parses first block
- `ParseSudoVdaDisplayIdFromLogText(string text)` — static, parses text
- `SudoVdaParseResult` record
- `IsSudoVdaFriendlyName` private method

The parsing is already in the right place — it's Apollo-specific (reads Apollo's log format) and lives on ApolloManager.

### What the proposed resolver would actually extract

The resolver would extract the 3-line post-detection sequence:
```csharp
seat.DisplayDevicePath = uuid;
_configBuilder.UpdateDisplayOutput(configPath, uuid);
await ApplyDisplayIsolationAsync(seat, ct);
```

This is not "display detection" — it's "apply display configuration after detection." And it's 3 lines.

### What would actually reduce coupling

The real coupling problem is **SeatManager → ApolloManager** for `GetLogPath` and `ParseSudoVdaDisplayId`. But:
- `GetLogPath` is called 4 times across the codebase (SeatManager ×2, OnConnectAppLauncher, ClientResolutionFollower)
- `ParseSudoVdaDisplayId` is called 1 time (provisioning)
- `ParseSudoVdaDisplayIdFromLogText` is called 1 time (late detection)

Moving these to a new class doesn't reduce coupling — it just adds indirection.

---

## Dependencies

| Dependency | Required for detection? | Windows-specific? | Already abstracted? |
|------------|------------------------|-------------------|---------------------|
| ApolloManager (GetLogPath) | Yes — resolves which log file to read | No | No (concrete) |
| ApolloManager (ParseSudoVdaDisplayId) | Yes — parses Apollo's log format | No | No (concrete) |
| ApolloManager (ParseSudoVdaDisplayIdFromLogText) | Yes — static parse method | No | No (concrete, static) |
| ApolloManager (GetConfigPath) | Yes — config file path for UpdateDisplayOutput | No | No (concrete) |
| ApolloConfigBuilder (UpdateDisplayOutput) | Yes — writes output_name to sunshine.conf | No | No (concrete) |
| ISessionLauncher (RunHelperInSeatSession) | Yes — runs display isolation helper in seat session | **Yes** | Yes (interface) |
| MultiSeatOptions (ApolloConfigDir) | Yes — config directory path | No | No (IOptions) |
| SeatInfo (DisplayDevicePath) | Yes — stores discovered UUID | No | No (model) |

A resolver would inherit all of these dependencies. It would not reduce SeatManager's dependency count — it would just move the dependency to a new class.

---

## Existing Tests

### Display detection tests (in `src/MultiSeat.Tests/Streaming/StreamingTests.cs`):

| Test | What it tests | Lines |
|------|--------------|-------|
| `ParseSudoVda_DoesNotMistakeTheLoneRdpSurfaceForSudoVda` | Single 1000Hz display → returns null | 927-940 |
| `ParseSudoVda_PicksTheNonPrimary1000HzDisplayAlongsideTheRdpSurface` | Two displays, non-primary 1000Hz → returns UUID | 944-958 |
| `ParseSudoVda_PrefersAnExplicitFriendlyNameOverTheFallback` | "VDD by MTT" name → returns UUID | 961-972 |
| `ParseSudoVda_IgnoresTheResolutionScaleNumerator` | 100-scale display at 60Hz → not mistaken for 1000Hz | 976-989 |
| `ParseSudoVda_ReturnsNothingWhenTheLogHasNoDisplayBlock` | No display block → returns null | 993-998 |
| `SeatInfo_DisplayDevicePath_DefaultsToNull` | Model default | 684-688 |
| `VirtualDisplay_Record_StoresAllFields` | Record construction | 693-710 |
| `VirtualDisplay_NullDevicePath_IndicatesDegradedMode` | Null path | 715-720 |
| `VirtualDisplayManager_CreatesAndDestroysDisplay` | Skipped — requires SudoVDA driver | 795-798 |
| `ResolveLogPath_*` (5 tests) | Log file resolution | 810-870 |

### What can be tested without hardware

- `ParseSudoVdaDisplayIdFromLogText` — **already tested** (5 tests, pure static method)
- `ResolveLogPath` — **already tested** (5 tests, file system only)
- `ApolloLogParser.ParseLastRequestedMode` — tested in ClientResolutionFollower tests

### What cannot be tested without hardware

- `ParseSudoVdaDisplayId` (instance method) — reads real files, but the static version is already tested
- `SetupDisplayIsolation` — requires real displays
- `VirtualDisplayManager.CreateDisplayAsync` — requires SudoVDA driver

### Test seam needed for extraction

If the resolver were extracted, the only new test seam would be the orchestration (read → parse → set path → update config → apply isolation). This is already testable by mocking `ApolloManager` and `ApolloConfigBuilder` — but those are concrete classes, so mocking requires an interface extraction that doesn't exist yet.

---

## Proposed Component

### What the audit proposed: `DisplayUuidResolver`

```csharp
public class DisplayUuidResolver
{
    public Task<bool> ResolveAsync(SeatInfo seat, CancellationToken ct);
}
```

### What it would actually contain

```csharp
public class DisplayUuidResolver
{
    private readonly ApolloManager _apolloManager;
    private readonly ApolloConfigBuilder _configBuilder;
    private readonly ISessionLauncher _sessionLauncher;
    private readonly ILogger _logger;
    private readonly MultiSeatOptions _options;

    // Provisioning path: parse first block, restart Apollo if found
    public async Task<bool> ResolveDuringProvisioningAsync(SeatInfo seat, CancellationToken ct);

    // Late detection path: parse last block, no restart
    public async Task<bool> ResolveLateAsync(SeatInfo seat, CancellationToken ct);

    // Shared post-detection: set path + update config + apply isolation
    private async Task ApplyAfterDetectionAsync(SeatInfo seat, string uuid, CancellationToken ct);
}
```

### What this actually achieves

- Moves ~50 lines from provisioning + ~45 lines from late detection into a new class
- The new class has the same dependencies as SeatManager (ApolloManager, ApolloConfigBuilder, ISessionLauncher)
- SeatManager's dependency count: 17 → 17 (no reduction — resolver replaces inline code, not dependencies)
- SeatManager loses direct calls to `ParseSudoVdaDisplayId` and `ParseSudoVdaDisplayIdFromLogText` — but gains a call to `ResolveAsync`
- The 3-line shared sequence is extracted into `ApplyAfterDetectionAsync`

### What this does NOT achieve

- Does not reduce SeatManager's dependency count
- Does not reduce coupling to ApolloManager (resolver depends on it)
- Does not improve testability (resolver depends on concrete classes)
- Does not prepare for provider abstraction (parsing is already on ApolloManager)
- Does not eliminate duplication (there are 3 shared lines, not 80)

---

## Architecture Placement

### Where does this belong?

The display detection is:
- **Not Core** — it reads Apollo's log format (provider-specific)
- **Not Infrastructure** — it's not a system service
- **Not pure Application** — it's provider-specific parsing
- **Provider-adjacent** — it reads the provider's log to discover a display UUID

In the RFC-MSE-0001 target architecture, this would belong in the **Provider** layer — the provider adapter owns its own display detection. But there is no provider abstraction yet.

### Would introducing a resolver create Windows-specific types in Core?

No — the resolver would live in `MultiSeat.Service`, not `MultiSeat.Shared`. But it wouldn't be in Core either. It would be another concrete class in the Service layer, same as ApolloManager.

### Does this belong in IVirtualDisplayManager?

`IVirtualDisplayManager` currently handles display lifecycle (create/destroy) and driver detection (IsDriverAvailable). Adding display UUID resolution would conflate two concerns:
- Display lifecycle (driver-side, infrastructure)
- Display UUID discovery (log parsing, provider-specific)

These are different boundaries. The display manager knows about the driver; the UUID resolver knows about Apollo's log format.

---

## Scope

### Files to add
- `src/MultiSeat.Service/Display/DisplayUuidResolver.cs` (new class, ~80 lines)

### Files to modify
- `src/MultiSeat.Service/Sessions/SeatManager.cs` — replace inline detection with resolver calls

### Files to test
- `src/MultiSeat.Tests/Streaming/StreamingTests.cs` — add resolver tests (mock ApolloManager)

### Expected behavior changes
- None — pure extraction

### Expected behavior
- Identical: provisioning still parses first block, late detection still parses last block
- Same delays, same restart behavior, same config updates

### Risk
- Low — pure extraction, no behavior change

### But...

The resolver would depend on:
- `ApolloManager` (concrete) — for GetLogPath, ParseSudoVdaDisplayId, GetConfigPath
- `ApolloConfigBuilder` (concrete) — for UpdateDisplayOutput
- `ISessionLauncher` (interface) — for RunHelperInSeatSession

That's 2 more concrete dependencies in a new class. We're not reducing coupling — we're moving it.

---

## Risks

### Risk 1: Over-engineering for 3 lines

The shared post-detection sequence is:
```csharp
seat.DisplayDevicePath = uuid;
_configBuilder.UpdateDisplayOutput(configPath, uuid);
await ApplyDisplayIsolationAsync(seat, ct);
```

Extracting 3 lines into a new class with 3 constructor dependencies is not a net improvement. The indirection cost outweighs the deduplication benefit.

### Risk 2: Masking the real coupling

The real coupling problem is SeatManager → ApolloManager (14 concrete dependencies). Creating a new class that depends on ApolloManager doesn't help — it just adds a layer.

### Risk 3: Divergent paths becoming artificially unified

If a resolver tries to unify provisioning and late detection into one method, it needs to handle:
- First block vs last block (different parsing targets)
- Restart Apollo vs don't restart (different side effects)
- Delay 5000ms vs no delay (different timing)

This would require parameters/flags that make the unified method harder to understand than the two separate, clear methods.

---

## Architectural Value

| Dimension | Rating | Explanation |
|-----------|--------|-------------|
| SeatManager coupling | **Low** | Resolver depends on same concrete classes. No dependency reduction. |
| Windows-specific isolation | **Low** | Detection is not Windows-specific (reads log files, parses JSON). |
| Testability | **Low** | `ParseSudoVdaDisplayIdFromLogText` is already a tested static method. |
| Future Provider architecture | **Low** | Detection is already on ApolloManager (provider-specific). Moving it to a new class doesn't help. |
| Future Vibepollo integration | **Low** | Vibepollo would have its own log format. The resolver would need Vibepollo-specific parsing, not shared logic. |
| Code duplication | **Low** | 3 shared lines × 2 call sites = 6 lines total. Not 80. |

---

## Comparison with RFC-MSE-0001

The RFC gap analysis identified:

| RFC Concept | Display Detection Status |
|-------------|------------------------|
| **VirtualDisplayHandle** | `seat.DisplayDevicePath` (string) — raw, not typed |
| **Provider lifecycle** | Detection is on ApolloManager (concrete) |
| **Provider contract** | Detection is provider-specific (reads Apollo's log format) |

**Would a resolver help with these?**

- `VirtualDisplayHandle`: No — the resolver would still store a string in `seat.DisplayDevicePath`
- Provider lifecycle: No — the resolver would depend on ApolloManager
- Provider contract: No — the resolver is provider-specific, not a contract

---

## Decision

```
DO NOT IMPLEMENT
```

**Rationale**: The proposed extraction solves a problem that doesn't exist at the claimed scale. The actual shared code is 3 lines, not 80. The two detection paths have deliberately different semantics. Extracting them into a new class that depends on the same concrete dependencies provides no architectural value. The SeatManager audit's recommendation should be revised.

**What would actually be valuable instead**:

1. **Extract AccountManager behind an interface** — Used in 4+ places, deeply Windows-specific (NetApi32 P/Invoke), most testable win
2. **Remove Service Locator pattern** — InputRouter, InputHookManager, ApolloManager as public properties on SeatManager; inject directly into API endpoints
3. **Make ProcessInjector an interface** — Used in 6+ places (Apollo launch, app launch, helper CLI, VoiceMeeter), deepest Windows coupling

---

## Revised Recommendation from SeatManager Audit

The SeatManager audit stated:

> "The highest-value next small change is extracting the SudoVDA display detection loop... reducing SeatManager by ~80 lines and removing its direct dependence on ApolloManager.GetLogPath/ParseSudoVdaDisplayId for display UUID discovery."

**This should be revised to**:

> "The highest-value next small change is extracting AccountManager behind an interface (IAccountManager). AccountManager is used in 4+ places, deeply Windows-specific (NetApi32 P/Invoke for account CRUD, DPAPI for credentials, SecurityIdentifier for group membership), and is the most testable win — interface extraction enables mocking Windows account operations in seat lifecycle tests."

---

## Final Report

```
Decision:                    DO NOT IMPLEMENT
Proposed component:          DisplayUuidResolver
Current duplication:         3 lines × 2 call sites (NOT 80 lines)
Files affected:              2 (SeatManager.cs + new resolver)
Estimated scope:             ~80 lines new, ~50 lines moved
Risk:                        Low (but no value either)
Tests available:             ParseSudoVdaDisplayIdFromLogText already tested (5 tests)
Architecture layer:          Provider-adjacent (Service layer, not Core)
Vibepollo impact:            None — different log format would need separate implementation

Audit:                       docs/audits/MSE-DisplayDetection-Refactoring-Proposal.md
Source modified:             NO
ProcessTracking modified:    NO
traycer modified:            NO
Commit:                      NONE
Push:                        NONE
```
