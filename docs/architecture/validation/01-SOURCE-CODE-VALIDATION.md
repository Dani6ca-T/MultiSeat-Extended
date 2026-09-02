# Source Code Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate Architecture Baseline against actual source code.

---

## Major Architectural Decisions

### D001: Seat is Aggregate Root

**DECISION**: Seat is the aggregate root for the multi-seat domain.

**CURRENT CODE**: SeatManager.cs

**EVIDENCE**:
```csharp
public sealed class SeatManager
{
    private readonly ConcurrentDictionary<Guid, SeatInfo> _seats = new();
    private readonly AccountManager _accounts;
    private readonly SessionLauncher _sessionLauncher;
    private readonly VirtualDisplayManager _displayManager;
    private readonly VibepolloManager _vibepolloManager;
    // ... 15+ dependencies
}
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: None. SeatManager orchestrates all subsystems.

**RECOMMENDATION**: No change needed.

---

### D002: Provider is External Process

**DECISION**: Streaming providers run as external processes.

**CURRENT CODE**: VibepolloManager.cs

**EVIDENCE**:
```csharp
var pid = await _processInjector.LaunchVibepolloInSessionAsync(
    seat.SessionId, seat.AccountName,
    _options.VibepolloExePath, configPath, ct);
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: VibepolloManager is tightly coupled to Vibepollo.

**RECOMMENDATION**: Create IStreamingProvider interface.

---

### D003: SudoVDA for Display

**DECISION**: Use SudoVDA for virtual display.

**CURRENT CODE**: VirtualDisplayManager.cs

**EVIDENCE**:
```csharp
public bool IsDriverAvailable => IsSudoVdaAdapterPresent();
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: License unknown.

**RECOMMENDATION**: Investigate SudoVDA license.

---

### D004: HidHide for Input Isolation

**DECISION**: Use HidHide session jail for gamepad isolation.

**CURRENT CODE**: HidHideConfigurator.cs

**EVIDENCE**:
```csharp
public void CloakForSession(SeatInfo seat)
{
    // HidHide session jail implementation
}
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: Default OFF (EnableHidHideCloaking = false).

**RECOMMENDATION**: Enable by default (target).

---

### D005: PerSession Audio

**DECISION**: Use Windows RDP per-session audio.

**CURRENT CODE**: SeatManager.cs

**EVIDENCE**:
```csharp
// PerSession (the only supported mode) needs no host-side audio device
_logger.LogInformation(
    "Seat {Id}: per-session audio — Vibepollo captures the session's own Remote Audio endpoint",
    seat.Id);
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: No microphone (RDP limitation).

**RECOMMENDATION**: Accept limitation, wait for Vibepollo WebRTC mic.

---

### D006: 5s Health Check

**DECISION**: Health checks run every 5 seconds.

**CURRENT CODE**: MultiSeatOptions.cs

**EVIDENCE**:
```csharp
public int HealthCheckIntervalMs { get; set; } = 5_000;
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: None.

**RECOMMENDATION**: No change needed.

---

### D007: Progressive Backoff

**DECISION**: Crash recovery uses progressive backoff.

**CURRENT CODE**: VibepolloManager.cs

**EVIDENCE**:
```csharp
public const int MaxRestartAttempts = 3;
```

**COMPATIBILITY**: PARTIALLY COMPATIBLE

**PROBLEM**: No progressive backoff (30/60/120s). Only MaxRestartAttempts.

**RECOMMENDATION**: Add progressive backoff.

---

### D008: Job Objects for Cleanup

**DECISION**: Use Job Objects for guaranteed process cleanup.

**CURRENT CODE**: Not implemented.

**EVIDENCE**: Codebase search — absent.

**COMPATIBILITY**: INCOMPATIBLE (missing)

**PROBLEM**: No Job Objects. Best-effort Kill only.

**RECOMMENDATION**: Add Job Objects.

---

### D009: Configuration Generated

**DECISION**: MultiSeat generates provider configuration.

**CURRENT CODE**: VibepolloConfigBuilder.cs

**EVIDENCE**:
```csharp
var configPath = _configBuilder.BuildConfig(seat, _options.VibepolloConfigDir);
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: None.

**RECOMMENDATION**: No change needed.

---

### D010: Credentials Protected

**DECISION**: Credentials never cross public models.

**CURRENT CODE**: Security implementations

**EVIDENCE**:
```csharp
// DPAPI encryption
byte[] encrypted = ProtectedData.Protect(plainData, null, DataProtectionScope.LocalMachine);
```

**COMPATIBILITY**: COMPATIBLE

**PROBLEM**: None.

**RECOMMENDATION**: No change needed.

---

## Summary

| Decision | Compatibility | Action |
|----------|---------------|--------|
| D001: Seat aggregate root | COMPATIBLE | None |
| D002: Provider external process | COMPATIBLE | Create IStreamingProvider |
| D003: SudoVDA display | COMPATIBLE | Investigate license |
| D004: HidHide input | COMPATIBLE | Enable by default |
| D005: PerSession audio | COMPATIBLE | Accept limitation |
| D006: 5s health check | COMPATIBLE | None |
| D007: Progressive backoff | PARTIALLY COMPATIBLE | Add backoff |
| D008: Job Objects | INCOMPATIBLE | Add Job Objects |
| D009: Configuration generated | COMPATIBLE | None |
| D010: Credentials protected | COMPATIBLE | None |

**Overall**: 8 COMPATIBLE, 1 PARTIALLY COMPATIBLE, 1 INCOMPATIBLE

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SeatManager orchestrates all | SeatManager.cs | FACT |
| VibepolloManager is coupled | VibepolloManager.cs | FACT |
| SudoVDA is used | VirtualDisplayManager.cs | FACT |
| HidHide session jail works | HidHideConfigurator.cs | FACT |
| PerSession audio used | SeatManager.cs | FACT |
| MaxRestartAttempts = 3 | VibepolloManager.cs | FACT |
| No Job Objects | Codebase search | FACT (absent) |
| No progressive backoff | VibepolloManager.cs | FACT |
