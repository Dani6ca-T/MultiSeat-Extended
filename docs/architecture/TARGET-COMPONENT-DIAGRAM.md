# Target Component Diagram

**Date**: 2026-08-30
**Status**: FROZEN

---

## System Architecture

```
                         Dashboard (React)
                             │
                             │ HTTP/WebSocket
                             ▼
                     Management API (ASP.NET Core)
                             │
                             ▼
                     Application Layer
                             │
                             ▼
                        Seat Manager
                             │
          ┌──────────────────┼──────────────────┐
          │                  │                  │
          ▼                  ▼                  ▼
      User Manager      Session Manager     Provider Manager
          │                  │                  │
          │                  │          ┌───────┴────────┐
          │                  │          ▼                ▼
          │                  │      Vibepollo          Apollo
          │                  │      (GPLv3)           (GPLv3)
          │                  │
          │          ┌───────┼────────┐
          │          ▼       ▼        ▼
          │       Display   Audio    Input
          │          │       │        │
          │       SudoVDA   RDP    HidHide
          │       (IddCx)  Remote   (MIT)
          │                 Audio
          │
          ▼
       Process
       Tracker
       (Job Objects)
          │
       Health
       Monitor
       (5s + backoff)
          │
       Windows
       (APIs)
```

---

## Component Responsibilities

### Management API

- REST endpoints
- WebSocket real-time updates
- Authentication (API key)

### Application Layer

- Use case orchestration
- Workflow coordination
- DTO mapping

### Seat Manager

- Provision seat (9-step pipeline)
- Teardown seat (reverse order)
- Start/stop streaming
- Recovery coordination

### User Manager

- Create/delete Windows accounts
- Group membership
- Credential storage (DPAPI)

### Session Manager

- Create RDP loopback sessions
- Token manipulation (CreateProcessAsUser)
- Session lifecycle

### Provider Manager

- Start/stop provider processes
- Configuration generation
- Health monitoring
- Provider abstraction (IStreamingProvider)

### Display Manager

- Create/destroy virtual displays
- Display isolation (primary + shrunk)
- Refresh rate clamping
- Late display detection

### Audio Manager

- Per-session audio (RDP Remote Audio)
- RustDesk suppression
- Audio routing

### Input Manager

- HidHide session jail
- Controller routing (optional)
- Device assignment

### Process Tracker

- PID → Seat mapping
- Job Object management
- Process cleanup
- Residual process adoption

### Health Monitor

- 5s health checks
- Progressive crash backoff
- Display re-detection
- Full seat re-provision

---

## Data Flow

```
User Request
    │
    ▼
API
    │
    ▼
Application
    │
    ▼
Seat Manager
    │
    ├──→ User Manager ──→ Windows
    ├──→ Session Manager ──→ RDP
    ├──→ Display Manager ──→ SudoVDA
    ├──→ Provider Manager ──→ Vibepollo
    ├──→ Input Manager ──→ HidHide
    └──→ Process Tracker ──→ Job Objects
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatManager orchestrates all | SeatManager.cs | FACT |
| Vibepollo is external process | VibepolloManager | FACT |
| SudoVDA handles display | VirtualDisplayManager | FACT |
| HidHide handles input | HidHideConfigurator | FACT |
| Job Objects handle cleanup | Windows API | FACT |
