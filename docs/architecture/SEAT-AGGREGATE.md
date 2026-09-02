# Seat Aggregate

**Date**: 2026-08-30
**Status**: FROZEN

---

## Aggregate Root: Seat

The **Seat** is the aggregate root for the multi-seat domain. It owns and orchestrates all resources required for an isolated interactive session.

---

## Aggregate Boundary

```
Seat (Aggregate Root)
├── User
├── Session
├── Display
├── AudioEndpoint
├── InputDevices (collection)
├── GameProcesses (collection)
└── ProviderInstance
```

---

## Why Seat is Aggregate Root

### Evidence

1. **SeatManager orchestrates all subsystems** — SeatManager.ProvisionSeatAsync coordinates User, Session, Display, Audio, Input, Provider, and Game.

2. **Seat owns lifecycle** — Seat creation triggers resource allocation; Seat teardown releases all resources.

3. **Seat is the consistency boundary** — All operations on Seat's children go through SeatManager.

4. **Seat is the identity boundary** — All child entities reference SeatId.

### Reasoning

- **User** cannot exist without Seat (created for Seat)
- **Session** cannot exist without Seat (launched for Seat)
- **Display** is assigned to Seat (UUID stored in SeatInfo)
- **AudioEndpoint** belongs to Session (which belongs to Seat)
- **InputDevices** are assigned to Seat (HidHide rules per Seat)
- **GameProcesses** run in Session (which belongs to Seat)
- **ProviderInstance** is launched for Seat (PID stored in SeatInfo)

### Counter-Arguments

- **ProviderInstance** could be aggregate root if providers were independent
- But providers are NOT independent — they are managed per Seat

---

## Invariants

### Structural Invariants

1. **One Seat has exactly one User** — Each seat has one Windows account
2. **One Seat has at most one active Session** — Sessions are 1:1 with Seats
3. **One Seat has at most one Display** — One virtual display per seat
4. **One Seat has at most one AudioEndpoint** — Per-session audio
5. **One Seat has at most one ProviderInstance** — One streaming server per seat
6. **One Seat has zero or more GameProcesses** — Multiple games can run

### Lifecycle Invariants

7. **User created before Session** — Account must exist before RDP logon
8. **Session created before Display** — RDP session must exist for display assignment
9. **Display created before Provider** — Provider needs display to capture
10. **Provider started before Game** — Provider must be running for game streaming
11. **Game stopped before Provider** — Game must stop before provider teardown
12. **Provider stopped before Session** — Provider must stop before session logoff

### Resource Invariants

13. **PortBlock is unique** — No two Seats share port blocks
14. **Display UUID is unique** — No two Seats share SudoVDA displays
15. **Session ID is unique** — No two Seats share Windows sessions
16. **PID is unique** — No two processes share PIDs

---

## Aggregate Operations

### ProvisionSeat (Create)

```
Input: SeatRequest (AccountName, Width, Height, Fps, LaunchApp)
Output: SeatInfo

Steps:
1. Validate capacity + account
2. Allocate port block
3. Create User (if needed)
4. Create Session (RDP loopback)
5. Create Display (SudoVDA)
6. Open firewall ports
7. Start Provider (Vibepollo)
8. Apply display isolation
9. Apply input isolation
10. Mark Ready
```

### TeardownSeat (Delete)

```
Input: SeatId
Output: void

Steps (reverse order, best-effort):
1. Stop games
2. Uninstall input hooks
3. Uncloak HidHide
4. Destroy controllers
5. Stop Provider
6. Close Job Object (kill all processes)
7. Close firewall ports
8. Destroy Display
9. Disconnect Session
10. Logoff Session
11. Release ports
12. Cleanup config
13. Delete User (if created)
```

### StartStreaming (Update)

```
Input: SeatId
Output: void

Steps:
1. Launch app (if specified)
2. Mark Streaming state
3. Notify clients
```

### StopStreaming (Update)

```
Input: SeatId
Output: void

Steps:
1. Kill launched apps
2. Mark Ready state
3. Notify clients
```

---

## Aggregate Consistency Rules

### Rule 1: Provisioning is Atomic

Either all provisioning steps succeed, or the Seat enters Error state and best-effort teardown runs.

### Rule 2: Teardown is Best-Effort

Each teardown step is wrapped in try/catch. Failure of one step does not prevent other steps.

### Rule 3: State Transitions are Monotonic

Seat status only moves forward through the lifecycle (Idle → Provisioning → Configuring → Ready → Streaming → TearingDown → Idle/Error).

### Rule 4: Resource Allocation is Exclusive

Port blocks, display UUIDs, and session IDs are exclusive to one Seat.

### Rule 5: Process Ownership is Clear

Every managed process (Provider, Game, Helper) has exactly one owning Seat.

---

## Consistency Boundary

```
┌─────────────────────────────────────────────┐
│                 Seat Aggregate               │
│                                              │
│  Seat (Root)                                │
│  ├── User ──────────────── (consistency)    │
│  ├── Session ───────────── (consistency)    │
│  ├── Display ───────────── (consistency)    │
│  ├── AudioEndpoint ─────── (consistency)    │
│  ├── InputDevices ──────── (consistency)    │
│  ├── GameProcesses ─────── (consistency)    │
│  └── ProviderInstance ──── (consistency)    │
│                                              │
│  All mutations go through SeatManager       │
│  All state is persisted atomically          │
│  All resources are released on teardown     │
└─────────────────────────────────────────────┘
```

---

## Cross-Agregate References

### Seat → Provider (StreamingProvider)

Seat references Provider by name (string). This is a **unidirectional reference** — Provider does not reference Seat.

**Consistency**: Not enforced within aggregate. ProviderInstance belongs to Seat aggregate.

### Seat → Host

Seat references Host implicitly (current machine).

**Consistency**: Not enforced. Host is singleton.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatManager orchestrates all subsystems | SeatManager.cs | FACT |
| Seat owns lifecycle | SeatManager.ProvisionSeatAsync | FACT |
| Seat is consistency boundary | SeatManager coordinates all operations | FACT |
| PortBlock is exclusive | PortAllocator | FACT |
| Display UUID is unique | VirtualDisplayManager | FACT |
| Session ID is unique | Windows Terminal Services | FACT |
| PID is unique | Windows process model | FACT |
