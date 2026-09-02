# Session Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define session management, RDP loopback, and Windows session lifecycle.

---

## Session Model

### Session

| Property | Type | Description |
|----------|------|-------------|
| SessionId | int | Windows session ID |
| SeatId | Guid | Owning seat |
| AccountName | string | Windows account |
| State | SessionState | Current state |
| Geometry | RdpGeometry | Resolution |

**FACT**: SessionLauncher creates RDP loopback sessions.

---

## Session Lifecycle

### States

```
Created → Connecting → Active → Disconnected → Terminating → Terminated
                                        ↓
                                   Reconnected
```

### Transitions

| From | To | Event |
|------|-----|-------|
| Created | Connecting | mstsc launched |
| Connecting | Active | Session active |
| Connecting | Failed | Timeout |
| Active | Disconnected | Network/sleep |
| Disconnected | Active | Reconnect |
| Active | Terminating | Logoff |
| Terminating | Terminated | Logoff complete |

---

## RDP Loopback

### How It Works

```
MultiSeat.Service
    │
    ├── SessionLauncher
    │       │
    │       ├── WTSGetActiveConsoleSessionId()
    │       ├── WTSQueryUserToken(sessionId)
    │       ├── OpenProcessToken(SYSTEM)
    │       ├── DuplicateTokenEx(TokenPrimary)
    │       ├── SetTokenInformation(TokenSessionId)
    │       ├── CreateEnvironmentBlock(userToken)
    │       └── CreateProcessAsUser(mstsc.exe)
    │
    └── mstsc.exe
            │
            └── Connects to 127.0.0.2 (loopback)
                    │
                    └── Creates RDP session
```

**FACT**: SessionLauncher uses CreateProcessAsUser with SYSTEM token.

---

## Session Creation

### Steps

```
1. Allocate port block (30 ports)
2. Create Windows account (if needed)
3. Apply group membership (Users + RDP Users)
4. Launch mstsc.exe via CreateProcessAsUser
   └── SYSTEM token assigned to user session
5. Wait for session activation
6. Return SessionId
```

### Key Details

- **Token**: SYSTEM token (SeTcbPrivilege)
- **Session**: Assigned to user's interactive session
- **Environment**: User's environment block
- **Desktop**: winsta0\default

**FACT**: Helios ProcessLauncher uses this exact pattern.

---

## Session Disconnect/Reconnect

### Disconnect

```
1. Network failure or system sleep
2. Session becomes inactive
3. Provider may fail (QueryDisplayConfig returns ERROR_ACCESS_DENIED)
4. Health check detects disconnect
```

### Reconnect

```
1. mstsc reconnects to same session
2. Session becomes active
3. Provider resumes capture
4. Display isolation may need re-application
```

### Key Point

Session ID is preserved across disconnect/reconnect. Everything running in the session survives.

---

## Session Teardown

### Steps

```
1. DisconnectSession (RDP disconnect)
2. LogoffSession (logoff user)
3. Wait for session termination
4. Release session resources
```

### Best-Effort

Each step is wrapped in try/catch. Failure of one step does not prevent others.

---

## Session Health

### Monitoring

| Check | Method | Interval |
|-------|--------|----------|
| Session active | WTS API | 5s |
| mstsc alive | PID check | 5s |
| RDP connected | Session state | 5s |

### Recovery

| Failure | Recovery |
|---------|----------|
| Session disconnected | Auto-reconnect |
| mstsc crashed | Re-launch mstsc |
| Session terminated | Re-create session |

---

## Session ↔ Seat Mapping

### Mapping

```
Seat ID → Session ID
Seat 0 → Session 1
Seat 1 → Session 2
Seat 2 → Session 3
```

### Storage

Session ID stored in SeatInfo.SessionId.

**FACT**: SeatInfo.SessionId tracks Windows session.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SessionLauncher uses CreateProcessAsUser | SessionLauncher.cs | FACT |
| SYSTEM token assigned to session | Helios ProcessLauncher | FACT |
| mstsc connects to 127.0.0.2 | SessionLauncher | FACT |
| Session ID preserved on reconnect | Windows Terminal Services | FACT |
| Best-effort teardown | TeardownSeatInternalAsync | FACT |
