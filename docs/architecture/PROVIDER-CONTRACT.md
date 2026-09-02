# Provider Contract

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define the conceptual contract between MultiSeat-Extended and streaming providers. This is NOT a C# interface — it is the behavioral contract that any provider adapter must fulfill.

---

## Provider Identity

### Metadata

| Property | Type | Description |
|----------|------|-------------|
| Name | string | Provider name (e.g., "Vibepollo", "Apollo") |
| Version | string | Provider version |
| Protocol | string | Streaming protocol (e.g., "Moonlight") |
| Capabilities | set | Supported features (HDR, WebRTC, etc.) |

**Why required**: MultiSeat needs to know provider capabilities for configuration.

---

## Provider Lifecycle Operations

### ValidateConfiguration

| Aspect | Details |
|--------|---------|
| Why required | Ensure provider configuration is valid before starting |
| Who calls it | SeatManager during provisioning |
| What it guarantees | Configuration is syntactically valid |
| Failure behavior | Throw ArgumentException, seat enters Error state |

**FACT**: VibepolloConfigBuilder validates configuration before writing.

---

### Prepare

| Aspect | Details |
|--------|---------|
| Why required | Generate provider configuration files |
| Who calls it | SeatManager during provisioning |
| What it guarantees | Configuration files exist and are valid |
| Failure behavior | Throw, seat enters Error state |

**FACT**: VibepolloConfigBuilder.BuildConfig generates sunshine.conf.

---

### Start

| Aspect | Details |
|--------|---------|
| Why required | Launch provider process |
| Who calls it | SeatManager during provisioning |
| What it guarantees | Provider process is running |
| Failure behavior | Throw, seat enters Error state |

**FACT**: VibepolloManager.StartAsync launches sunshine.exe.

---

### Stop

| Aspect | Details |
|--------|---------|
| Why required | Terminate provider process |
| Who calls it | SeatManager during teardown |
| What it guarantees | Provider process is terminated |
| Failure behavior | Best-effort, force terminate if needed |

**FACT**: VibepolloManager.Stop kills process.

---

### Restart

| Aspect | Details |
|--------|---------|
| Why required | Recover from provider failure |
| Who calls it | Health monitor during recovery |
| What it guarantees | Provider process is running again |
| Failure behavior | Backoff and retry |

**INFERENCE**: Restart = Stop + Start with config rebuild.

---

### QueryHealth

| Aspect | Details |
|--------|---------|
| Why required | Detect provider failures |
| Who calls it | Health monitor every 5 seconds |
| What it guarantees | Returns current health status |
| Failure behavior | Mark as unhealthy, trigger recovery |

**FACT**: VibepolloServerQuery queries HTTP endpoint.

---

### GetLogPath

| Aspect | Details |
|--------|---------|
| Why required | Read provider logs for diagnostics |
| Who calls it | Diagnostics, display detection |
| What it guarantees | Log file path exists |
| Failure behavior | Return null, skip log reading |

**FACT**: VibepolloManager.GetLogPath returns log path.

---

### ParseDisplayId

| Aspect | Details |
|--------|---------|
| Why required | Discover display UUID from provider log |
| Who calls it | Display manager during provisioning |
| What it guarantees | Display UUID if provider created display |
| Failure behavior | Return null, retry later |

**FACT**: VibepolloManager.ParseSudoVdaDisplayId parses log.

---

## Configuration Operations

### GenerateConfig

| Aspect | Details |
|--------|---------|
| Why required | Create provider configuration for seat |
| Who calls it | SeatManager during provisioning |
| What it guarantees | Config file exists with correct values |
| Failure behavior | Throw, seat enters Error state |

**FACT**: VibepolloConfigBuilder.BuildConfig generates sunshine.conf.

---

### UpdateDisplayOutput

| Aspect | Details |
|--------|---------|
| Why required | Point provider to correct display |
| Who calls it | Display manager after UUID discovery |
| What it guarantees | Config targets correct display |
| Failure behavior | Provider captures wrong display |

**FACT**: VibepolloConfigBuilder.UpdateDisplayOutput writes output_name.

---

### CleanupConfig

| Aspect | Details |
|--------|---------|
| Why required | Remove provider configuration on teardown |
| Who calls it | SeatManager during teardown |
| What it guarantees | Config files removed |
| Failure behavior | Best-effort, orphan files possible |

**FACT**: VibepolloConfigBuilder.CleanupConfig removes files.

---

## Client Operations

### GetPairedClients

| Aspect | Details |
|--------|---------|
| Why required | List paired Moonlight clients |
| Who calls it | API for dashboard |
| What it guarantees | List of paired client names |
| Failure behavior | Return empty list |

**FACT**: VibepolloConfigBuilder.GetPairedClients reads pairing state.

---

### UnpairClient

| Aspect | Details |
|--------|---------|
| Why required | Remove specific paired client |
| Who calls it | API for dashboard |
| What it guarantees | Client is unpaired |
| Failure behavior | Return false |

**FACT**: VibepolloConfigBuilder.UnpairClient removes client.

---

## Contract Summary

| Operation | Phase | Required | Failure |
|-----------|-------|----------|---------|
| ValidateConfiguration | Provisioning | Yes | Error |
| Prepare | Provisioning | Yes | Error |
| Start | Provisioning | Yes | Error |
| Stop | Teardown | Yes | Best-effort |
| Restart | Recovery | Yes | Backoff |
| QueryHealth | Monitoring | Yes | Degraded |
| GetLogPath | Diagnostics | No | Skip |
| ParseDisplayId | Provisioning | No | Retry |
| GenerateConfig | Provisioning | Yes | Error |
| UpdateDisplayOutput | Provisioning | Yes | Warning |
| CleanupConfig | Teardown | No | Best-effort |
| GetPairedClients | API | No | Empty |
| UnpairClient | API | No | False |

---

## Provider Adapter Pattern

```
IStreamingProvider (contract)
    │
    ├── VibepolloAdapter (implementation)
    │     └── Delegates to VibepolloManager
    │
    ├── ApolloAdapter (future)
    │     └── Delegates to ApolloManager
    │
    └── SunshineAdapter (future)
          └── Delegates to SunshineManager
```

---

## Recovery Architecture — Current vs Future

### Current State (polling-based)

Provider crash detection and recovery currently uses **polling-based health checks**:

```
SessionHealthCheck (every 5 seconds)
    ↓ polls
IsProcessAlive(seat.ApolloProcessId)
    ↓ detects crash
ApolloManager.RestartAsync(seat, ct)
    ↓ executes
ProcessInjector.LaunchApolloInSessionAsync
```

- Detection: `Process.GetProcessById` + `HasExited` (5-second interval)
- Recovery owner: `SessionHealthCheck` (orchestration layer)
- Restart execution: `ApolloManager.RestartAsync` (handles restart count, MaxRestartAttempts)
- Expected exit handling: Not modeled — `ApolloManager.Stop()` kills directly without signaling "expected" to any monitor
- `IProviderLifecycleConsumer`: **not wired** — no production implementation exists
- `IProcessMonitor.ProcessExited`: **not subscribed** — no production consumer exists

### ProcessTracking Future Boundary (potential future integration)

ProcessTracking defines an **event-driven** recovery path that is currently disconnected from production:

```
IProcessMonitor.ProcessExited (event-driven, immediate)
    ↓
IProviderLifecycleConsumer.HandleProviderExitedAsync (decision point)
    ↓
Expected → ignore
Unexpected → recovery signal → future P1 backoff
```

- Detection: `Process.Exited` event via `WaitForSingleObject` (immediate, no polling)
- Recovery owner: `IProviderLifecycleConsumer` implementor (TBD)
- `WasExpected` / `MarkExpectedExit`: designed to prevent unnecessary crash recovery during intentional stops, but currently not part of the Apollo recovery flow
- `ProcessIdentity` (PID + StartedAt): protects against PID reuse, but currently raw `int` PID is used in `SeatInfo.ApolloProcessId`

### Migration Boundary

The transition from polling-based to event-driven recovery would be a **separate architectural step** if pursued. The current polling recovery should not be modified as part of documentation updates.

Key considerations for potential future migration:
- `SessionHealthCheck` would need to subscribe to `IProcessMonitor.ProcessExited` instead of polling
- An `IProviderLifecycleConsumer` implementor would need to be created (likely in `SessionHealthCheck` or a dedicated recovery service)
- `ApolloManager` would need to register/unregister processes with `IProcessTracker`
- `WasExpected` would need to be set before `ApolloManager.Stop()` kills a process
- The 5-second polling interval would be replaced by immediate event-driven detection

These changes are **not currently planned or committed** in the repository.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| ValidateConfiguration is needed | VibepolloConfigBuilder | FACT |
| Prepare generates sunshine.conf | VibepolloConfigBuilder.BuildConfig | FACT |
| Start launches process | VibepolloManager.StartAsync | FACT |
| Stop kills process | VibepolloManager.Stop | FACT |
| QueryHealth uses HTTP | VibepolloServerQuery | FACT |
| ParseDisplayId reads log | VibepolloManager.ParseSudoVdaDisplayId | FACT |
| GetPairedClients reads state | VibepolloConfigBuilder.GetPairedClients | FACT |
