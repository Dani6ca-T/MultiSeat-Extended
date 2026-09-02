# Domain Model

**Date**: 2026-08-30
**Status**: FROZEN

---

## Entities

### Host

| Aspect | Details |
|--------|---------|
| Identity | Machine SID + hostname (singleton) |
| State | Always exists |
| Lifecycle | Permanent |
| Relationships | Contains Seats |
| Owner | System |
| Persistence | Implicit (current machine) |
| Invariant | Exactly one Host per deployment |

**FACT**: Host is implicit — the machine running MultiSeat-Extended.

---

### Seat

| Aspect | Details |
|--------|---------|
| Identity | Guid (Id) |
| State | See STATE-MACHINES.md |
| Lifecycle | Created → Provisioned → Active → Stopped |
| Relationships | Has User, Session, Display, Audio, Input, Provider, Games |
| Owner | MultiSeat-Extended |
| Persistence | Disk (target) |
| Invariant | One Seat owns at most one active Session |

**FACT**: SeatInfo is the core entity in current implementation.

---

### User

| Aspect | Details |
|--------|---------|
| Identity | Windows account name (string) |
| State | Active / Deleted |
| Lifecycle | Created before Seat, deleted after teardown (if created by MultiSeat) |
| Relationships | Belongs to Seat |
| Owner | AccountManager |
| Persistence | Windows local account |
| Invariant | One User belongs to exactly one Seat |

**FACT**: AccountManager creates Windows local accounts.

---

### Session

| Aspect | Details |
|--------|---------|
| Identity | Windows SessionId (int) |
| State | Created → Connecting → Active → Disconnected → Terminated |
| Lifecycle | Created during provisioning, terminated during teardown |
| Relationships | Belongs to Seat, owns Display, AudioEndpoint |
| Owner | SessionManager |
| Persistence | Windows Terminal Services |
| Invariant | One Session belongs to exactly one Seat |

**FACT**: SessionLauncher creates RDP loopback sessions.

---

### Display

| Aspect | Details |
|--------|---------|
| Identity | SudoVDA device UUID (string) |
| State | Created → Assigned → Destroyed |
| Lifecycle | Created during provisioning, destroyed during teardown |
| Relationships | Assigned to Seat, targeted by Provider |
| Owner | DisplayManager + SudoVDA driver |
| Persistence | SudoVDA driver state |
| Invariant | One Display assigned to at most one Seat |

**FACT**: VirtualDisplayManager creates SudoVDA displays.

---

### AudioEndpoint

| Aspect | Details |
|--------|---------|
| Identity | Windows audio endpoint ID (string) |
| State | Created → Active → Destroyed |
| Lifecycle | Created with Session, destroyed with Session |
| Relationships | Belongs to Session |
| Owner | Windows RDP |
| Persistence | Windows audio subsystem |
| Invariant | One AudioEndpoint belongs to exactly one Session |

**FACT**: PerSession audio creates Remote Audio endpoint per session.

---

### InputDevice

| Aspect | Details |
|--------|---------|
| Identity | HID device instance path (string) |
| State | Connected / Disconnected |
| Lifecycle | Physical device (persistent) |
| Relationships | Assigned to Seat via HidHide jail |
| Owner | HidHide driver |
| Persistence | HidHide blacklist |
| Invariant | One InputDevice assigned to at most one Seat |

**FACT**: HidHideConfigurator manages session jail rules.

---

### Game

| Aspect | Details |
|--------|---------|
| Identity | Executable path + arguments (value object) |
| State | Defined → Launched → Running → Exited |
| Lifecycle | Defined in configuration, launched during streaming |
| Relationships | Runs in Session, tracked by ProcessTracker |
| Owner | ProcessInjector |
| Persistence | Configuration (apps.json) |
| Invariant | One Game definition can have multiple GameProcesses |

**FACT**: Games are defined in apps.json, launched via ProcessInjector.

---

### GameProcess

| Aspect | Details |
|--------|---------|
| Identity | Process ID (int) + Seat ID (Guid) |
| State | Starting → Running → Exited → Crashed |
| Lifecycle | Created by game launch, terminated on crash/teardown |
| Relationships | Belongs to Game, tracked by ProcessTracker |
| Owner | ProcessTracker |
| Persistence | None (runtime only) |
| Invariant | One GameProcess belongs to exactly one Seat |

**FACT**: ProcessTracker maps PID → Seat.

---

### StreamingProvider

| Aspect | Details |
|--------|---------|
| Identity | Provider name (string, e.g., "Vibepollo") |
| State | Available |
| Lifecycle | Permanent (singleton per provider type) |
| Relationships | Creates ProviderInstances |
| Owner | ProviderManager |
| Persistence | Configuration (appsettings.json) |
| Invariant | Exactly one StreamingProvider per provider type |

**FACT**: VibepolloManager manages Vibepollo provider.

---

### ProviderInstance

| Aspect | Details |
|--------|---------|
| Identity | Process ID (int) + Seat ID (Guid) |
| State | Created → Starting → Running → Degraded → Stopped → Failed |
| Lifecycle | Started during provisioning, stopped during teardown |
| Relationships | Belongs to Seat, uses StreamingProvider |
| Owner | ProviderManager (target) |
| Persistence | Process state, configuration |
| Invariant | One ProviderInstance belongs to exactly one Seat |

**FACT**: VibepolloManager manages per-seat Vibepollo process.

---

## Value Objects

### PortBlock

| Aspect | Details |
|--------|---------|
| Identity | PortBase (int) |
| Composition | 30 consecutive ports |
| Relationships | Assigned to Seat |
| Validation | Must not overlap with other seats |
| Invariant | PortBase >= 48100 (default) |

**FACT**: Constants.PortsPerSeat = 30, PortBase = 48100.

---

### RdpGeometry

| Aspect | Details |
|--------|---------|
| Identity | Width × Height (value object) |
| Composition | Width (int), Height (int) |
| Relationships | Used by Session creation |
| Validation | Must be valid mstsc geometry |
| Invariant | Width > 0, Height > 0 |

**FACT**: RdpGeometry.ForClient() creates valid geometry.

---

### SeatStatus

| Aspect | Details |
|--------|---------|
| Values | Idle, Provisioning, Configuring, Ready, Streaming, TearingDown, Error |
| Transitions | Defined by provisioning pipeline |
| Invariant | Status transitions follow state machine |

**FACT**: SeatStatus enum exists in current implementation.

---

## Aggregates

### Seat Aggregate (Root: Seat)

```
Seat (Root)
├── User
├── Session
├── Display
├── AudioEndpoint
├── InputDevices (collection)
├── GameProcesses (collection)
└── ProviderInstance
```

**Boundary**: All entities within the Seat aggregate are consistency-managed together.

**Invariant**: Seat must have exactly one User, one Session, one ProviderInstance. Display, AudioEndpoint, InputDevices, GameProcesses may be empty.

---

### Provider Aggregate (Root: StreamingProvider)

```
StreamingProvider (Root)
└── ProviderInstances (collection)
```

**Boundary**: Provider instances are managed independently per seat.

**Invariant**: ProviderInstance must belong to exactly one Seat.

---

## Relationships Diagram

```
Host (1)
  └── Seat (N)
        ├── User (1)
        │     └── Windows account
        ├── Session (1)
        │     ├── Display (0..1)
        │     │     └── SudoVDA UUID
        │     ├── AudioEndpoint (0..1)
        │     │     └── Remote Audio
        │     └── InputDevices (N)
        │           └── HidHide jail
        ├── GameProcesses (N)
        │     └── PID tracking
        └── ProviderInstance (0..1)
              ├── StreamingProvider (1)
              ├── Config (sunshine.conf)
              └── Health (HTTP ping)
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatInfo is core entity | SeatManager.cs | FACT |
| Seat has AccountName | SeatInfo model | FACT |
| Seat has SessionId | SeatInfo model | FACT |
| Seat has PortBase | SeatInfo model | FACT |
| Seat has DisplayDevicePath | SeatInfo model | FACT |
| Seat has VibepolloProcessId | SeatInfo model | FACT |
| User is Windows account | AccountManager.cs | FACT |
| Session is Windows SessionId | SessionLauncher.cs | FACT |
| Display is SudoVDA UUID | VirtualDisplayManager | FACT |
| AudioEndpoint is per-session | PerSession mode | FACT |
| GameProcess has PID | ProcessInjector | FACT |
| ProviderInstance has PID | VibepolloManager | FACT |
