# ADR: ProcessTracking Integration Boundary

**Date**: 2026-09-02
**Status**: APPROVED
**Supersedes**: P1-0 M1/M2 event-driven recovery design

---

## Decision

The first incremental ProcessTracking integration will provide process identity protection, ownership tracking, lifecycle observation, and correct expected-exit handling — WITHOUT changing the recovery architecture.

Recovery remains polling-based via `SessionHealthCheck → ApolloManager.RestartAsync`.

`IProviderLifecycleConsumer` is NOT used in this phase.

`IProcessMonitor.ProcessExited` does NOT independently trigger Apollo restart.

---

## Current Architecture

```
SeatManager.ProvisionSeatAsync
    → ApolloManager.StartAsync(seat, ct)
    → returns int PID
    → seat.ApolloProcessId = PID

SessionHealthCheck.CheckSeatAsync (every 5s)
    → IsProcessAlive(seat.ApolloProcessId)  [Process.GetProcessById]
    → if dead: _apolloManager.RestartAsync(seat, ct)
    → seat.ApolloProcessId = newPid

SeatManager.TeardownSeatInternalAsync
    → ApolloManager.Stop(seat)  [Process.Kill(entireProcessTree)]
    → seat.ApolloProcessId = 0
```

Key facts:
- ApolloManager uses raw `int` PID (`seat.ApolloProcessId`)
- ApolloManager._instances tracks `(SeatId → ApolloInstance with PID + StartedAt + RestartCount)`
- No ProcessTracking code is wired into any production flow
- Recovery is a direct method call, not an event-driven chain

---

## Problem with P1-0 M1/M2 Proposal

The P1-0 document proposed:

```
IProcessMonitor.ProcessExited
    → IProviderLifecycleConsumer
    → SeatManager
    → ApolloManager.RestartAsync
```

This has three structural problems:

1. **Unnecessary layer**: `IProviderLifecycleConsumer` adds a coupling layer between detection and action. The current direct-call pattern (`SessionHealthCheck → RestartAsync`) is simpler and equally correct.

2. **Wrong recovery owner**: SeatManager is a seat lifecycle orchestrator, not a provider recovery service. Adding recovery responsibility violates its single purpose.

3. **Dual restart risk**: If both `IProcessMonitor` and `SessionHealthCheck` independently trigger restart, two Apollo processes could launch simultaneously (race condition in `RestartAsync`).

---

## Approved Incremental Boundary

### Phase 1: Observation (this decision)

```
ApolloManager.StartAsync
    → create Apollo process
    → obtain ProcessIdentity (PID + StartedAt)
    → register ownership with IProcessTracker
    → start monitoring with IProcessMonitor

ApolloManager.Stop / intentional termination
    → mark expected exit via IProcessMonitor.MarkExpectedExit
    → terminate Apollo (Process.Kill)
    → unregister from IProcessTracker
    → stop monitoring via IProcessMonitor.StopMonitoring

SessionHealthCheck
    → remains the recovery owner
    → remains polling-based (every 5s)
    → continues calling ApolloManager.RestartAsync
    → NO subscription to IProcessMonitor.ProcessExited

IProcessMonitor
    → provides lifecycle observation
    → does NOT independently trigger Apollo restart
    → exists for future use, not active in recovery path
```

### What Phase 1 provides

| Capability | Before | After |
|-----------|--------|-------|
| PID reuse protection | None — raw int PID | ProcessIdentity (PID + StartedAt) |
| Process ownership | Implicit in ApolloManager._instances | Explicit in IProcessTracker |
| Expected exit handling | Not modeled | MarkExpectedExit prevents wasted poll + restart attempt |
| Lifecycle observation | Polling only | Event-driven available (not active in recovery) |
| Cleanup guarantee | Process.Kill only | Process.Kill + Job Object safety net (future) |

### What Phase 1 does NOT change

| Concern | Current | After Phase 1 |
|---------|---------|---------------|
| Recovery owner | SessionHealthCheck | SessionHealthCheck (unchanged) |
| Detection method | Polling (5s) | Polling (5s) (unchanged) |
| Restart execution | ApolloManager.RestartAsync | ApolloManager.RestartAsync (unchanged) |
| Recovery architecture | Direct call | Direct call (unchanged) |

---

## Responsibilities

### ApolloManager

- Register process with IProcessTracker after StartAsync
- Start monitoring with IProcessMonitor after StartAsync
- Call MarkExpectedExit before Kill in Stop
- Unregister from IProcessTracker after Stop
- Stop monitoring after Stop
- Handle restart logic (RestartAsync, MaxRestartAttempts)

### SessionHealthCheck

- Remain the sole recovery trigger
- Continue polling-based detection (IsProcessAlive)
- Call ApolloManager.RestartAsync on crash detection
- Do NOT subscribe to IProcessMonitor.ProcessExited

### IProcessTracker

- Track process ownership (ProcessIdentity → SeatId → ManagedProcessType)
- Enforce INVARIANT-2 (one process, one seat)
- Provide GetByOwner for seat-scoped queries

### IProcessMonitor

- Provide event-driven exit notification (ProcessExited event)
- Support MarkExpectedExit for intentional termination
- Support StopMonitoring for cleanup
- Do NOT trigger recovery independently

---

## Why Duplicate Recovery Must Be Avoided

If both IProcessMonitor and SessionHealthCheck independently call RestartAsync:

1. Process exits → IProcessMonitor fires ProcessExited
2. Simultaneously, SessionHealthCheck polls → finds process dead
3. Both call ApolloManager.RestartAsync
4. First call launches new PID, updates _instances
5. Second call finds _instances updated, but race window exists
6. Result: potentially two Apollo processes for one seat

The cleanest solution: only SessionHealthCheck triggers restart. IProcessMonitor updates state (marks PID as dead) but does not directly call RestartAsync. SessionHealthCheck detects the state change on next poll and handles recovery through the existing, proven path.

---

## Why IProviderLifecycleConsumer Is Not Used in Phase 1

1. **Wrong abstraction level**: The interface assumes a generic "any provider, any recovery" pattern, but the actual system has exactly one provider (Apollo) with exactly one restart path (RestartAsync).

2. **Adds unnecessary layer**: The interface sits between detection and action. A simple event subscription in SessionHealthCheck achieves the same result with less coupling.

3. **Naming mismatch**: "Consumer" implies passive reception. The actual role would be active recovery orchestration — a different responsibility.

4. **Domain/infrastructure confusion**: The interface lives in MultiSeat.Shared (domain layer), but its only meaningful implementor would be a SessionHealthCheck or recovery service — which is infrastructure, not domain.

If a provider abstraction is eventually needed (IStreamingProvider), recovery should be part of THAT interface, not a separate IProviderLifecycleConsumer.

---

## ProcessGroup / Job Object Integration

ProcessGroup (Job Object with KILL_ON_JOB_CLOSE) is a separate concern from ProcessTracking observation. It should NOT be bundled into this first integration unless later analysis proves it necessary.

Rationale:
- ProcessGroup provides cleanup guarantee (safety net when Process.Kill fails)
- This is a different responsibility from ownership tracking and lifecycle observation
- Bundling increases integration scope and risk
- Can be added independently in a later phase

---

## Event-Driven Recovery: Future Option

Event-driven recovery (IProcessMonitor.ProcessExited → restart) remains a future architectural option, not part of this phase.

When/if pursued, it would require:
- SessionHealthCheck to subscribe to IProcessMonitor.ProcessExited
- A recovery gate to prevent duplicate restarts
- Careful handling of expected vs unexpected exits
- Integration testing with real Apollo crashes

This is explicitly NOT planned or committed in the current repository.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current recovery is polling-based | SessionHealthCheck.cs | FACT |
| ApolloManager.RestartAsync handles restart | ApolloManager.cs | FACT |
| IProviderLifecycleConsumer has zero consumers | grep analysis | FACT |
| ProcessTracking is disconnected from production | grep analysis | FACT |
| Dual-restart race is a real risk | RestartAsync code analysis | ANALYSIS |
| MarkExpectedExit prevents wasted poll | SessionHealthCheck code analysis | ANALYSIS |
