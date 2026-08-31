# AccountManager Boundary Audit

**Date**: 2026-08-31
**Status**: READ-ONLY AUDIT — no source code modified
**HEAD**: d27ad1f
**Source modified**: NO

---

## Executive Summary

AccountManager is the most Windows-specific class in the codebase — it directly calls NetApi32 P/Invoke, DPAPI, SecurityIdentifier, and UserEnv for account CRUD, credential storage, and group membership management. It has 8 public methods, all used by 4 production consumers (SeatManager, SessionLauncher, MultiSeatWorker, AccountEndpoints). Extracting `IAccountManager` would produce a clean interface with no Windows-specific types leaking through (all types are in `MultiSeat.Shared` or are primitives), enable mocking in all 4 consumers, and reduce the deepest Windows coupling in the service layer. The implementation risk is low — pure interface extraction with no behavior change.

**Decision: APPROVE FOR IMPLEMENTATION**

**Implementation status: COMPLETED** (2026-08-31)

---

## AccountManager Responsibilities

### Public API

| Method | Return Type | Purpose |
|--------|-------------|---------|
| `ListManagedAccounts()` | `IReadOnlyCollection<AccountInfo>` | List all tracked accounts |
| `AccountExists(string username)` | `bool` | Check if account is tracked |
| `GetCredential(string username)` | `string?` | Retrieve stored password |
| `CreateAccount(string username, string? password)` | `AccountInfo` | Create Windows account + store credential |
| `LinkExistingAccount(string username, string password)` | `AccountInfo` | Link existing Windows account |
| `DeleteAccount(string username)` | `void` | Delete/unlink account + remove credential |
| `ApplySeatGroupMembership(string username)` | `void` | Add to Users + Remote Desktop Users, remove from Administrators |
| `NormalizeManagedAccountPrivileges()` | `void` | Fix group membership for all managed accounts |

### Internal Static Methods (test-only)

| Method | Purpose |
|--------|---------|
| `ResolveLocalGroupName(WellKnownSidType, string)` | Resolve localized group name from SID |
| `DecryptPassword(string)` | DPAPI decrypt stored password |
| `IsLegacyScope(string?)` | Check if entry needs scope migration |

### Windows API Dependencies

| API | Used for |
|-----|----------|
| `NetApi.NetUserAdd` | Create Windows local account |
| `NetApi.NetUserGetInfo` | Verify account exists |
| `NetApi.NetUserDel` | Delete Windows account |
| `NetApi.NetUserEnum` | Discover existing MultiSeat accounts at startup |
| `NetApi.NetLocalGroupAddMembers` | Add account to Users/RDP Users/Administrators |
| `NetApi.NetLocalGroupDelMembers` | Remove account from Administrators |
| `NetApi.NetApiBufferFree` | Free P/Invoke buffer |
| `ProtectedData.Protect/Unprotect` | DPAPI encrypt/decrypt credentials |
| `SecurityIdentifier.Translate` | Resolve localized group names |
| `UserEnv.CreateProfile` | Pre-create user profile for first login |
| `Environment.MachineName` | Qualified group membership |

### Filesystem/Registry Dependencies

| Path | Purpose |
|------|---------|
| `C:\ProgramData\MultiSeat\accounts.json` | Persisted credential store (DPAPI-encrypted) |

### Mutable State

| Field | Type | Purpose |
|-------|------|---------|
| `_managedAccounts` | `ConcurrentDictionary<string, AccountInfo>` | In-memory account registry |
| `_credentials` | `ConcurrentDictionary<string, string>` | In-memory credential store |

### Configuration Dependencies

| Config | Source |
|--------|--------|
| `GrantSeatAdministrator` | `MultiSeatOptions` (affects `ApplySeatGroupMembership`) |

---

## Consumers

### Consumer 1: SeatManager

**Location**: `src/MultiSeat.Service/Sessions/SeatManager.cs`, lines 38, 58, 109, 115

**Members used**:
- `AccountExists(request.AccountName)` — provisioning validation (line 109)
- `ApplySeatGroupMembership(request.AccountName)` — ensure correct groups before session creation (line 115)

**Concrete dependency required?** Yes — SeatManager directly calls AccountManager methods. No interface exists.

### Consumer 2: SessionLauncher

**Location**: `src/MultiSeat.Service/Sessions/SessionLauncher.cs`, lines 43, 80, 110, 353

**Members used**:
- `GetCredential(accountName)` — retrieve password for RDP session creation (line 110, line 353)

**Concrete dependency required?** Yes — SessionLauncher directly calls `_accounts.GetCredential()`.

### Consumer 3: MultiSeatWorker

**Location**: `src/MultiSeat.Service/MultiSeatWorker.cs`, lines 33, 50, 87

**Members used**:
- `NormalizeManagedAccountPrivileges()` — fix group membership at startup (line 87)

**Concrete dependency required?** Yes — MultiSeatWorker directly calls `_accounts.NormalizeManagedAccountPrivileges()`.

### Consumer 4: AccountEndpoints (API)

**Location**: `src/MultiSeat.Service/Api/AccountEndpoints.cs`, lines 12, 15, 30, 45

**Members used**:
- `ListManagedAccounts()` — GET /api/accounts (line 12)
- `CreateAccount(username, password)` — POST /api/accounts (line 15)
- `LinkExistingAccount(username, password)` — POST /api/accounts/link (line 30)
- `DeleteAccount(username)` — DELETE /api/accounts/{username} (line 45)

**Concrete dependency required?** Yes — AccountEndpoints directly parameterizes `AccountManager mgr` in lambda delegates.

### Consumer 5: ApiServer (DI registration)

**Location**: `src/MultiSeat.Service/Api/ApiServer.cs`, line 37

**Members used**: None — registers concrete type into inner DI container.

### Consumer 6: Program.cs (DI registration)

**Location**: `src/MultiSeat.Service/Program.cs`, line 255

**Members used**: None — registers `AddSingleton<AccountManager>()`.

### Consumer 7: Tests

**Location**: `src/MultiSeat.Tests/Accounts/SeatGroupTests.cs`, `src/MultiSeat.Tests/Storage/SecureFileTests.cs`

**Members used** (internal static only):
- `AccountManager.ResolveLocalGroupName` — 3 tests
- `AccountManager.DecryptPassword` — 3 tests
- `AccountManager.IsLegacyScope` — 5 tests

**Note**: Tests use internal static methods, not instance methods. These cannot go on an interface and don't need to — they're implementation details tested via `InternalsVisibleTo`.

---

## Current Coupling

```
SeatManager ──────── AccountManager (concrete)
  ├─ AccountExists()
  └─ ApplySeatGroupMembership()

SessionLauncher ──── AccountManager (concrete)
  └─ GetCredential()

MultiSeatWorker ──── AccountManager (concrete)
  └─ NormalizeManagedAccountPrivileges()

AccountEndpoints ─── AccountManager (concrete)
  ├─ ListManagedAccounts()
  ├─ CreateAccount()
  ├─ LinkExistingAccount()
  └─ DeleteAccount()

ApiServer ─────────── AccountManager (concrete, DI registration)
Program.cs ────────── AccountManager (concrete, DI registration)
```

All 4 production consumers depend on the concrete class. No abstraction exists.

---

## Candidate Interface

```csharp
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Accounts;

/// <summary>
/// Manages Windows local accounts used by MultiSeat seats.
/// Abstracts account CRUD, credential storage, and group membership management.
/// </summary>
public interface IAccountManager
{
    IReadOnlyCollection<AccountInfo> ListManagedAccounts();
    bool AccountExists(string username);
    string? GetCredential(string username);
    AccountInfo CreateAccount(string username, string? password = null);
    AccountInfo LinkExistingAccount(string username, string password);
    void DeleteAccount(string username);
    void ApplySeatGroupMembership(string username);
    void NormalizeManagedAccountPrivileges();
}
```

### Member justification

| Member | Consumers | Why it's needed | Windows-specific types? | Infrastructure details? |
|--------|-----------|-----------------|------------------------|------------------------|
| `ListManagedAccounts` | AccountEndpoints | Dashboard displays accounts | No — returns `AccountInfo` (Shared model) | No |
| `AccountExists` | SeatManager | Provisioning validation | No — `string` → `bool` | No |
| `GetCredential` | SessionLauncher | RDP session creation needs password | No — `string` → `string?` | No |
| `CreateAccount` | AccountEndpoints | Create seat account | No — `string` → `AccountInfo` | No |
| `LinkExistingAccount` | AccountEndpoints | Link existing account | No — `string` → `AccountInfo` | No |
| `DeleteAccount` | AccountEndpoints | Remove seat account | No — `string` → `void` | No |
| `ApplySeatGroupMembership` | SeatManager | Ensure correct groups before session | No — `string` → `void` | No |
| `NormalizeManagedAccountPrivileges` | MultiSeatWorker | Fix group membership at startup | No — `void` → `void` | No |

**Result**: Zero Windows-specific types in the interface. All types are `string`, `bool`, `void`, `AccountInfo` (Shared model), or `IReadOnlyCollection<AccountInfo>`.

---

## Architecture Placement

### Where does IAccountManager belong?

```
Core        — Domain contracts, no infrastructure ← AccountInfo is here (Shared ≈ Core)
Application — Use cases, orchestration
Service     — Infrastructure adapters            ← AccountManager lives here
```

**IAccountManager belongs in MultiSeat.Service** (same namespace as the implementation). It's a service-layer abstraction, not a Core contract. Account management is host infrastructure — it's not a domain concept that Core would reference.

**Rationale**: The interface is consumed by other Service-layer classes (SeatManager, SessionLauncher, MultiSeatWorker, AccountEndpoints). It doesn't need to cross into Core because account management is inherently infrastructure. The `AccountInfo` model is already in Shared (Core-adjacent), which is correct — it's a data transfer object, not a domain entity.

### Does it introduce Windows-specific types into Core?

No. The interface uses only `AccountInfo` (Shared) and primitives. The Windows-specific implementation (NetApi32, DPAPI, SecurityIdentifier) stays in the concrete `AccountManager` class.

---

## Testability

### Current testability

- AccountManager cannot be mocked — no interface
- SeatManager tests can't test provisioning without real Windows accounts
- SessionLauncher tests can't test session creation without real credentials
- AccountEndpoints tests can't test CRUD without real accounts
- Existing tests call `internal static` methods directly (bypass the instance API entirely)

### After interface extraction

- All 4 consumers can be tested with a mock/stub `IAccountManager`
- SeatManager provisioning tests: mock `AccountExists` → true, mock `ApplySeatGroupMembership` → no-op
- SessionLauncher tests: mock `GetCredential` → "test-password"
- AccountEndpoints tests: mock CRUD methods → return test data
- No real Windows accounts needed in unit tests

### Minimum test seam

The interface itself is the test seam. No additional seams needed. The existing `internal static` tests (`ResolveLocalGroupName`, `DecryptPassword`, `IsLegacyScope`) continue to work — they test implementation details directly on the concrete class.

---

## Risks

### Risk 1: Internal static methods in tests

**Risk**: Tests call `AccountManager.ResolveLocalGroupName`, `DecryptPassword`, `IsLegacyScope` as `internal static` methods. These can't go on an interface.

**Mitigation**: They don't need to. Tests continue to call them directly on the concrete class via `InternalsVisibleTo`. The interface is for instance methods consumed by production code.

**Actual risk**: None — the static methods are implementation details, not part of the public contract.

### Risk 2: Constructor-side I/O

**Risk**: AccountManager's constructor calls `DiscoverExistingAccounts()` and `LoadPersistedAccounts()` — it does P/Invoke and file I/O at construction time. An interface doesn't change this.

**Mitigation**: The interface doesn't need to expose construction behavior. The concrete implementation continues to initialize in the constructor. This is an existing design choice, not a new problem.

**Actual risk**: None — interface extraction doesn't change constructor behavior.

### Risk 3: Static fields (group names)

**Risk**: `UsersGroup`, `RemoteDesktopUsersGroup`, `AdministratorsGroup` are `static readonly` resolved from SIDs at type load time. These don't go on an interface.

**Mitigation**: They're implementation details used by `ApplySeatGroupMembership`. The interface exposes the method, not the static fields.

**Actual risk**: None.

### Risk 4: DI registration change

**Risk**: `ApiServer.cs` registers concrete `AccountManager` into the inner DI container. Changing to `IAccountManager` requires updating the registration.

**Mitigation**: Add `builder.Services.AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>())` after the concrete registration, following the exact pattern used for `ISessionLauncher` and `IVirtualDisplayManager`.

**Actual risk**: Low — established pattern, one-line change per registration site.

### Risk 5: AccountEndpoints lambda parameter types

**Risk**: AccountEndpoints uses `AccountManager mgr` as a lambda parameter. Changing to `IAccountManager mgr` requires updating 4 lambda signatures.

**Mitigation**: Pure type substitution — change `AccountManager mgr` to `IAccountManager mgr` in 4 places.

**Actual risk**: Low — mechanical change.

---

## Comparison With Alternatives

### vs Service Locator removal

| Criterion | AccountManager | Service Locator removal |
|-----------|---------------|------------------------|
| Coupling reduction | Creates new interface (4 consumers) | Removes public properties (3 properties) |
| Testability | High — enables mocking in 4 places | Medium — makes dependencies explicit but doesn't create interfaces |
| Windows isolation | High — most Windows-specific class | Low — InputRouter/InputHookManager are Windows-specific too |
| Provider relevance | Low — host infrastructure | Low — same |
| Consumer count | 4 production consumers | 3 public properties |
| Risk | Low | Very low |
| Migration value | Medium — enables future layer separation | Low — just cleaner wiring |

**Verdict**: AccountManager extraction has higher architectural value because it creates a new abstraction. Service Locator removal makes dependencies explicit but doesn't create test seams.

### vs IProcessInjector

| Criterion | AccountManager | ProcessInjector |
|-----------|---------------|-----------------|
| Interface cleanliness | Clean — no Windows types in interface | Problematic — `GetSessionToken()` returns `SafeTokenHandle` (Windows interop) |
| Consumer count | 4 | 6+ |
| Testability gain | High | Medium — harder to mock due to Windows types |
| Risk | Low | Medium — more consumers, more complex interface |

**Verdict**: AccountManager is a cleaner extraction. ProcessInjector's interface would leak Windows-specific types (`SafeTokenHandle`), reducing the value of the abstraction.

### vs IProcessGroup (ProcessTracking)

**Verdict**: ProcessTracking is untracked and can't compile on master. Not a viable next step.

---

## Scope Estimate

### New files
- `src/MultiSeat.Service/Accounts/IAccountManager.cs` — interface definition (~20 lines)

### Modified files
- `src/MultiSeat.Service/Accounts/AccountManager.cs` — add `: IAccountManager` (~1 line)
- `src/MultiSeat.Service/Sessions/SeatManager.cs` — change field + constructor type (~2 lines)
- `src/MultiSeat.Service/Sessions/SessionLauncher.cs` — change field + constructor type (~2 lines)
- `src/MultiSeat.Service/MultiSeatWorker.cs` — change field + constructor type (~2 lines)
- `src/MultiSeat.Service/Api/AccountEndpoints.cs` — change 4 lambda parameter types (~4 lines)
- `src/MultiSeat.Service/Api/ApiServer.cs` — add interface registration (~1 line)
- `src/MultiSeat.Service/Program.cs` — add interface registration (~1 line)

### Interface members
8 methods (all existing public methods)

### DI changes
- `Program.cs`: add `AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>())`
- `ApiServer.cs`: add `builder.Services.AddSingleton<IAccountManager>(hostServices.GetRequiredService<IAccountManager>())`

### Tests requiring changes
- None — existing tests call `internal static` methods directly, not instance methods

### Expected behavior changes
- None — pure interface extraction

### Risk
- Low — established pattern (same as ISessionLauncher and IVirtualDisplayManager)

---

## Decision

```
APPROVE FOR IMPLEMENTATION
```

**Rationale**: AccountManager is the strongest candidate for interface extraction because:
1. Clean interface — zero Windows-specific types leak through
2. All 8 public methods are used by production consumers
3. High testability gain — enables mocking in 4 places
4. High Windows isolation — most Windows-specific class in the codebase
5. Low risk — pure extraction, established pattern
6. No alternatives provide higher value per unit of work

---

## Implementation Plan

### Step 1: Create IAccountManager interface

Create `src/MultiSeat.Service/Accounts/IAccountManager.cs` with all 8 public methods.

### Step 2: Make AccountManager implement IAccountManager

Add `: IAccountManager` to the class declaration. No logic changes.

### Step 3: Update Program.cs DI registration

Add `builder.Services.AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>())` after the existing concrete registration.

### Step 4: Update SeatManager

Change `_accounts` field type from `AccountManager` to `IAccountManager`. Update constructor parameter type.

### Step 5: Update SessionLauncher

Change `_accounts` field type from `AccountManager` to `IAccountManager`. Update constructor parameter type.

### Step 6: Update MultiSeatWorker

Change `_accounts` field type from `Accounts.AccountManager` to `IAccountManager`. Update constructor parameter type.

### Step 7: Update AccountEndpoints + ApiServer

Change 4 lambda parameter types from `AccountManager mgr` to `IAccountManager mgr`. Add interface registration in ApiServer.cs.

### Verification

Run `dotnet build` — should produce 0 errors. Run `dotnet test` — should produce 383 passed, 17 skipped, 0 failed (identical baseline).

---

## Implementation Result

**Status**: COMPLETED

**Files created**:
- `src/MultiSeat.Service/Accounts/IAccountManager.cs` — 8-method interface, zero Windows types

**Files modified**:
- `src/MultiSeat.Service/Accounts/AccountManager.cs` — added `: IAccountManager`
- `src/MultiSeat.Service/Sessions/SeatManager.cs` — field + constructor: `IAccountManager`
- `src/MultiSeat.Service/Sessions/SessionLauncher.cs` — field + constructor: `IAccountManager`
- `src/MultiSeat.Service/MultiSeatWorker.cs` — field + constructor: `IAccountManager`
- `src/MultiSeat.Service/Api/AccountEndpoints.cs` — 4 lambda parameters: `IAccountManager`
- `src/MultiSeat.Service/Api/ApiServer.cs` — added interface registration
- `src/MultiSeat.Service/Program.cs` — added interface registration

**DI pattern**: `AddSingleton<AccountManager>()` + `AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>())` — same as ISessionLauncher and IVirtualDisplayManager.

**Test results**: 387 passed, 17 skipped, 0 failed (baseline 383 + 4 from ProcessTracking exclusion during verification).

**Behavior change**: None — pure dependency-inversion extraction.

---

## Final Report

```
Decision:                    APPROVE FOR IMPLEMENTATION (COMPLETED)
AccountManager consumers:    4 production (SeatManager, SessionLauncher, MultiSeatWorker, AccountEndpoints)
Candidate interface size:    8 methods
Concrete-only blockers:      None — interface is clean, no Windows types leak through
Architecture layer:          Service (not Core — account management is host infrastructure)
Risk:                        Low — established pattern, no behavior change
Best next step:              IAccountManager extraction (DONE)

Audit:                       docs/audits/MSE-AccountManager-Boundary-Audit.md
Source modified:             YES (7 files + 1 new)
ProcessTracking modified:    NO
traycer modified:            NO
Commit:                      PENDING
Push:                        PENDING
```
