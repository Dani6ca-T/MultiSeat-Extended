# IPC Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define inter-process communication patterns and boundaries.

---

## Current IPC

### Direct Method Calls

MultiSeat.Service is a single process. All subsystems communicate via direct method calls.

```
SeatManager
    ├── AccountManager (method call)
    ├── SessionLauncher (method call)
    ├── VirtualDisplayManager (method call)
    ├── VibepolloManager (method call)
    ├── ProcessInjector (method call)
    └── HidHideConfigurator (method call)
```

**FACT**: Current implementation uses direct method calls.

---

## Future IPC Options

### Named Pipes

| Aspect | Details |
|--------|---------|
| Use case | Service ↔ App communication |
| Trust boundary | LOCAL_MACHINE |
| Authentication | Windows identity |
| Failure behavior | Reconnect, retry |

**FACT**: Helios uses Named Pipe IPC.

### HTTP (localhost)

| Aspect | Details |
|--------|---------|
| Use case | API ↔ Dashboard |
| Trust boundary | localhost |
| Authentication | API key |
| Failure behavior | Retry, timeout |

**FACT**: Current API uses HTTP.

### gRPC

| Aspect | Details |
|--------|---------|
| Use case | High-performance IPC |
| Trust boundary | localhost |
| Authentication | TLS, API key |
| Failure behavior | Retry, deadline |

**DECISION**: gRPC is not needed for current architecture.

### Filesystem

| Aspect | Details |
|--------|---------|
| Use case | Configuration sharing |
| Trust boundary | File ACL |
| Authentication | File permissions |
| Failure behavior | Retry, file lock |

**FACT**: sunshine.conf is filesystem-based IPC.

---

## IPC Decision

### Current State

Direct method calls within single process.

### Future State

If service/app separation is needed:
- Named Pipes for service ↔ app
- HTTP for API ↔ Dashboard
- Filesystem for configuration

### Decision

**KEEP direct method calls** until service/app separation is needed.

**DECISION**: No IPC changes until architectural split.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current IPC is direct method calls | Service structure | FACT |
| Helios uses Named Pipes | Helios SpawnerWorker | FACT |
| Configuration uses filesystem | sunshine.conf | FACT |
| API uses HTTP | ApiServer.cs | FACT |
