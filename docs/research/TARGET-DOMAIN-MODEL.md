# Target Domain Model

**Date**: 2026-08-30
**Purpose**: Define core entities for the MultiSeat-Extended domain

---

## Entities

### Host

| Aspect | Details |
|--------|---------|
| Identity | Machine SID, hostname |
| Lifecycle | Always exists (singleton) |
| Relationships | Contains Seats |
| Owner | System |
| Persistence | Implicit (current machine) |

### Seat

| Aspect | Details |
|--------|---------|
| Identity | Guid Id |
| Lifecycle | Provisioning → Configuring → Ready → Streaming → TearingDown → Idle/Error |
| Relationships | Has User, Session, Display, AudioEndpoint, InputDevices, Games, Provider |
| Owner | MultiSeat-Extended |
| Persistence | SeatInfo (currently in-memory, target: disk) |

### User

| Aspect | Details |
|--------|---------|
| Identity | Windows account name, SID |
| Lifecycle | Created before Seat, deleted after Seat teardown |
| Relationships | Belongs to Seat |
| Owner | AccountManager |
| Persistence | Windows local account |

### Session

| Aspect | Details |
|--------|---------|
| Identity | Windows SessionId (int) |
| Lifecycle | Created during provisioning, logoff during teardown |
| Relationships | Belongs to Seat, owns Display, AudioEndpoint |
| Owner | SessionLauncher |
| Persistence | Windows Terminal Services |

### Display

| Aspect | Details |
|--------|---------|
| Identity | SudoVDA device UUID (string) |
| Lifecycle | Created during provisioning, destroyed during teardown |
| Relationships | Assigned to Seat, targeted by Provider |
| Owner | VirtualDisplayManager + SudoVDA driver |
| Persistence | SudoVDA driver state |

### AudioEndpoint

| Aspect | Details |
|--------|---------|
| Identity | Windows audio endpoint ID |
| Lifecycle | Created with Session (Remote Audio), destroyed with Session |
| Relationships | Belongs to Session |
| Owner | Windows RDP |
| Persistence | Windows audio subsystem |

### InputDevice

| Aspect | Details |
|--------|---------|
| Identity | HID device instance path |
| Lifecycle | Physical device (persistent) |
| Relationships | Assigned to Seat via HidHide jail |
| Owner | HidHide driver |
| Persistence | HidHide blacklist |

### Game

| Aspect | Details |
|--------|---------|
| Identity | Executable path + arguments |
| Lifecycle | Launched during streaming, killed during teardown |
| Relationships | Runs in Session, tracked by ProcessTracker |
| Owner | ProcessInjector |
| Persistence | None (runtime only) |

### GameProcess

| Aspect | Details |
|--------|---------|
| Identity | Process ID (int) |
| Lifecycle | Created by Game launch, terminated on crash/teardown |
| Relationships | Belongs to Game, tracked by ProcessTracker |
| Owner | ProcessTracker |
| Persistence | None (runtime only) |

### StreamingProvider

| Aspect | Details |
|--------|---------|
| Identity | Provider name (string) |
| Lifecycle | Always available (singleton) |
| Relationships | Creates ProviderInstances |
| Owner | ProviderManager |
| Persistence | Configuration (appsettings.json) |

### ProviderInstance

| Aspect | Details |
|--------|---------|
| Identity | Process ID (int) + Seat ID |
| Lifecycle | Started during provisioning, stopped during teardown |
| Relationships | Belongs to Seat, uses StreamingProvider |
| Owner | VibepolloManager (current), ProviderManager (target) |
| Persistence | Process state, configuration |

---

## Entity Relationships

```
Host (1)
  └── Seat (N)
        ├── User (1)
        │     └── Windows account
        ├── Session (1)
        │     ├── Display (1)
        │     │     └── SudoVDA UUID
        │     ├── AudioEndpoint (1)
        │     │     └── Remote Audio
        │     └── InputDevices (N)
        │           └── HidHide jail
        ├── Games (N)
        │     └── GameProcess (N)
        │           └── PID tracking
        └── ProviderInstance (1)
              ├── StreamingProvider (1)
              ├── Config (sunshine.conf)
              └── Health (HTTP ping)
```

---

## Value Objects

### PortBlock

| Aspect | Details |
|--------|---------|
| Identity | PortBase (int) |
| Composition | 30 consecutive ports |
| Relationships | Assigned to Seat |
| Validation | Must not overlap with other seats |

### RdpGeometry

| Aspect | Details |
|--------|---------|
| Identity | Width × Height |
| Composition | Width (int), Height (int) |
| Relationships | Used by Session creation |
| Validation | Must be valid mstsc geometry |

### SeatStatus

| Aspect | Details |
|--------|---------|
| Values | Idle, Provisioning, Configuring, Ready, Streaming, TearingDown, Error |
| Transitions | Defined by provisioning pipeline |

---

## Aggregates

### Seat Aggregate

- **Root**: Seat
- **Entities**: User, Session, Display, AudioEndpoint, GameProcess, ProviderInstance
- **Value Objects**: PortBlock, RdpGeometry, SeatStatus
- **Invariants**: Seat must have exactly one User, one Session, one Display, one ProviderInstance

### Provider Aggregate

- **Root**: StreamingProvider
- **Entities**: ProviderInstance
- **Value Objects**: ProviderHealth
- **Invariants**: ProviderInstance must belong to exactly one Seat

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatInfo is the core entity | SeatManager.cs | VERIFIED |
| Seat has AccountName | SeatInfo model | VERIFIED |
| Seat has SessionId | SeatInfo model | VERIFIED |
| Seat has PortBase | SeatInfo model | VERIFIED |
| Seat has DisplayDevicePath | SeatInfo model | VERIFIED |
| Seat has VibepolloProcessId | SeatInfo model | VERIFIED |
| SeatStatus enum exists | SeatStatus.cs | VERIFIED |
| PortBlock is 30 ports | Constants.PortsPerSeat = 30 | VERIFIED |
| MaxSeats = 8 | Constants.MaxSeats = 8 | VERIFIED |
