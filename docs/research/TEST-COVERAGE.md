# MultiSeat-Extended: Покрытие тестами

## Обзор

Проект использует xUnit + Moq для тестирования. 22 тестовых файла, покрывающих основные компоненты.

## Test Projects

```
MultiSeat.Tests
├── xUnit (test framework)
├── Moq (mocking)
├── Microsoft.NET.Test.Sdk
└── References: MultiSeat.Service, MultiSeat.Shared
```

## Test Files by Area

### Accounts (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `SeatGroupTests.cs` | Seat group membership, admin removal, localized group names | ✅ Good |

**What's tested:**
- ApplySeatGroupMembership adds Users + Remote Desktop Users
- Removes Administrators when GrantSeatAdministrator=false
- Localized group name resolution from WellKnown SID

**What's missing:**
- Account creation/deletion flows
- Credential encryption/decryption
- DPAPI scope migration
- Profile pre-creation

### Api (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `PublicEndpointTests.cs` | Auth state, public endpoints | ✅ Good |

**What's tested:**
- IsAlwaysPublic logic (GET /api/system/auth)
- POST on same path requires auth

**What's missing:**
- Endpoint authorization
- CORS behavior
- WebSocket authentication
- Rate limiting (not implemented)

### Diagnostics (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `LoggingFilterTests.cs` | Log filter rules, Event Log visibility | ✅ Good |

**What's tested:**
- Shipped appsettings.json lets Information through
- Keeps ASP.NET Core request logs out
- Keeps Debug out
- Provider-specific rule precedence

**What's missing:**
- HidHideInspector (manual/diagnostic tool)

### Emulators (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `RetroArchConfigSeederTests.cs` | RetroArch config seeding | ✅ Good |

**What's tested:**
- Config file generation
- Netplay port assignment
- Shared ROM directory

**What's missing:**
- Other emulator seeders (Dolphin, PCSX2 — not implemented)

### Input (4 files)

| File | Tests | Coverage |
|------|-------|----------|
| `HidHideArgumentTests.cs` | CLI argument formatting | ✅ Good |
| `HidHideParserTests.cs` | CLI output parsing | ✅ Good |
| `HidHideSessionJailTests.cs` | Session jail rule generation | ✅ Good |
| `InputTests.cs` | InputRouter, ControllerManager | ⚠️ Partial |

**What's tested:**
- HidHide CLI argument construction
- HidHide CLI output parsing
- Session jail rule generation
- XInput state forwarding (basic)

**What's missing:**
- InputRouter polling loop behavior
- Vibration feedback routing
- Controller auto-assignment logic
- HidHideConfigurator integration
- InputHookManager (currently no-op)

### Integration (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `EndToEndTests.cs` | MultiSeatOptions defaults | ✅ Good |

**What's tested:**
- Default option values
- Vibepollo paths
- Port configuration

**What's missing:**
- Full provisioning pipeline
- Session creation flow
- Health check cycle

### Sessions (8 files)

| File | Tests | Coverage |
|------|-------|----------|
| `DialogClickHelperTests.cs` | Button click automation | ✅ Good |
| `PortAllocatorTests.cs` | Port block allocation | ✅ Good |
| `ProcessInjectorTests.cs` | Process launching | ⚠️ Partial |
| `RdpCredentialStoreTests.cs` | Credential store | ✅ Good |
| `RdpFileBuilderTests.cs` | RDP file generation | ✅ Good |
| `RdpWrapperTests.cs` | RDP Wrap detection | ✅ Good |
| `SeatManagerTests.cs` | Seat lifecycle | ⚠️ Partial |
| `SeatStateTests.cs` | State transitions | ✅ Good |
| `SessionGuardTests.cs` | Console session guard | ✅ Good |
| `SessionLauncherTests.cs` | Session creation | ⚠️ Partial |

**What's tested:**
- Port allocation/release
- RDP file generation with geometry
- Credential store read/write
- RDP Wrap detection
- State transition validation
- Console session guard
- ProcessInjector session verification

**What's missing:**
- Full session creation via RDP loopback
- Session reconnect after sleep
- ProcessInjector CreateProcessAsUser (requires SYSTEM)
- SeatManager provisioning pipeline (requires Windows environment)
- SessionLauncher WTS polling

### Storage (1 file)

| File | Tests | Coverage |
|------|-------|----------|
| `SecureFileTests.cs` | ACL hardening | ✅ Good |

**What's tested:**
- Permission restriction
- Legacy scope migration
- Error handling

### Streaming (2 files)

| File | Tests | Coverage |
|------|-------|----------|
| `StreamingTests.cs` | VibepolloConfigBuilder, VibepolloManager | ✅ Good |
| `VibepolloLogParserTests.cs` | Log parsing | ✅ Good |

**What's tested:**
- Config generation with required fields
- PerSession audio config (no sink named)
- Display output update
- Virtual display app stripping
- Log parsing for requested mode
- MaxRestartAttempts constant

**What's missing:**
- VibepolloManager process lifecycle (requires Vibepollo)
- OnConnectAppLauncher behavior
- ClientResolutionFollower behavior

## Coverage Summary

| Area | Files | Coverage | Quality |
|------|-------|----------|---------|
| Accounts | 1 | ⚠️ Partial | Good for what's there |
| Api | 1 | ⚠️ Partial | Auth logic covered |
| Configuration | 0 | ❌ None | No tests for MultiSeatOptions binding |
| Diagnostics | 1 | ✅ Good | Log filters well tested |
| Display | 0 | ❌ None | VirtualDisplayManager not tested |
| Emulators | 1 | ✅ Good | RetroArch seeder covered |
| Input | 4 | ✅ Good | HidHide well tested |
| Integration | 1 | ⚠️ Partial | Defaults only |
| Interop | 0 | ❌ None | P/Invoke not testable |
| Monitoring | 0 | ❌ None | SessionHealthCheck not tested |
| Sessions | 8 | ⚠️ Partial | Helpers good, integration missing |
| Storage | 1 | ✅ Good | ACL hardening covered |
| Streaming | 2 | ✅ Good | Config builder well tested |

## Critical Untested Areas

### 1. Session Creation Pipeline
- SessionLauncher.CreateSessionViaRdpLoopbackAsync
- RDP loopback flow
- WTS polling
- Keepalive process management

### 2. ProcessInjector
- CreateProcessAsUserW execution
- Token acquisition and verification
- Environment block creation
- Session verification

### 3. Health Check Recovery
- Session disconnect/reconnect
- Vibepollo crash detection
- Auto-restart logic
- Late display detection

### 4. Display Management
- VirtualDisplayManager lifecycle
- Display isolation application
- Resolution negotiation
- SudoVDA detection

### 5. Full Seat Lifecycle
- ProvisionSeatAsync end-to-end
- TeardownSeatAsync cleanup
- State transitions under load
- Error recovery paths

## Why Integration Tests Are Hard

Most critical paths require:
- SYSTEM privileges (CreateProcessAsUser)
- Windows Session 0 context
- RDP Wrapper installed
- SudoVDA driver installed
- Vibepollo installed
- Active console session with user logged in

These cannot be unit tested — they require a full Windows environment with all prerequisites installed.

## Recommendations

1. **Add Unit Tests for Configuration**
   - MultiSeatOptions binding
   - SeatPresetStore persistence
   - VibepolloConfigBuilder edge cases

2. **Add Integration Tests for Display**
   - VirtualDisplayManager with mocked ProcessInjector
   - ResolutionNegotiator validation
   - Display enumeration parsing

3. **Add Integration Tests for Health Check**
   - SessionHealthCheck with mocked dependencies
   - Auto-restart logic
   - State transition scenarios

4. **Consider Test Helpers**
   - Mock ProcessInjector for session tests
   - Mock WtsApi for session state tests
   - Mock VibepolloManager for lifecycle tests
