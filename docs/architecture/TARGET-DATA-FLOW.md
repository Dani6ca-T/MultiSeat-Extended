# Target Data Flow

**Date**: 2026-08-30
**Status**: FROZEN

---

## Provisioning Flow

```
API Request (POST /api/seats)
    │
    ▼
Application Layer
    │
    ▼
SeatManager.ProvisionSeatAsync
    │
    ├── 1. Validate capacity + account
    ├── 2. Allocate port block
    ├── 3. Create Windows account
    ├── 4. Launch RDP session
    ├── 5. Create SudoVDA display
    ├── 6. Open firewall ports
    ├── 7. Start Vibepollo
    ├── 8. Discover display UUID
    ├── 9. Apply display isolation
    ├── 10. Apply HidHide rules
    └── 11. Mark Ready
    │
    ▼
WebSocket Broadcast (seat_update)
    │
    ▼
Dashboard Update
```

---

## Streaming Flow

```
Moonlight Client
    │
    │ Connect to port 48100+1
    ▼
Vibepollo (per seat)
    │
    ├── Capture (DDA/WGC)
    ├── Encode (NVENC/AMF)
    ├── Stream (RTSP/WebRTC)
    │
    ▼
Client receives video/audio/input
```

---

## Recovery Flow

```
Health Check (5s)
    │
    ├── Provider unreachable
    │       │
    │       ▼
    │   Mark Degraded
    │       │
    │       ▼
    │   Apply Backoff (30/60/120s)
    │       │
    │       ▼
    │   Restart Provider
    │       │
    │       ▼
    │   Verify Health
    │       │
    │       ├── Success → Mark Ready
    │       └── Failure → Increment crash count
    │                       │
    │                       └── Max attempts → Mark Failed
    │
    ├── Display lost
    │       │
    │       ▼
    │   TryLateDisplayDetectionAsync
    │       │
    │       ├── Found → Apply isolation
    │       └── Not found → Continue monitoring
    │
    └── Session disconnected
            │
            ▼
        Reconnect mstsc
            │
            ├── Success → Resume monitoring
            └── Failure → Log warning
```

---

## Teardown Flow

```
API Request (DELETE /api/seats/{id})
    │
    ▼
SeatManager.TeardownSeatAsync
    │
    ├── 1. Stop games
    ├── 2. Uninstall input hooks
    ├── 3. Uncloak HidHide
    ├── 4. Destroy controllers
    ├── 5. Stop Vibepollo
    ├── 6. Close Job Object (kill all)
    ├── 7. Close firewall ports
    ├── 8. Destroy display
    ├── 9. Disconnect session
    ├── 10. Logoff session
    ├── 11. Release ports
    ├── 12. Cleanup config
    └── 13. Delete account
    │
    ▼
WebSocket Broadcast (seat_update)
    │
    ▼
Dashboard Update
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Provisioning is 9-step pipeline | SeatManager.cs | FACT |
| Recovery uses health checks | SessionHealthCheck | FACT |
| Teardown is best-effort | TeardownSeatInternalAsync | FACT |
| WebSocket broadcasts updates | WebSocketHub | FACT |
