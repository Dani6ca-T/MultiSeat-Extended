# ProcessInjector Boundary Audit

**Date**: 2026-08-31
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: 0d3d02d
**Source modified**: NO

---

## Executive Summary

ProcessInjector is a pure Windows infrastructure class — every method directly calls `CreateProcessAsUserW`, manipulates tokens (`WTSQueryUserToken`, `DuplicateTokenEx`, `GetTokenInformation`, `SetTokenInformation`), manages environment blocks, and handles process lifecycle (`WaitForSingleObject`, `TerminateProcess`). It has 5 public methods, all used by 5 production consumers. While a `IProcessInjector` interface could technically be created (the public API doesn't expose `SafeTokenHandle`), the interface would be a thin wrapper over inherently Windows-specific operations. The parameters are Windows session IDs, the return values are PIDs, and the implementation depends on the concrete `SessionLauncher` for token acquisition. The testability gain is moderate — consumers can't meaningfully mock process creation because the contract IS Windows process creation.

**Decision: DEFER** — IProcessInjector is valid but not the highest-value next step. Service Locator removal provides better value per unit of work.

---

## ProcessInjector Responsibilities

### Public API

| Method | Return Type | Purpose |
|--------|-------------|---------|
| `LaunchInSessionAsync(sessionId, accountName, exePath, arguments, workingDir, ct, allowConsoleSession)` | `Task<int>` | Launch process in target Windows session |
| `LaunchApolloInSessionAsync(sessionId, accountName, apolloExePath, configPath, ct)` | `Task<int>` | Launch Apollo with specific config/working dir |
| `LaunchInConsoleSessionAsync(exePath, arguments, ct)` | `Task<int>` | Launch process in console session (GUI apps) |
| `RunInConsoleSessionAsync(exePath, arguments, timeoutMs, ct)` | `Task<int>` | Launch + wait for exit (helper processes) |
| `LaunchApolloInConsoleSessionAsync(apolloExePath, configPath, ct)` | `Task<int>` | Launch Apollo in console session (GPU access) |

### Internal Static Methods

| Method | Purpose |
|--------|---------|
| `EnsureNotConsoleSession(sessionId, consoleSessionId, exePath, allowConsoleSession)` | Guard against wrong-session launches |

### Windows API Dependencies

| API | Used for |
|-----|----------|
| `AdvApi.CreateProcessAsUserW` | Core process creation in target session |
| `AdvApi.GetTokenInformation` | Verify token session ID |
| `AdvApi.SetTokenInformation` | Re-stamp token if session ID mismatch |
| `AdvApi.GetTokenInformationHandle` | Get linked filtered token |
| `AdvApi.DuplicateTokenEx` | Create primary token from session token |
| `UserEnv.CreateEnvironmentBlock` | Build user environment |
| `UserEnv.DestroyEnvironmentBlock` | Cleanup environment block |
| `WtsApi.WTSQueryUserToken` | Acquire session token |
| `Kernel32.WaitForSingleObject` | Wait for process startup/exit |
| `Kernel32.GetExitCodeProcess` | Read process exit code |
| `Kernel32.TerminateProcess` | Kill mis-targeted process |
| `Kernel32.ProcessIdToSessionId` | Verify process landed in correct session |
| `Kernel32.CloseHandle` | Cleanup handles |

### Key Internal Dependency

ProcessInjector depends on `SessionLauncher` (concrete) for `GetSessionToken()`. This method:
- Returns `SafeTokenHandle` (Windows handle wrapper)
- Is NOT on `ISessionLauncher` — deliberately excluded because `SafeTokenHandle` is Windows-specific
- Is the primary mechanism for acquiring session tokens

This creates a tight coupling: ProcessInjector → SessionLauncher (concrete) → Windows token APIs.

### Mutable State

None — ProcessInjector is stateless. All state is per-call (token, environment block, process handles).

---

## Consumers

### Consumer 1: SeatManager

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines 40, 60, 440

**Members used**:
- `LaunchInSessionAsync` — launch apps inside active seat sessions (line 440)

**Concrete dependency required?** Yes — SeatManager directly calls `_processInjector.LaunchInSessionAsync()`.

### Consumer 2: ApolloManager

**Location**: `src/MultiSeat.Service/Streaming/ApolloManager.cs`, lines 19, 43, 52, 101, 224

**Members used**:
- `LaunchApolloInSessionAsync` — launch Apollo in seat session (lines 101, 224)

**Concrete dependency required?** Yes — ApolloManager directly calls `_processInjector.LaunchApolloInSessionAsync()`.

### Consumer 3: OnConnectAppLauncher

**Location**: `src/MultiSeat.Service/Streaming/OnConnectAppLauncher.cs`, lines 44, 52, 143

**Members used**:
- `LaunchInSessionAsync` — launch apps on CLIENT CONNECTED (line 143)

**Concrete dependency required?** Yes — OnConnectAppLauncher directly calls `_injector.LaunchInSessionAsync()`.

### Consumer 4: AudioRouter

**Location**: `src/MultiSeat.Service/Audio/AudioRouter.cs`, lines 35, 119, 301

**Members used**:
- `LaunchInConsoleSessionAsync` — start VoiceMeeter in console session (line 301)

**Concrete dependency required?** Yes — AudioRouter directly calls `_processInjector.LaunchInConsoleSessionAsync()`.

### Consumer 5: VirtualDisplayManager

**Location**: `src/MultiSeat.Service/Display/VirtualDisplayManager.cs`, lines 27, 39, 149

**Members used**:
- `RunInConsoleSessionAsync` — run --enum-displays helper in console session (line 149)

**Concrete dependency required?** Yes — VirtualDisplayManager directly calls `_processInjector.RunInConsoleSessionAsync()`.

### Consumer 6: Tests

**Location**: `src/MultiSeat.Tests/Sessions/SessionGuardTests.cs`, `src/MultiSeat.Tests/Sessions/ProcessInjectorTests.cs`

**Members used**:
- `EnsureNotConsoleSession` — 6 tests for session guard logic
- Kernel32/AdvApi constant verification — 7 tests

**Note**: Tests call `internal static` methods and verify P/Invoke constants. They don't test the instance API.

---

## Current Coupling

```
SeatManager ──────── ProcessInjector (concrete)
  └─ LaunchInSessionAsync()

ApolloManager ────── ProcessInjector (concrete)
  └─ LaunchApolloInSessionAsync()

OnConnectAppLauncher ── ProcessInjector (concrete)
  └─ LaunchInSessionAsync()

AudioRouter ──────── ProcessInjector (concrete)
  └─ LaunchInConsoleSessionAsync()

VirtualDisplayManager ── ProcessInjector (concrete)
  └─ RunInConsoleSessionAsync()

ProcessInjector ──── SessionLauncher (concrete)
  └─ GetSessionToken() [NOT on ISessionLauncher]
```

All 5 production consumers depend on the concrete class. No abstraction exists.

---

## Candidate Interface

```csharp
namespace MultiSeat.Service.Sessions;

/// <summary>
/// Launches executables inside Windows sessions.
/// </summary>
public interface IProcessInjector
{
    Task<int> LaunchInSessionAsync(
        int sessionId, string accountName, string exePath,
        string? arguments = null, string? workingDir = null,
        CancellationToken ct = default, bool allowConsoleSession = false);

    Task<int> LaunchApolloInSessionAsync(
        int sessionId, string accountName, string apolloExePath,
        string configPath, CancellationToken ct);

    Task<int> LaunchInConsoleSessionAsync(
        string exePath, string? arguments, CancellationToken ct);

    Task<int> RunInConsoleSessionAsync(
        string exePath, string? arguments, int timeoutMs, CancellationToken ct);

    Task<int> LaunchApolloInConsoleSessionAsync(
        string apolloExePath, string configPath, CancellationToken ct);
}
```

### Member justification

| Member | Consumers | Windows-specific types? | Infrastructure details? |
|--------|-----------|------------------------|------------------------|
| `LaunchInSessionAsync` | SeatManager, OnConnectAppLauncher | Parameters: `int sessionId` (Windows session ID) | Yes — process creation is infrastructure |
| `LaunchApolloInSessionAsync` | ApolloManager | Parameters: `int sessionId` | Yes — Apollo-specific launch |
| `LaunchInConsoleSessionAsync` | AudioRouter | None beyond session ID | Yes — console session launch |
| `RunInConsoleSessionAsync` | VirtualDisplayManager | None beyond session ID | Yes — helper process execution |
| `LaunchApolloInConsoleSessionAsync` | (unused by production consumers) | None beyond session ID | Yes — Apollo console launch |

**Result**: No Windows-specific types in the interface surface (no `SafeTokenHandle`, no `IntPtr`). But the parameters are inherently Windows-specific — `int sessionId` is a Windows session ID, not a domain concept.

---

## Architecture Placement

### Where does IProcessInjector belong?

```
Core        — Domain contracts, no infrastructure
Application — Use cases, orchestration
Service     — Infrastructure adapters            ← ProcessInjector lives here
```

**IProcessInjector belongs in MultiSeat.Service** (same namespace as the implementation). It's a service-layer abstraction, not a Core contract. Process creation is inherently infrastructure — it's not a domain concept.

**Rationale**: The interface is consumed by other Service-layer classes (SeatManager, ApolloManager, OnConnectAppLauncher, AudioRouter, VirtualDisplayManager). It doesn't need to cross into Core because process creation is Windows infrastructure. The `int sessionId` parameter is a Windows primitive, not a domain value object.

### Does it introduce Windows-specific types into Core?

No — `int sessionId` is a primitive. But the semantic meaning is Windows-specific. A Core-layer interface would need a `SessionId` value object to abstract this, which is premature given the current 2-layer architecture.

---

## Testability

### Current testability

- ProcessInjector cannot be mocked — no interface
- SeatManager tests can't test app launch without real Windows sessions
- ApolloManager tests can't test Apollo launch without real sessions
- OnConnectAppLauncher tests can't test app launch without real sessions
- AudioRouter tests can't test VoiceMeeter launch without real sessions
- VirtualDisplayManager tests can't test display enumeration without real sessions
- Existing tests call `internal static` methods (EnsureNotConsoleSession) and verify P/Invoke constants

### After interface extraction

- All 5 consumers can be tested with a mock/stub `IProcessInjector`
- SeatManager tests: mock `LaunchInSessionAsync` → return test PID
- ApolloManager tests: mock `LaunchApolloInSessionAsync` → return test PID
- But: the mocked behavior is trivial (return a PID). The real value of process creation is the Windows integration, which can't be unit-tested anyway.

### Minimum test seam

The interface itself is the test seam. But the test value is limited — mocking `LaunchInSessionAsync` to return `42` doesn't test anything meaningful. The real tests are integration tests that require SYSTEM privileges and real Windows sessions.

---

## Risks

### Risk 1: Interface is a thin wrapper over infrastructure

The interface methods are all `Task<int>` (PID) with Windows session IDs as parameters. The interface doesn't abstract away Windows — it just hides the P/Invoke calls. Consumers still need to understand Windows sessions to use the interface meaningfully.

### Risk 2: SessionLauncher dependency remains concrete

ProcessInjector depends on `SessionLauncher` (concrete) for `GetSessionToken()`. Even with `IProcessInjector`, this dependency stays concrete because `GetSessionToken` returns `SafeTokenHandle` (Windows-specific) and is deliberately excluded from `ISessionLauncher`. Extracting `IProcessInjector` doesn't break this coupling — it just moves it.

### Risk 3: Low testability gain

The main testability win from interface extraction is mocking. But mocking process creation to return a PID doesn't test anything meaningful. The real value is in integration tests, which require SYSTEM privileges regardless of the interface.

### Risk 4: 5 consumers but no domain value

All 5 consumers use ProcessInjector for the same purpose: launch a process in a session. The interface doesn't enable new architectural patterns — it just makes the dependency explicit. Service Locator removal achieves the same goal with less work.

---

## Comparison With Alternatives

### A. IProcessInjector

| Criterion | Rating |
|-----------|--------|
| Architectural value | Medium — makes dependency explicit |
| Testability | Low — mocking process creation is trivial |
| Windows coupling | High — interface parameters are Windows session IDs |
| Consumer count | 5 |
| Implementation risk | Low — pure extraction |
| Scope | 7 files (1 new, 6 modified) |
| Core/Application boundary | No impact — stays in Service layer |

### B. Narrower interface (e.g., ISeatProcessLauncher)

A smaller interface with only the methods SeatManager and OnConnectAppLauncher need:
- `LaunchInSessionAsync` only

| Criterion | Rating |
|-----------|--------|
| Architectural value | Low — only covers 2 of 5 consumers |
| Testability | Low — same mocking limitation |
| Windows coupling | High — same session ID parameters |
| Consumer count | 2 |
| Implementation risk | Low |
| Scope | 4 files |

**Verdict**: Worse than full IProcessInjector — fewer consumers, same limitations.

### C. Service Locator removal (InputRouter, InputHookManager, ApolloManager)

Remove public properties from SeatManager, inject dependencies directly into API endpoints.

| Criterion | Rating |
|-----------|--------|
| Architectural value | Medium — makes dependencies explicit |
| Testability | Low — same as before, just explicit |
| Windows coupling | Low — no new types involved |
| Consumer count | 3 properties → direct injection |
| Implementation risk | Very low — pure wiring change |
| Scope | 3-4 files |

**Verdict**: Better value per unit of work than IProcessInjector. Same architectural value, lower risk, no new interface needed.

### D. Skip to IStreamingProvider (provider abstraction)

Define `IStreamingProvider` to abstract ApolloManager, ApolloConfigBuilder, and ApolloServerQuery.

| Criterion | Rating |
|-----------|--------|
| Architectural value | High — enables provider migration |
| Testability | High — mock entire streaming backend |
| Windows coupling | Low — provider-specific, not Windows-specific |
| Consumer count | SeatManager (1) |
| Implementation risk | Medium — larger scope |
| Scope | 5-8 files |

**Verdict**: Highest value but larger scope. Better as a future step after smaller extractions.

---

## Hidden Problems Found

### 1. Service Locator pattern (SeatManager)

SeatManager exposes `InputRouter`, `InputHookManager`, and `ApolloManager` as public properties for API access. This is a Service Locator anti-pattern that should be addressed before more interface extractions.

### 2. ProcessInjector depends on concrete SessionLauncher

ProcessInjector calls `_sessionLauncher.GetSessionToken()` which returns `SafeTokenHandle`. This method is NOT on `ISessionLauncher` — it was deliberately excluded. The coupling is:
```
ProcessInjector → SessionLauncher (concrete) → SafeTokenHandle
```

Extracting `IProcessInjector` doesn't break this — it just adds an interface on top.

### 3. RunInConsoleSessionAsync blocks synchronously

VirtualDisplayManager calls `_processInjector.RunInConsoleSessionAsync(...).GetAwaiter().GetResult()` — a sync-over-async anti-pattern. This is an existing issue, not introduced by this audit.

---

## Decision

```
DEFER
```

**Rationale**: IProcessInjector is a valid extraction but not the highest-value next step.

**Why defer**:
1. Interface is a thin wrapper over inherently Windows-specific operations
2. Testability gain is low — mocking process creation is trivial
3. SessionLauncher dependency stays concrete regardless
4. Service Locator removal provides better value per unit of work
5. 5 consumers is good, but the interface doesn't enable new patterns

**If implemented later**: The extraction is straightforward — 7 files, low risk, no behavior change. But it should come after Service Locator removal and possibly after IStreamingProvider.

---

## Recommended Next Step

**Service Locator removal** — make SeatManager's `InputRouter`, `InputHookManager`, and `ApolloManager` properties explicit by injecting them directly into API endpoints.

**Why this is better than IProcessInjector**:
- Same architectural value (makes dependencies explicit)
- Lower risk (pure wiring change, no new interface)
- No new types involved
- Prepares for future IStreamingProvider by cleaning up SeatManager's surface
- Can be done in 3-4 files with minimal scope

---

## Final Report

```
Decision:                    DEFER
Candidate interface:         IProcessInjector
Production consumers:        5 (SeatManager, ApolloManager, OnConnectAppLauncher, AudioRouter, VirtualDisplayManager)
Methods actually required:   5 public + 1 internal static

Windows-specific types:      None in interface surface (int sessionId is primitive)
SafeTokenHandle:             Internal to implementation, not in interface
Clean boundary possible:     YES (but thin wrapper over infrastructure)

Testability benefit:         LOW — mocking process creation is trivial
Architectural value:         MEDIUM — makes dependency explicit
Risk:                        LOW — pure extraction
Scope:                       7 files (1 new, 6 modified)

Best alternative:            Service Locator removal (InputRouter, InputHookManager, ApolloManager)

Audit:                       docs/audits/MSE-ProcessInjector-Boundary-Audit.md
Source modified:             NO
Commit:                      NONE
Push:                        NONE

Recommended next step:       Service Locator removal from SeatManager
```
