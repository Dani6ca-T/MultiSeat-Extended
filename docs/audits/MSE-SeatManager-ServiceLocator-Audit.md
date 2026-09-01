# SeatManager Service Locator Audit

## Scope

Investigate whether SeatManager exposes dependencies as public properties (Service Locator pattern), and whether this creates architectural problems.

## All Public Properties

| Property | Type | Classification | External consumers |
|----------|------|----------------|-------------------|
| `ActiveSeatCount` | `int` | State (computed) | API endpoints (indirectly) |
| `GetAllSeats()` | `IReadOnlyCollection<SeatInfo>` | State query | API endpoints |
| `GetSeat(Guid)` | `SeatInfo?` | State query | API endpoints |
| `ControllerRoutingEnabled` | `bool` | State (option value) | `InputEndpoints` |
| `InputRouter` | `InputRouter` (concrete) | **Dependency exposure** | `InputEndpoints` |
| `InputHookManager` | `InputHookManager` (concrete) | **Dependency exposure** | `InputEndpoints` |
| `ApolloManager` | `ApolloManager` (concrete) | **Dependency exposure** | **NONE** |

## Actual External Consumers

### Consumer 1: InputEndpoints

```
InputEndpoints
    ↓ reads
SeatManager.ControllerRoutingEnabled
    ↓
bool (option value — legitimate)

InputEndpoints
    ↓ reads
SeatManager.InputRouter
    ↓ calls
InputRouter.GetConnectedControllers()
InputRouter.GetAssignments()
InputRouter.AssignController(int, Guid)
InputRouter.UnassignController(int)
InputRouter.AutoAssign(List<SeatInfo>)

InputEndpoints
    ↓ reads
SeatManager.InputHookManager
    ↓ reads
InputHookManager.IsInstalled
InputHookManager.CurrentSessionId
```

### Consumer 2: ApolloManager property

```
SeatManager.ApolloManager
    ↓
NO external consumers
```

Zero code reads this property. It exists but is unused from outside SeatManager.

## Service Locator Classification

| Property | Classification | Explanation |
|----------|---------------|-------------|
| `ControllerRoutingEnabled` | **State Exposure** (legitimate) | Read-only bool derived from `_options.EnableViGEmController`. Not a dependency. |
| `InputRouter` | **Service Locator** | API layer reaches through SeatManager to get InputRouter. InputEndpoints then calls InputRouter methods directly. |
| `InputHookManager` | **Service Locator** | API layer reaches through SeatManager to get InputHookManager for status queries. |
| `ApolloManager` | **Unnecessary Public Visibility** | No external consumer. Could be private with zero impact. |

## Existing Interface Coverage

| Dependency | Interface exists? | Used via interface? |
|-----------|------------------|-------------------|
| `AccountManager` | ✅ `IAccountManager` | ✅ Yes |
| `SessionLauncher` | ✅ `ISessionLauncher` | ✅ Yes |
| `VirtualDisplayManager` | ✅ `IVirtualDisplayManager` | ✅ Yes |
| `ProcessInjector` | ❌ | N/A (deferred) |
| `InputRouter` | ❌ | Exposed as concrete property |
| `InputHookManager` | ❌ | Exposed as concrete property |
| `ApolloManager` | ❌ | Exposed as concrete property (unused) |
| `ControllerManager` | ❌ | Private (correct) |
| `AudioRouter` | ❌ | Private (correct) |
| `FirewallManager` | ❌ | Private (correct) |
| `PortAllocator` | ❌ | Private (correct) |
| `HidHideConfigurator` | ❌ | Private (correct) |
| `OnConnectAppLauncher` | ❌ | Private (correct) |
| `ApolloConfigBuilder` | ❌ | Private (correct) |
| `ApolloServerQuery` | ❌ | Private (correct) |

## Candidate Changes

### Candidate A: Make ApolloManager private

**What changes:** Remove the `public ApolloManager ApolloManager => _apolloManager;` property. Make the field `_apolloManager` remain as-is (it's already a private field).

**Why it matters:** Zero external consumers. Pure unnecessary visibility.

**Risk:** Near zero — no code reads it externally.

**Scope:** 1 line removed from SeatManager.cs.

**Enables later:** Nothing directly — cleanup.

### Candidate B: Inject InputRouter + InputHookManager directly into InputEndpoints

**What changes:** Instead of `InputEndpoints` receiving `SeatManager` and reaching through to `InputRouter`/`InputHookManager`, inject those services directly into `InputEndpoints.Map()`.

**Why it matters:** Eliminates the Service Locator pattern for these two dependencies. InputEndpoints becomes a clean, independently injectable endpoint group.

**Risk:** Low — same services, different injection path.

**Scope:** InputEndpoints.cs + ApiServer.cs (DI registration). SeatManager loses two public properties.

**Enables later:** Cleaner dependency graph; SeatManager no longer serves as a dependency gateway for input.

### Candidate C: Create IInputRouter / IInputHookManager interfaces

**What changes:** Extract interfaces for InputRouter and InputHookManager.

**Why it matters:** Enables mocking for testing. But InputRouter and InputHookManager are thin wrappers over ViGEmBus and Win32 hooks respectively — mocking them tests nothing meaningful.

**Risk:** Low but value is low too (same reasoning as IProcessInjector audit).

**Scope:** 2 new interfaces, 2 class updates, multiple consumers.

**Enables later:** Marginal testability. No provider boundary impact.

### Candidate D: Leave everything as-is

**What changes:** Nothing.

**Risk:** None now. The Service Locator pattern is a code smell, not a bug.

## Dependency Graph

```
Current state:

InputEndpoints
    ↓ receives
SeatManager (concrete, via DI)
    ↓ exposes
InputRouter (concrete property)
InputHookManager (concrete property)
ControllerRoutingEnabled (bool property)

Preferred state:

InputEndpoints
    ↓ receives directly via DI
InputRouter
InputHookManager
SeatManager (only for seat state queries)
```

## Risk Assessment

The Service Locator exposure creates these concrete problems:

1. **InputEndpoints couples to SeatManager** — it receives the entire SeatManager when it only needs InputRouter, InputHookManager, and a seat lookup function.
2. **ApolloManager public property is dead code** — no consumer uses it, but it signals that SeatManager is a dependency gateway.
3. **Testability** — testing InputEndpoints requires constructing a full SeatManager with all 17 dependencies, when it really only needs 3 services.

However, none of these are blocking anything today. The exposure is a code smell, not an architectural blocker.

## Decision

**APPROVE FOR IMPLEMENTATION** — Candidates A + B combined (smallest change, highest value)

The combined change is:
1. Remove unused `ApolloManager` public property (1 line)
2. Inject `InputRouter` + `InputHookManager` directly into `InputEndpoints` (remove Service Locator)
3. Remove `InputRouter` and `InputHookManager` public properties from SeatManager

This eliminates the Service Locator pattern without creating any new interfaces. It is the same approach as the `IAccountManager` extraction but even simpler — no new interface needed, just correct DI.

## Implementation Plan

1. Remove `public ApolloManager ApolloManager => _apolloManager;` from SeatManager.cs
2. Remove `public InputRouter InputRouter => _inputRouter;` from SeatManager.cs
3. Remove `public InputHookManager InputHookManager => _inputHookManager;` from SeatManager.cs
4. Update `InputEndpoints.Map()` to receive `InputRouter`, `InputHookManager`, and `SeatManager` separately
5. Update `ApiServer.cs` to register `InputRouter` + `InputHookManager` in the inner DI container
6. Run build and tests
7. Update documentation

## Implementation Result

**Status:** ✅ Implemented

**Files changed:**
- `src/MultiSeat.Service/Sessions/SeatManager.cs` — removed 3 public dependency properties
- `src/MultiSeat.Service/Api/InputEndpoints.cs` — injected InputRouter + InputHookManager directly
- `src/MultiSeat.Service/Api/ApiServer.cs` — registered InputRouter + InputHookManager in inner DI

**Build:** ✅ 0 errors from tracked code (9 pre-existing ProcessTracking errors)
**Tests:** ✅ 387 passed / 17 skipped / 0 failed

**Verification:**
- Searched for `.InputRouter`, `.InputHookManager`, `.ApolloManager` — no external consumers remain
- All remaining matches are XML comments, DI registrations, and internal constants
- csproj files restored to original state after temporary ProcessTracking exclusion
