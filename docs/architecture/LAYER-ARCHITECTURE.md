# Layer Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Current Project Structure

```
src/
├── MultiSeat.slnx
├── MultiSeat.Shared/          # Constants, models, shared code
├── MultiSeat.Service/         # Main service (Windows Service)
│   ├── Accounts/              # Windows account management
│   ├── Api/                   # ASP.NET Core REST API
│   ├── Configuration/         # Options, settings
│   ├── Diagnostics/           # Inspectors, diagnostics
│   ├── Display/               # SudoVDA virtual display
│   ├── Emulators/             # RetroArch config seeding
│   ├── Input/                 # HidHide, ViGEm, InputHook
│   ├── Interop/               # P/Invoke, Windows APIs
│   ├── Monitoring/            # Health checks, server query
│   ├── Sessions/              # SessionLauncher, SeatManager
│   ├── Storage/               # File storage
│   └── Streaming/             # VibepolloManager
├── MultiSeat.InputHook/       # Input hook DLL
├── MultiSeat.Dashboard/       # React web UI
└── MultiSeat.Tests/           # Unit + integration tests
```

---

## Layer Architecture (Proposed)

### Layer 1: MultiSeat.Shared (Domain)

**Responsibility**: Domain models, constants, shared abstractions

**May contain**:
- Domain entities (Seat, User, Session, etc.)
- Value objects (PortBlock, RdpGeometry, SeatStatus)
- Domain events
- Domain exceptions
- Provider interface (IStreamingProvider)
- Display interface (IDisplayBackend)
- Input interface (IInputBackend)
- Configuration models

**Must NOT contain**:
- Windows APIs
- P/Invoke
- DPAPI
- Registry
- File I/O
- Network I/O

**Dependencies**: None (leaf package)

---

### Layer 2: MultiSeat.Application (Application)

**Responsibility**: Use cases, orchestration, application logic

**May contain**:
- Use case handlers
- SeatManager (orchestration)
- Workflow definitions
- Application services
- DTOs, mappers
- Validation logic

**Must NOT contain**:
- Windows APIs
- P/Invoke
- Driver interactions
- Provider implementations

**Dependencies**: MultiSeat.Shared

---

### Layer 3: MultiSeat.Infrastructure (Infrastructure)

**Responsibility**: Windows implementation, driver interactions, external integrations

**May contain**:
- Windows account management (AccountManager)
- Session management (SessionLauncher)
- Display management (VirtualDisplayManager)
- Provider management (VibepolloManager, provider adapters)
- Process management (ProcessInjector, ProcessTracker)
- Security (DPAPI, ACL, API key)
- Configuration persistence
- Logging, diagnostics

**Must NOT contain**:
- Domain logic
- Business rules
- Use case orchestration

**Dependencies**: MultiSeat.Shared, MultiSeat.Application

---

### Layer 4: MultiSeat.Provider.SDK

**Responsibility**: Provider contract, adapter interfaces

**May contain**:
- IStreamingProvider interface
- Provider configuration models
- Provider health models
- Provider lifecycle contracts

**Must NOT contain**:
- Provider implementations
- Windows APIs
- Domain logic

**Dependencies**: MultiSeat.Shared

---

### Layer 5: MultiSeat.Provider.Host

**Responsibility**: Provider process hosting, lifecycle management

**May contain**:
- Provider process management
- Provider configuration generation
- Provider health monitoring
- Provider restart logic

**Must NOT contain**:
- Domain logic
- Provider-specific implementations

**Dependencies**: MultiSeat.Provider.SDK, MultiSeat.Infrastructure

---

### Layer 6: Plugins (Future)

**Responsibility**: Optional extensions, custom providers

**May contain**:
- Custom provider adapters
- Custom display backends
- Custom input backends
- Custom health checks

**Dependencies**: MultiSeat.Provider.SDK

---

### Layer 7: MultiSeat.Dashboard (UI)

**Responsibility**: Web UI, API consumption

**May contain**:
- React components
- API client
- State management
- UI logic

**Must NOT contain**:
- Business logic
- Domain logic
- Windows APIs

**Dependencies**: MultiSeat.Service (via HTTP)

---

## Dependency Rules

### Allowed Dependencies

```
Dashboard → Service (HTTP)
Service → Application → Shared
Service → Infrastructure → Shared
Service → Provider.Host → Provider.SDK → Shared
Plugins → Provider.SDK → Shared
```

### Forbidden Dependencies

```
Shared → ANYTHING (leaf)
Application → Infrastructure
Application → Windows APIs
Provider.SDK → Infrastructure
Dashboard → Domain logic
```

---

## Current vs Target

| Current | Target | Migration |
|---------|--------|-----------|
| MultiSeat.Shared | MultiSeat.Shared (Domain) | Rename, extract interfaces |
| MultiSeat.Service | MultiSeat.Application + Infrastructure | Split |
| MultiSeat.InputHook | MultiSeat.Infrastructure (Input) | Integrate |
| MultiSeat.Dashboard | MultiSeat.Dashboard | Keep |
| MultiSeat.Tests | MultiSeat.Tests | Keep |
| (none) | MultiSeat.Provider.SDK | Create |
| (none) | MultiSeat.Provider.Host | Create |

---

## Migration Strategy

### Phase 1: Extract Interfaces

1. Create IStreamingProvider in Shared
2. Create IDisplayBackend in Shared
3. Create IInputBackend in Shared
4. Move VibepolloManager behind IStreamingProvider

### Phase 2: Split Service

1. Extract Application layer (orchestration)
2. Extract Infrastructure layer (Windows implementation)
3. Move P/Invoke to Interop module

### Phase 3: Provider SDK

1. Create Provider.SDK project
2. Move IStreamingProvider to SDK
3. Create adapter pattern

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current structure is monolithic | Project files | FACT |
| Clean Architecture requires layering | Architecture principles | DECISION |
| Provider SDK enables flexibility | Helios pattern | INFERENCE |
| Migration is incremental | Existing test coverage | INFERENCE |
