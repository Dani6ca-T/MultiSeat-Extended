# Architectural Invariants

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define inviolable rules that must hold at all times.

---

## Structural Invariants

### I1: One Seat Has Exactly One User

**Statement**: Each Seat has exactly one Windows account.

**Evidence**: SeatInfo.AccountName, AccountManager creates one account per seat.

**Violation**: Would cause session creation failure.

---

### I2: One Seat Has At Most One Active Session

**Statement**: Each Seat has at most one active Windows session.

**Evidence**: SeatInfo.SessionId, SessionLauncher creates one session per seat.

**Violation**: Would cause resource conflicts.

---

### I3: One Seat Has At Most One Provider Instance

**Statement**: Each Seat has at most one running provider process.

**Evidence**: SeatInfo.VibepolloProcessId, VibepolloManager manages one process.

**Violation**: Would cause port conflicts, display conflicts.

---

### I4: One Seat Has At Most One Display

**Statement**: Each Seat has at most one virtual display.

**Evidence**: SeatInfo.DisplayDevicePath, VirtualDisplayManager creates one display.

**Violation**: Would cause capture confusion.

---

### I5: Port Blocks Are Exclusive

**Statement**: No two Seats share port blocks.

**Evidence**: PortAllocator allocates unique 30-port blocks.

**Violation**: Would cause network conflicts.

---

### I6: Display UUIDs Are Exclusive

**Statement**: No two Seats share SudoVDA display UUIDs.

**Evidence**: Each SudoVDA instance creates unique UUID.

**Violation**: Would cause display capture conflicts.

---

### I7: Session IDs Are Exclusive

**Statement**: No two Seats share Windows session IDs.

**Evidence**: Windows Terminal Services assigns unique IDs.

**Violation**: Would cause session conflicts.

---

### I8: PIDs Are Unique

**Statement**: No two processes share PIDs.

**Evidence**: Windows process model.

**Violation**: Impossible (OS guarantee).

---

## Lifecycle Invariants

### I9: User Created Before Session

**Statement**: Windows account must exist before RDP logon.

**Evidence**: SeatManager provisioning pipeline order.

**Violation**: Would cause RDP logon failure.

---

### I10: Session Created Before Display

**Statement**: RDP session must exist for display assignment.

**Evidence**: SeatManager provisioning pipeline order.

**Violation**: Would cause display creation failure.

---

### I11: Display Created Before Provider

**Statement**: Virtual display must exist for provider to capture.

**Evidence**: SeatManager provisioning pipeline order.

**Violation**: Would cause provider capture failure.

---

### I12: Provider Started Before Game

**Statement**: Streaming server must be running for game streaming.

**Evidence**: SeatManager provisioning pipeline order.

**Violation**: Would cause game not streamable.

---

### I13: Game Stopped Before Provider

**Statement**: Games must stop before provider teardown.

**Evidence**: TeardownSeatInternalAsync reverse order.

**Violation**: Would cause orphan game processes.

---

### I14: Provider Stopped Before Session

**Statement**: Provider must stop before session logoff.

**Evidence**: TeardownSeatInternalAsync reverse order.

**Violation**: Would cause provider crash.

---

## Resource Invariants

### I15: Every Managed Process Has an Owner

**Statement**: Every process launched by MultiSeat belongs to exactly one Seat.

**Evidence**: ProcessTracker maps PID → Seat.

**Violation**: Would cause orphan processes.

---

### I16: Every Restart Has Bounded Retry

**Statement**: Crash recovery has maximum retry count.

**Evidence**: MaxRestartAttempts = 3.

**Violation**: Would cause infinite restart loops.

---

### I17: Credentials Never Cross Public Models

**Statement**: Passwords, tokens, secrets never appear in SeatSpec, API wire, or logs.

**Evidence**: Security architecture.

**Violation**: Would cause credential exposure.

---

### I18: Core Never Depends on Windows

**Statement**: Domain/Core has no Windows API dependencies.

**Evidence**: Layer architecture.

**Violation**: Would break portability.

---

### I19: Provider Implementation Never Enters Core

**Statement**: Vibepollo-specific code never enters Core/Domain.

**Evidence**: Provider abstraction.

**Violation**: Would break provider independence.

---

### I20: A Stopped Seat Owns No Running Provider Process

**Statement**: When Seat is torn down, provider process is terminated.

**Evidence**: TeardownSeatInternalAsync stops provider.

**Violation**: Would cause orphan provider processes.

---

## Evidence

| Invariant | Source | Status |
|-----------|--------|--------|
| I1: One User per Seat | AccountManager | FACT |
| I2: One Session per Seat | SessionLauncher | FACT |
| I3: One Provider per Seat | VibepolloManager | FACT |
| I5: Port blocks exclusive | PortAllocator | FACT |
| I6: Display UUIDs exclusive | SudoVDA | FACT |
| I9: User before Session | Provisioning pipeline | FACT |
| I15: Every process has owner | ProcessTracker (target) | DECISION |
| I16: Restart bounded | MaxRestartAttempts | FACT |
| I17: Credentials protected | Security architecture | FACT |
