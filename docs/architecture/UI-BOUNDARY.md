# UI Boundary

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define dashboard responsibilities and boundaries.

---

## Dashboard Components

### Seat List

- List all seats
- Show seat status
- Quick actions (start/stop/restart)

### Seat Details

- Seat configuration
- Subsystem status (Vibepollo, Display, Audio, Controller, Firewall, Session)
- Launch app
- Reset display/controller
- Change resolution/NVENC
- Paired clients

### Provider Status

- Vibepollo process status
- Vibepollo health
- Restart count
- Logs

### Health

- System health
- Seat health
- Provider health

### Logs

- Real-time log viewer
- Log filtering

### Diagnostics

- HidHide inspector
- Log filter inspector
- Advanced color check

---

## UI Rules

### Rule 1: No Business Logic

UI contains only presentation logic. All business logic in application layer.

### Rule 2: API Consumption

UI communicates with backend via REST API + WebSocket.

### Rule 3: No Credentials

UI does not store or display credentials.

---

## UI Technology

| Component | Technology |
|-----------|------------|
| Framework | React |
| Build | Vite |
| Styling | CSS modules |
| State | React hooks |

**FACT**: Dashboard uses React.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Dashboard uses React | MultiSeat.Dashboard | FACT |
| UI communicates via API | ApiServer.cs | FACT |
| WebSocket for real-time updates | WebSocketHub | FACT |
