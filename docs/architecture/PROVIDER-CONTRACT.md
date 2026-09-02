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
