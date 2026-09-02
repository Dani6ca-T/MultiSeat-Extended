# Observability

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define logging, metrics, health, events, and diagnostics.

---

## Logs

### Structured Logging

```csharp
_logger.LogInformation("Seat {Id}: provider started (PID {Pid})", seat.Id, pid);
```

### Log Levels

| Level | Usage |
|-------|-------|
| Debug | Detailed diagnostics |
| Information | Normal operations |
| Warning | Degraded state, non-critical failures |
| Error | Critical failures, provisioning errors |

### Log Destinations

| Destination | Scope |
|-------------|-------|
| Console | Service output |
| File | Persistent logs |
| Dashboard | Real-time view |

---

## Health

### Health Checks

| Check | Interval | Owner |
|-------|----------|-------|
| Session active | 5s | SessionHealthCheck |
| Provider alive | 5s | VibepolloServerQuery |
| Display available | 5s | TryLateDisplayDetectionAsync |

### Health States

| State | Meaning |
|-------|---------|
| Healthy | All checks pass |
| Degraded | Some checks fail |
| Unhealthy | Critical checks fail |

---

## Metrics

### Current State

No metrics endpoint exists.

### Target State

| Metric | Type |
|--------|------|
| seat_count | Gauge |
| seat_status | Gauge |
| provider_restarts | Counter |
| health_check_failures | Counter |

**DECISION**: Metrics endpoint is P3 priority.

---

## Events

### Domain Events

| Event | Description |
|-------|-------------|
| SeatCreated | Seat provisioned |
| SeatStarted | Streaming started |
| SeatStopped | Seat torn down |
| ProviderCrashed | Provider process crashed |
| GameCrashed | Game process crashed |
| SessionDisconnected | RDP session disconnected |

**DECISION**: Event model is defined in EVENT-MODEL.md.

---

## Diagnostics

### Built-In Diagnostics

| Tool | Purpose |
|------|---------|
| HidHideInspector | Verify HidHide rules |
| LogFilterInspector | Filter Vibepollo logs |
| --advanced-color | Check HDR capability |
| GET /api/seats/{id}/diagnostics | Seat diagnostics |

---

## Audit

### What to Audit

| Action | Audit |
|--------|-------|
| Seat creation | Log + event |
| Seat deletion | Log + event |
| Provider restart | Log + event |
| Configuration change | Log |

### What NOT to Audit

| Action | Reason |
|--------|--------|
| Health checks | Too frequent |
| Process alive checks | Too frequent |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Structured logging used | Service code | FACT |
| 5s health check interval | SessionHealthCheck | FACT |
| No metrics endpoint | Codebase search | FACT (absent) |
| HidHideInspector exists | Diagnostics | FACT |
