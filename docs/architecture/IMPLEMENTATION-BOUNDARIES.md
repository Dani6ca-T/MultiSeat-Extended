# Implementation Boundaries

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define what each layer may and must not contain.

---

## Layer: MultiSeat.Shared (Domain)

### May Contain

- Domain entities (Seat, User, Session, etc.)
- Value objects (PortBlock, RdpGeometry, SeatStatus)
- Domain events
- Domain exceptions
- Provider interface (IStreamingProvider)
- Display interface (IDisplayBackend)
- Input interface (IInputBackend)
- Configuration models

### Must NOT Contain

- Windows APIs
- P/Invoke
- DPAPI
- Registry
- File I/O
- Network I/O
- Driver interactions

---

## Layer: MultiSeat.Application

### May Contain

- Use case handlers
- SeatManager (orchestration)
- Workflow definitions
- Application services
- DTOs, mappers
- Validation logic

### Must NOT Contain

- Windows APIs
- P/Invoke
- Driver interactions
- Provider implementations

---

## Layer: MultiSeat.Infrastructure

### May Contain

- Windows account management (AccountManager)
- Session management (SessionLauncher)
- Display management (VirtualDisplayManager)
- Provider management (VibepolloManager, adapters)
- Process management (ProcessInjector, ProcessTracker)
- Security (DPAPI, ACL, API key)
- Configuration persistence
- Logging, diagnostics

### Must NOT Contain

- Domain logic
- Business rules
- Use case orchestration

---

## Layer: MultiSeat.Provider.SDK

### May Contain

- IStreamingProvider interface
- Provider configuration models
- Provider health models
- Provider lifecycle contracts

### Must NOT Contain

- Provider implementations
- Windows APIs
- Domain logic

---

## Layer: MultiSeat.Provider.Host

### May Contain

- Provider process management
- Provider configuration generation
- Provider health monitoring
- Provider restart logic

### Must NOT Contain

- Domain logic
- Provider-specific implementations

---

## Layer: Plugins

### May Contain

- Custom provider adapters
- Custom display backends
- Custom input backends
- Custom health checks

### Must NOT Contain

- Core domain logic

---

## Layer: MultiSeat.Dashboard

### May Contain

- React components
- API client
- State management
- UI logic

### Must NOT Contain

- Business logic
- Domain logic
- Windows APIs

---

## Current vs Target

| Current | Target | Boundary Change |
|---------|--------|-----------------|
| MultiSeat.Shared | Shared (Domain) | Add interfaces |
| MultiSeat.Service | Application + Infrastructure | Split |
| MultiSeat.InputHook | Infrastructure (Input) | Integrate |
| MultiSeat.Dashboard | Dashboard | Keep |
| (none) | Provider.SDK | Create |
| (none) | Provider.Host | Create |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current structure is monolithic | Project files | FACT |
| Clean Architecture requires layering | Principles | DECISION |
| Provider SDK enables flexibility | Helios pattern | INFERENCE |
