# Event Model

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define domain and application events.

---

## Domain Events

### Seat Events

| Event | Description | Trigger |
|-------|-------------|---------|
| SeatCreated | Seat provisioned | ProvisionSeatAsync |
| SeatReady | All components started | Provisioning complete |
| SeatStarted | Streaming began | Client connected |
| SeatStopped | Streaming ended | Client disconnected |
| SeatDegraded | Component failed | Health check |
| SeatRecovered | Component restored | Recovery success |
| SeatTearingDown | Teardown began | TeardownSeatAsync |
| SeatTornDown | Teardown complete | All resources released |
| SeatFailed | Provisioning failed | Exception thrown |

### Provider Events

| Event | Description | Trigger |
|-------|-------------|---------|
| ProviderStarted | Process launched | StartAsync |
| ProviderStopped | Process terminated | Stop |
| ProviderCrashed | Process exited unexpectedly | Health check |
| ProviderRestarted | Process restarted | Recovery |
| ProviderHealthCheckFailed | HTTP ping failed | Health check |

### Session Events

| Event | Description | Trigger |
|-------|-------------|---------|
| SessionCreated | RDP session created | LaunchSessionAsync |
| SessionConnected | mstsc connected | Session active |
| SessionDisconnected | Network/sleep | Health check |
| SessionReconnected | mstsc reconnected | Reconnect |
| SessionTerminated | Logoff complete | LogoffSession |

### Game Events

| Event | Description | Trigger |
|-------|-------------|---------|
| GameStarted | Process launched | LaunchInSessionAsync |
| GameExited | Process exited normally | Exit code 0 |
| GameCrashed | Process crashed | Exit code != 0 |

### Display Events

| Event | Description | Trigger |
|-------|-------------|---------|
| DisplayCreated | SudoVDA display created | CreateDisplayAsync |
| DisplayAssigned | UUID discovered | ParseSudoVdaDisplayId |
| DisplayIsolated | Primary + shrunk applied | ApplyDisplayIsolationAsync |
| DisplayDestroyed | Display removed | DestroyDisplayAsync |

---

## Event Usage

### Health Check → Degraded

```
Health check fails
    → SeatDegraded event
    → Recovery triggered
```

### Recovery → Ready

```
Recovery succeeds
    → SeatRecovered event
    → SeatReady state
```

### Teardown → Idle

```
Teardown complete
    → SeatTornDown event
    → SeatIdle state
```

---

## Event Storage

### Current State

Events are logged via ILogger.

### Target State

| Storage | Purpose |
|---------|---------|
| ILogger | Real-time logs |
| Event store (future) | Audit trail |
| WebSocket (existing) | Dashboard updates |

**DECISION**: Event store is future work.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatStatus enum exists | SeatStatus.cs | FACT |
| WebSocket broadcasts seat updates | WebSocketHub | FACT |
| Events logged via ILogger | Service code | FACT |
