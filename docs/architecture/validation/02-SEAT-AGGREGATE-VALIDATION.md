# Seat Aggregate Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate Seat as aggregate root against actual source code.

---

## 1. Does Seat Own These Resources?

### User

**Evidence**: SeatManager.cs
```csharp
if (!_accounts.AccountExists(request.AccountName))
    throw new InvalidOperationException($"Account '{request.AccountName}' does not exist.");
```

**Analysis**: User is NOT created by SeatManager. User must exist BEFORE Seat is created.

**VERDICT**: User is NOT owned by Seat. User is a prerequisite.

---

### Session

**Evidence**: SeatManager.cs
```csharp
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
    seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));
```

**Analysis**: Session is created by SeatManager during provisioning.

**VERDICT**: Session IS owned by Seat.

---

### Display

**Evidence**: SeatManager.cs
```csharp
await _displayManager.CreateDisplayAsync(seat, ct);
```

**Analysis**: Display is created by SeatManager during provisioning.

**VERDICT**: Display IS owned by Seat.

---

### Audio

**Evidence**: SeatManager.cs
```csharp
// PerSession (the only supported mode) needs no host-side audio device
_logger.LogInformation(
    "Seat {Id}: per-session audio — Vibepollo captures the session's own Remote Audio endpoint",
    seat.Id);
```

**Analysis**: Audio is managed by Windows RDP, not SeatManager.

**VERDICT**: Audio is NOT owned by Seat. Audio is a Windows primitive.

---

### Input

**Evidence**: SeatManager.cs
```csharp
_hidHide.CloakForSession(seat);
_inputHookManager.InstallForSession((uint)seat.SessionId);
```

**Analysis**: Input isolation is applied by SeatManager.

**VERDICT**: Input IS owned by Seat.

---

### Provider

**Evidence**: SeatManager.cs
```csharp
seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
```

**Analysis**: Provider is started by SeatManager.

**VERDICT**: Provider IS owned by Seat.

---

### Games

**Evidence**: SeatManager.cs
```csharp
await _processInjector.LaunchInSessionAsync(
    seat.SessionId, seat.AccountName,
    request.ExecutablePath, request.Arguments, request.WorkingDirectory, ct);
```

**Analysis**: Games are launched by SeatManager.

**VERDICT**: Games ARE owned by Seat.

---

## 2. Resources with Independent Lifecycle

| Resource | Independent? | Evidence |
|----------|--------------|----------|
| User | Yes (created before Seat) | AccountManager |
| Session | No (created with Seat) | SessionLauncher |
| Display | No (created with Seat) | VirtualDisplayManager |
| Audio | Yes (Windows primitive) | RDP Remote Audio |
| Input | No (applied with Seat) | HidHideConfigurator |
| Provider | No (started with Seat) | VibepolloManager |
| Games | No (launched with Seat) | ProcessInjector |

---

## 3. Resources that Survive Seat

| Resource | Survives? | Evidence |
|----------|-----------|----------|
| User | Yes (not deleted on teardown) | AccountManager |
| Session | No (logged off on teardown) | SessionLauncher |
| Display | No (destroyed on teardown) | VirtualDisplayManager |
| Audio | Yes (Windows primitive) | RDP Remote Audio |
| Input | No (uncloaked on teardown) | HidHideConfigurator |
| Provider | No (killed on teardown) | VibepolloManager |
| Games | No (killed on teardown) | ProcessInjector |

---

## 4. Hidden Global State

### ConcurrentDictionary<Guid, SeatInfo> _seats

**Evidence**: SeatManager.cs
```csharp
private readonly ConcurrentDictionary<Guid, SeatInfo> _seats = new();
```

**Analysis**: In-memory state. Lost on service restart.

**VERDICT**: Global state exists (in-memory).

---

### ConcurrentDictionary<Guid, VibepolloInstance> _instances

**Evidence**: VibepolloManager.cs
```csharp
private readonly ConcurrentDictionary<Guid, VibepolloInstance> _instances = new();
```

**Analysis**: In-memory state. Lost on service restart.

**VERDICT**: Global state exists (in-memory).

---

### ConcurrentDictionary<Guid, VirtualDisplay> _displays

**Evidence**: VirtualDisplayManager.cs
```csharp
private readonly ConcurrentDictionary<Guid, VirtualDisplay> _displays = new();
```

**Analysis**: In-memory state. Lost on service restart.

**VERDICT**: Global state exists (in-memory).

---

## 5. Circular Dependencies

### SeatManager → VibepolloManager → ProcessInjector → SeatManager?

**Evidence**: VibepolloManager.cs
```csharp
public VibepolloManager(
    ILogger<VibepolloManager> logger,
    IOptions<MultiSeatOptions> options,
    VibepolloConfigBuilder configBuilder,
    ProcessInjector processInjector)
```

**Analysis**: No circular dependency. VibepolloManager depends on ProcessInjector, not SeatManager.

**VERDICT**: No circular dependencies.

---

## 6. Singleton Managers

| Manager | Singleton? | Evidence |
|---------|------------|----------|
| SeatManager | Yes | DI container |
| AccountManager | Yes | DI container |
| SessionLauncher | Yes | DI container |
| VirtualDisplayManager | Yes | DI container |
| VibepolloManager | Yes | DI container |
| ProcessInjector | Yes | DI container |
| HidHideConfigurator | Yes | DI container |

**VERDICT**: All managers are singletons (DI container).

---

## 7. Does Aggregate Pattern Fit?

### Analysis

**Arguments FOR**:
1. SeatManager orchestrates all subsystems
2. Seat owns lifecycle of resources
3. Seat is the consistency boundary

**Arguments AGAINST**:
1. User has independent lifecycle (created before Seat)
2. Audio is a Windows primitive (not owned by Seat)
3. Multiple singleton managers (global state)

### Verdict

**AGGREGATE PATTERN FITS** with the following qualifications:
- User is a prerequisite, not a child
- Audio is delegated to Windows
- Global state needs persistence

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| User created before Seat | AccountManager | FACT |
| Session created with Seat | SessionLauncher | FACT |
| Display created with Seat | VirtualDisplayManager | FACT |
| Audio is Windows primitive | RDP Remote Audio | FACT |
| Input applied with Seat | HidHideConfigurator | FACT |
| Provider started with Seat | VibepolloManager | FACT |
| Games launched with Seat | ProcessInjector | FACT |
| In-memory state | ConcurrentDictionary | FACT |
| No circular dependencies | Dependency analysis | FACT |
| All managers singletons | DI container | FACT |
