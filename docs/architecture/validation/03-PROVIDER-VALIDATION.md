# Provider Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate provider architecture against actual source code.

---

## 1. Vibepollo Coupling Points

### VibepolloManager.cs

**Coupling**:
- Vibepollo-specific log parsing (ParseSudoVdaDisplayId)
- Vibepollo-specific config path (GetConfigPath)
- Vibepollo-specific executable (VibepolloExePath)
- Vibepollo-specific health check (HTTP ping)
- Vibepollo-specific restart logic

**VERDICT**: Tightly coupled to Vibepollo.

---

### VibepolloConfigBuilder.cs

**Coupling**:
- Generates sunshine.conf (Vibepollo format)
- Parses Vibepollo log format
- Manages Vibepollo state files

**VERDICT**: Tightly coupled to Vibepollo.

---

## 2. Classes That Know About Vibepollo

| Class | Knowledge |
|-------|-----------|
| SeatManager | VibepolloManager (injected) |
| VibepolloManager | Vibepollo process, config, logs |
| VibepolloConfigBuilder | sunshine.conf format |
| Monitoring.VibepolloServerQuery | Vibepollo HTTP API |
| ProcessInjector | LaunchVibepolloInSessionAsync |

**VERDICT**: 5 classes know about Vibepollo.

---

## 3. Vibepollo-Specific Configuration

### sunshine.conf

```ini
sunshine_name = "MultiSeat-Seat-{SeatId}"
port = {PortBase}
output_name = {DisplayUuid}
encoder = {NvencPreset}
audio_sink = default
apps = {AppsJsonPath}
```

**VERDICT**: Configuration is Vibepollo-specific.

---

### sunshine_state.json

```json
{
  "uuid": "unique-per-instance",
  "cert": "...",
  "key": "...",
  "paired_clients": []
}
```

**VERDICT**: State is Vibepollo-specific.

---

## 4. How Vibepollo is Launched

**Evidence**: VibepolloManager.cs
```csharp
var pid = await _processInjector.LaunchVibepolloInSessionAsync(
    seat.SessionId, seat.AccountName,
    _options.VibepolloExePath, configPath, ct);
```

**VERDICT**: Launched via ProcessInjector with seat's session and config.

---

## 5. Can We Have 2+ Instances?

**Evidence**: VibepolloManager.cs
```csharp
private readonly ConcurrentDictionary<Guid, VibepolloInstance> _instances = new();
```

**Analysis**: Each seat gets its own VibepolloInstance. Multiple seats = multiple instances.

**VERDICT**: Yes, multi-instance supported via separate sessions.

---

## 6. How Port is Determined

**Evidence**: SeatManager.cs
```csharp
seat.PortBase = _portAllocator.Allocate();
```

**Analysis**: Port allocated by PortAllocator (30-port blocks).

**VERDICT**: Port is allocated per seat.

---

## 7. How Display is Determined

**Evidence**: SeatManager.cs
```csharp
var displayId = _vibepolloManager.ParseSudoVdaDisplayId(logPath);
seat.DisplayDevicePath = displayId;
_configBuilder.UpdateDisplayOutput(configPath, displayId);
```

**Analysis**: Display UUID discovered from Vibepollo log, written to config.

**VERDICT**: Display is discovered and assigned.

---

## 8. How Session is Determined

**Evidence**: SeatManager.cs
```csharp
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
    seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));
```

**Analysis**: Session created via RDP loopback.

**VERDICT**: Session is created per seat.

---

## 9. How Process is Determined

**Evidence**: VibepolloManager.cs
```csharp
var pid = await _processInjector.LaunchVibepolloInSessionAsync(
    seat.SessionId, seat.AccountName,
    _options.VibepolloExePath, configPath, ct);
```

**Analysis**: Process launched via ProcessInjector.

**VERDICT**: Process is tracked by PID.

---

## 10. How Crash is Detected

**Evidence**: VibepolloManager.cs
```csharp
public bool IsAlive(Guid seatId)
{
    if (!_instances.TryGetValue(seatId, out var instance))
        return false;
    return instance.IsAlive;
}
```

**Analysis**: Health check via process alive + HTTP ping.

**VERDICT**: Crash detected by health check.

---

## 11. Can Provider be Replaced by Apollo?

**Analysis**: Apollo uses same sunshine.conf format. Could replace Vibepollo with Apollo.

**VERDICT**: Yes, but requires adapter implementation.

---

## Minimum Provider Boundary

```
IStreamingProvider
├── Start(seat) → PID
├── Stop(seat)
├── Restart(seat) → PID
├── IsAlive(seatId) → bool
├── GetRestartCount(seatId) → int
├── GenerateConfig(seat) → configPath
├── GetConfigPath(seatId) → configPath
├── UpdateDisplayOutput(configPath, displayId)
├── CleanupConfig(accountName, configDir)
├── GetLogPath(accountName, configDir) → logPath
├── ParseDisplayId(logPath) → displayId
├── QueryHealth(port) → HealthStatus
├── GetPairedClients(accountName, configDir) → List
├── UnpairClient(accountName, configDir, clientName) → bool
└── UnpairAllClients(accountName, configDir)
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| VibepolloManager is coupled | VibepolloManager.cs | FACT |
| VibepolloConfigBuilder is coupled | VibepolloConfigBuilder.cs | FACT |
| 5 classes know about Vibepollo | Codebase analysis | FACT |
| Configuration is Vibepollo-specific | sunshine.conf format | FACT |
| Multi-instance supported | ConcurrentDictionary | FACT |
| Port allocated per seat | PortAllocator | FACT |
| Display discovered from log | ParseSudoVdaDisplayId | FACT |
| Crash detected by health check | IsAlive + HTTP ping | FACT |
