# Architecture Baseline

**Date**: 2026-08-30
**Status**: FROZEN — Single Source of Truth

---

## 1. System Purpose

**MultiSeat-Extended** is a **Windows-first multi-seat orchestration platform** for isolated interactive sessions with pluggable streaming providers.

### Technical Definition

MultiSeat-Extended enables multiple simultaneous Windows user sessions on a single host, each with:
- Independent virtual display
- Isolated audio endpoint
- Optional input device assignment
- Dedicated streaming server instance
- Managed game process lifecycle

---

## 2. Architectural Principles

1. **Clean Architecture** — Domain/Core depends on nothing
2. **Provider Independence** — Streaming is a provider
3. **Windows Isolation** — Windows APIs behind abstractions
4. **Credential Boundary** — Secrets never cross public models
5. **Seat is Orchestration** — Seat owns all resources
6. **Best-Effort Teardown** — Job Objects guarantee cleanup
7. **Health Checks are Fast** — 5-second interval
8. **State Persists** — Disk persistence target
9. **Configuration is Generated** — MultiSeat generates provider config
10. **Isolation is Default** — Display, audio, input isolated

---

## 3. Layers

```
MultiSeat.Shared (Domain)
    ↓
MultiSeat.Application (Use Cases)
    ↓
MultiSeat.Infrastructure (Windows)
    ↓
MultiSeat.Provider.SDK (Contract)
    ↓
MultiSeat.Provider.Host (Hosting)
```

---

## 4. Domain Model

### Entities

| Entity | Identity | Lifecycle |
|--------|----------|-----------|
| Host | Machine SID | Permanent |
| Seat | Guid | Provisioned → Active → Stopped |
| User | Account name | Created → Deleted |
| Session | SessionId | Created → Terminated |
| Display | UUID | Created → Destroyed |
| AudioEndpoint | Endpoint ID | Created → Destroyed |
| InputDevice | Device path | Connected → Disconnected |
| Game | Executable path | Defined → Launched |
| GameProcess | PID | Starting → Running → Exited |
| StreamingProvider | Name | Permanent |
| ProviderInstance | PID + SeatId | Created → Stopped |

### Value Objects

| Value Object | Composition |
|--------------|-------------|
| PortBlock | PortBase + 30 ports |
| RdpGeometry | Width × Height |
| SeatStatus | Status enum |

---

## 5. Seat Aggregate

```
Seat (Root)
├── User
├── Session
├── Display
├── AudioEndpoint
├── InputDevices (N)
├── GameProcesses (N)
└── ProviderInstance
```

### Invariants

1. One User per Seat
2. One Session per Seat
3. One Provider per Seat
4. Port blocks exclusive
5. Display UUIDs exclusive

---

## 6. State Machines

### Seat

```
Created → Provisioning → Configuring → Ready → Streaming
    ↓                                              ↓
  Failed ←────────────────────────────────── TearingDown → Idle
```

### ProviderInstance

```
Created → Starting → Running → Degraded → Stopping → Stopped
                              ↓
                            Failed
```

### Session

```
Created → Connecting → Active → Disconnected → Terminating → Terminated
```

### GameProcess

```
Starting → Running → Exited/Crashed/Unknown
```

---

## 7. Provider Architecture

### Contract

| Operation | Phase | Required |
|-----------|-------|----------|
| ValidateConfiguration | Provisioning | Yes |
| Prepare | Provisioning | Yes |
| Start | Provisioning | Yes |
| Stop | Teardown | Yes |
| Restart | Recovery | Yes |
| QueryHealth | Monitoring | Yes |
| GenerateConfig | Provisioning | Yes |
| CleanupConfig | Teardown | No |

### Multi-Instance

Provider does NOT support multi-instance. MultiSeat creates illusion via:
- Separate Windows sessions
- Unique port blocks
- Separate config directories
- Isolated displays

---

## 8. Process Architecture

### Ownership

```
MultiSeat.Service (SYSTEM)
    └── SeatManager
            ├── mstsc.exe (per seat)
            ├── sunshine.exe (per seat)
            └── game.exe (per seat)
```

### Job Objects

- One Job Object per Seat
- JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
- Contains provider + game + helper processes

### Recovery

- Progressive backoff (30/60/120s)
- MaxRestartAttempts = 3
- Residual process adoption (WMI)

---

## 9. Display

### Backend

- SudoVDA (IddCx kernel driver)
- One display per seat
- UUID-based assignment

### Isolation

- SudoVDA primary + RDP shrunk (640x480)
- Refresh rate clamping

---

## 10. Audio

### Model

- PerSession (RDP Remote Audio)
- True isolation (per-session endpoint)
- No VAC needed
- No microphone (RDP limitation)

---

## 11. Input

### Gamepad

- Vibepollo forwards natively
- HidHide session jail for isolation

### Keyboard/Mouse

- No isolation needed
- Physical → console, Moonlight → seat

---

## 12. Games

### Lifecycle

- Launched via ProcessInjector
- Tracked by ProcessTracker (target)
- Cleaned up via Job Object

### Limitations

- No RDP compatibility patching
- No Steam multi-instance
- No anti-cheat bypass

---

## 13. Steam

### Integration

- Shared game library (icacls)
- Per-seat Steam userdata
- No Steam multi-instance

---

## 14. Security

### Privileges

- Service: SYSTEM (SeTcbPrivilege)
- Seats: Standard user (no admin)
- Providers: SYSTEM in seat session

### Credentials

- DPAPI for encryption
- Never in SeatSpec, API wire, or logs

---

## 15. IPC

### Current

- Direct method calls (single process)

### Future

- Named Pipes (if service/app split)
- HTTP (API ↔ Dashboard)

---

## 16. Configuration

### Layers

| Layer | File | Owner |
|-------|------|-------|
| Host | appsettings.json | MultiSeat |
| Seat | SeatPreset | MultiSeat |
| Provider | sunshine.conf | MultiSeat (generated) |
| Runtime | SeatInfo | MultiSeat |

### Rules

- Credentials never in config files
- Configuration generated, not edited

---

## 17. Recovery

### Failure Types

| Type | Detection | Recovery |
|------|-----------|----------|
| Provider crash | 5s health | Auto-restart |
| Game crash | Process exit | Optional restart |
| Session disconnect | Health check | Reconnect |
| Display lost | Health check | Late detection |

### Backoff

| Crash | Backoff |
|-------|---------|
| 1-2 | Immediate |
| 3 | 30s |
| 4 | 60s |
| 5+ | 120s |

---

## 18. Observability

### Logs

- Structured logging (ILogger)
- Console + file + dashboard

### Health

- 5s interval checks
- Session, provider, display

### Diagnostics

- HidHideInspector
- LogFilterInspector
- Advanced color check

---

## 19. API

### Operations

- Seat CRUD
- Start/stop streaming
- Provider management
- Account management
- Health/diagnostics

### Authentication

- API key (X-API-Key header)

---

## 20. Dependencies

| Dependency | License | Boundary |
|------------|---------|----------|
| Vibepollo | GPLv3 | External process |
| TermWrap | MIT | DLL proxy |
| SudoVDA | Unknown | Kernel driver |
| HidHide | MIT | Kernel driver |
| Windows | Proprietary | Platform |

---

## 21. Drivers

| Driver | Purpose | Required |
|--------|---------|----------|
| SudoVDA | Virtual display | Yes |
| HidHide | Gamepad isolation | No (optional) |
| TermWrap | RDP patching | Yes |

---

## 22. Architectural Invariants

20 invariants defined in ARCHITECTURAL-INVARIANTS.md

Key invariants:
- One User per Seat
- One Session per Seat
- One Provider per Seat
- Port blocks exclusive
- Credentials protected
- Every process has owner
- Restart bounded

---

## 23. Known Limitations

1. No HDR (EnableHdr is no-op)
2. No microphone (RDP limitation)
3. No K/M isolation (InputHookManager no-op)
4. No Steam multi-instance
5. No game RDP compatibility
6. No game crash detection
7. No process tracking (target)
8. No seat state persistence (target)

---

## 24. Open Questions

1. SudoVDA license terms?
2. HDR enablement feasibility?
3. Steam multi-instance approach?
4. K/M isolation re-architecture?

---

## 25. ADR Candidates

12 ADR candidates in ADR-CANDIDATES.md

Key candidates:
- Provider abstraction
- Process ownership
- Job Objects
- Display backend
- Input backend
- Audio backend
- Session lifecycle
- Credential transport
- Provider bootstrap
- Recovery policy
- Seat state persistence
- Process tracking

---

## Evidence

| Section | Source | Status |
|---------|--------|--------|
| System purpose | Research + analysis | FACT |
| Principles | Research findings | FACT |
| Domain model | Source code | FACT |
| State machines | Source code + research | FACT |
| Provider contract | Helios + Vibepollo | FACT |
| Process architecture | Helios + Windows API | FACT |
| Display architecture | SudoVDA integration | FACT |
| Audio architecture | PerSession implementation | FACT |
| Input architecture | HidHide + Vibepollo | FACT |
| Game architecture | ProcessInjector | FACT |
| Steam architecture | SharedGameLibrary | FACT |
| Security architecture | DPAPI + ACL | FACT |
| Invariants | All architecture docs | FACT |
| Risks | Research + analysis | FACT |
| ADR candidates | Research findings | FACT |
