# State Machines

**Date**: 2026-08-30
**Status**: FROZEN

---

## 1. Seat State Machine

### States

```
Created
Provisioning
Configuring
Ready
Starting
Streaming
Degraded
Recovering
Stopping
Stopped
Failed
TearingDown
Idle
```

### Transitions

```
Created ──→ Provisioning
Provisioning ──→ Configuring
Configuring ──→ Ready
Ready ──→ Starting
Starting ──→ Streaming
Streaming ──→ Ready (client disconnect)
Ready ──→ Degraded (component failure)
Streaming ──→ Degraded (component failure)
Degraded ──→ Recovering
Recovering ──→ Ready (recovery success)
Recovering ──→ Failed (recovery exhausted)
Ready ──→ Stopping
Streaming ──→ Stopping
Degraded ──→ Stopping
Stopping ──→ Stopped
Stopped ──→ Idle
Created/Provisioning/Configuring ──→ Failed (provisioning error)
Failed ──→ TearingDown
Any ──→ TearingDown (teardown requested)
TearingDown ──→ Idle
```

### Events

| Event | From | To | Guard |
|-------|------|-----|-------|
| ProvisionRequested | Created | Provisioning | None |
| SessionCreated | Provisioning | Configuring | SessionId > 0 |
| DisplayCreated | Configuring | Ready | DisplayDevicePath set |
| ProviderStarted | Configuring | Ready | VibepolloProcessId > 0 |
| ClientConnected | Ready | Starting | None |
| StreamingStarted | Starting | Streaming | Provider streaming |
| ClientDisconnected | Streaming | Ready | None |
| ComponentFailed | Ready/Streaming | Degraded | Any component fails |
| RecoveryStarted | Degraded | Recovering | None |
| RecoverySucceeded | Recovering | Ready | All components healthy |
| RecoveryFailed | Recovering | Failed | Backoff exhausted |
| TeardownRequested | Any | TearingDown | None |
| TeardownComplete | TearingDown | Idle | All resources released |
| ProvisionFailed | Provisioning/Configuring | Failed | Exception thrown |

### Recovery Transitions

```
Degraded ──→ Recovering ──→ Ready (success)
Degraded ──→ Recovering ──→ Failed (exhausted)
Failed ──→ TearingDown ──→ Idle (cleanup)
```

---

## 2. ProviderInstance State Machine

### States

```
Created
Starting
Running
Degraded
Stopping
Stopped
Failed
```

### Transitions

```
Created ──→ Starting
Starting ──→ Running (process alive + health OK)
Starting ──→ Failed (process not alive after timeout)
Running ──→ Degraded (health check fails)
Degraded ──→ Running (health check passes)
Degraded ──→ Failed (restart exhausted)
Running ──→ Stopping (stop requested)
Degraded ──→ Stopping (stop requested)
Stopping ──→ Stopped (process terminated)
Running ──→ Failed (process crashed)
Degraded ──→ Failed (process crashed)
```

### Events

| Event | From | To | Guard |
|-------|------|-----|-------|
| StartRequested | Created | Starting | None |
| ProcessAlive | Starting | Running | PID alive + health OK |
| ProcessNotAlive | Starting | Failed | Timeout exceeded |
| HealthCheckFailed | Running | Degraded | HTTP ping fails |
| HealthCheckPassed | Degraded | Running | HTTP ping succeeds |
| RestartExhausted | Degraded | Failed | MaxRestartAttempts reached |
| StopRequested | Running/Degraded | Stopping | None |
| ProcessTerminated | Stopping | Stopped | Process exited |
| ProcessCrashed | Running/Degraded | Failed | Process exited unexpectedly |

---

## 3. Session State Machine

### States

```
Created
Connecting
Active
Disconnected
Terminating
Terminated
Failed
```

### Transitions

```
Created ──→ Connecting
Connecting ──→ Active (mstsc connected)
Connecting ──→ Failed (connection timeout)
Active ──→ Disconnected (network/sleep)
Disconnected ──→ Active (reconnect)
Active ──→ Terminating (logoff requested)
Disconnected ──→ Terminating (logoff requested)
Terminating ──→ Terminated (logoff complete)
```

### Events

| Event | From | To | Guard |
|-------|------|-----|-------|
| RdpLoopbackStarted | Created | Connecting | mstsc launched |
| RdpConnected | Connecting | Active | Session active |
| ConnectionTimeout | Connecting | Failed | Timeout exceeded |
| NetworkLost | Active | Disconnected | Network failure |
| SleepDetected | Active | Disconnected | System sleep |
| ReconnectSucceeded | Disconnected | Active | mstsc reconnected |
| LogoffRequested | Active/Disconnected | Terminating | None |
| LogoffComplete | Terminating | Terminated | Session ended |

---

## 4. GameProcess State Machine

### States

```
Starting
Running
Exited
Crashed
Unknown
```

### Transitions

```
Starting ──→ Running (process alive)
Starting ──→ Unknown (PID not found)
Running ──→ Exited (process exit code 0)
Running ──→ Crashed (process exit code != 0)
Running ──→ Unknown (process not found)
Unknown ──→ Running (process rediscovered)
```

### Events

| Event | From | To | Guard |
|-------|------|-----|-------|
| ProcessCreated | Starting | Running | PID alive |
| ProcessExitedCleanly | Running | Exited | Exit code 0 |
| ProcessCrashed | Running | Crashed | Exit code != 0 |
| ProcessNotFound | Starting/Running | Unknown | PID not found |
| ProcessRediscovered | Unknown | Running | PID found via WMI |

---

## 5. Display State Machine

### States

```
NotCreated
Creating
Created
Assigned
Isolated
Destroyed
Failed
```

### Transitions

```
NotCreated ──→ Creating
Creating ──→ Created (SudoVDA IPC success)
Creating ──→ Failed (SudoVDA IPC failure)
Created ──→ Assigned (UUID written to config)
Assigned ──→ Isolated (primary + shrunk applied)
Isolated ──→ Destroyed (teardown)
Created ──→ Destroyed (teardown)
Assigned ──→ Destroyed (teardown)
```

### Events

| Event | From | To | Guard |
|-------|------|-----|-------|
| CreateRequested | NotCreated | Creating | None |
| SudoVdaIpcSuccess | Creating | Created | Display created |
| SudoVdaIpcFailure | Creating | Failed | IPC error |
| UuidDiscovered | Created | Assigned | UUID from log |
| IsolationApplied | Assigned | Isolated | Primary + shrunk |
| DestroyRequested | Any | Destroyed | Teardown |

---

## State Machine Diagrams

### Seat Lifecycle

```
                    ┌─────────┐
                    │ Created │
                    └────┬────┘
                         │
                         ▼
                    ┌─────────────┐
                    │ Provisioning│
                    └──────┬──────┘
                           │
                           ▼
                    ┌────────────┐
                    │ Configuring│
                    └──────┬─────┘
                           │
                           ▼
                    ┌─────────┐
              ┌────→│  Ready  │←────┐
              │     └────┬────┘     │
              │          │          │
              │          ▼          │
              │     ┌─────────┐    │
              │     │Starting │    │
              │     └────┬────┘    │
              │          │         │
              │          ▼         │
              │     ┌──────────┐  │
              │     │Streaming │  │
              │     └────┬─────┘  │
              │          │        │
              │          ▼        │
              │     ┌──────────┐ │
              └─────│ Degraded │ │
                    └────┬─────┘ │
                         │       │
                         ▼       │
                    ┌──────────┐│
                    │Recovering││
                    └────┬─────┘│
                         │      │
                         ▼      │
                    ┌────────┐ │
                    │ Failed │ │
                    └────┬───┘ │
                         │     │
                         ▼     │
                    ┌──────────┐
                    │TearingDown│
                    └────┬─────┘
                         │
                         ▼
                    ┌─────────┐
                    │  Idle   │
                    └─────────┘
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatStatus enum exists | SeatStatus.cs | FACT |
| Current states: Idle/Provisioning/Configuring/Ready/Streaming/Error/Idle/TearingDown | SeatInfo model | FACT |
| Provider has process lifecycle | VibepolloManager | FACT |
| Session has connect/disconnect | SessionLauncher | FACT |
| Game has process lifecycle | ProcessInjector | FACT |
| Display has create/destroy | VirtualDisplayManager | FACT |
