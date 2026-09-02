# API Boundary

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define API operations, boundaries, and contracts.

---

## API Operations

### Seat Operations

| Operation | Method | Endpoint | Description |
|-----------|--------|----------|-------------|
| Create seat | POST | /api/seats | Provision new seat |
| Get seat | GET | /api/seats/{id} | Get seat details |
| List seats | GET | /api/seats | List all seats |
| Delete seat | DELETE | /api/seats/{id} | Tear down seat |
| Start streaming | POST | /api/seats/{id}/start | Start streaming |
| Stop streaming | POST | /api/seats/{id}/stop | Stop streaming |
| Restart Vibepollo | POST | /api/seats/{id}/vibepollo/restart | Restart provider |
| Stop Vibepollo | POST | /api/seats/{id}/vibepollo/stop | Stop provider |
| Start Vibepollo | POST | /api/seats/{id}/vibepollo/start | Start provider |
| Get services | GET | /api/seats/{id}/services | Get subsystem status |
| Launch app | POST | /api/seats/{id}/apps | Launch app in seat |
| Reset display | POST | /api/seats/{id}/display/reset | Reset display |
| Reset controller | POST | /api/seats/{id}/controller/reset | Reset controller |
| Set resolution | POST | /api/seats/{id}/resolution | Change resolution |
| Set NVENC preset | POST | /api/seats/{id}/nvenc | Change NVENC preset |
| Get paired clients | GET | /api/seats/{id}/clients | List paired clients |
| Unpair client | DELETE | /api/seats/{id}/clients/{name} | Unpair client |
| Unpair all | DELETE | /api/seats/{id}/clients | Unpair all clients |

### Account Operations

| Operation | Method | Endpoint | Description |
|-----------|--------|----------|-------------|
| Create account | POST | /api/accounts | Create Windows account |
| Delete account | DELETE | /api/accounts/{name} | Delete account |
| List accounts | GET | /api/accounts | List accounts |

### System Operations

| Operation | Method | Endpoint | Description |
|-----------|--------|----------|-------------|
| Health | GET | /api/health | System health |
| Diagnostics | GET | /api/seats/{id}/diagnostics | Seat diagnostics |

---

## API Authentication

### API Key

```
Header: X-API-Key: {key}
```

### Validation

```csharp
if (request.Headers["X-API-Key"] != expectedApiKey)
    return Unauthorized();
```

---

## API Response Models

### SeatResponse

```json
{
  "id": "guid",
  "accountName": "string",
  "status": "Ready",
  "sessionId": 1,
  "vibepolloProcessId": 1234,
  "portBase": 48100,
  "displayDevicePath": "string",
  "width": 1920,
  "height": 1080,
  "fps": 60
}
```

### SeatServices

```json
{
  "vibepollo": true,
  "vibepolloReachable": true,
  "vibepolloStreaming": false,
  "display": true,
  "audio": true,
  "controller": false,
  "firewall": true,
  "session": true
}
```

---

## WebSocket

### Events

| Event | Description |
|-------|-------------|
| seat_update | Seat state changed |

### Payload

```json
{
  "type": "seat_update",
  "data": { ... }
}
```

---

## API Boundary Rules

### Rule 1: API Does Not Contain Business Logic

API endpoints delegate to application layer.

### Rule 2: API Uses DTOs, Not Domain Models

API wire model is separate from domain model.

### Rule 3: Credentials Never Cross API

No passwords, tokens, or secrets in API requests/responses.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| API uses ASP.NET Core | ApiServer.cs | FACT |
| API key middleware | ApiServer.cs | FACT |
| WebSocket broadcasts updates | WebSocketHub | FACT |
| SeatServices endpoint exists | SeatManager.GetSeatServicesAsync | FACT |
