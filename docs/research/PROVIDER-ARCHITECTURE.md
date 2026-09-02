# Provider Architecture

**Date**: 2026-08-30
**Purpose**: Define streaming provider abstraction for MultiSeat-Extended

---

## Current State: VibepolloManager (Tightly Coupled)

**Source**: VibepolloManager.cs

**Coupling points**:
1. Vibepollo-specific log parsing (ParseSudoVdaDisplayId)
2. Vibepollo-specific config path (GetConfigPath)
3. Vibepollo-specific executable (sunshine.exe)
4. Vibepollo-specific health check (HTTP ping to GFE port)
5. Vibepollo-specific restart logic

**Problem**: Cannot switch to Apollo or any other Sunshine fork without code changes.

---

## Target: IStreamingProvider Interface

```csharp
public interface IStreamingProvider
{
    // Identity
    string Name { get; }
    string ExecutablePath { get; }
    
    // Lifecycle
    Task<int> StartAsync(SeatInfo seat, CancellationToken ct);
    void Stop(SeatInfo seat);
    bool IsAlive(Guid seatId);
    int GetRestartCount(Guid seatId);
    
    // Configuration
    void GenerateConfig(SeatInfo seat, string configDir);
    string? GetConfigPath(Guid seatId);
    void UpdateDisplayOutput(string configPath, string displayId);
    void CleanupConfig(string accountName, string configDir);
    
    // Health
    Task<ProviderHealth> QueryHealthAsync(int port, CancellationToken ct);
    
    // Display
    string? ParseDisplayId(string logPath);
    string GetLogPath(string accountName, string configDir);
    
    // Clients
    List<string> GetPairedClients(string accountName, string configDir);
    bool UnpairClient(string accountName, string configDir, string clientName);
    void UnpairAllClients(string accountName, string configDir);
}

public class ProviderHealth
{
    public bool IsReachable { get; set; }
    public bool IsStreaming { get; set; }
    public string? Version { get; set; }
}
```

---

## Adapter Implementations

### VibepolloAdapter (Current)

```csharp
public class VibepolloAdapter : IStreamingProvider
{
    public string Name => "Vibepollo";
    public string ExecutablePath => _options.VibepolloExePath;
    
    // Delegates to existing VibepolloManager logic
    // Parses Vibepollo-specific log format
    // Uses sunshine.conf format
}
```

### ApolloAdapter (Future)

```csharp
public class ApolloAdapter : IStreamingProvider
{
    public string Name => "Apollo";
    public string ExecutablePath => _options.ApolloExePath;
    
    // Similar to Vibepollo (same sunshine.conf format)
    // Different log format
    // Different health check endpoint
}
```

### SunshineAdapter (Future)

```csharp
public class SunshineAdapter : IStreamingProvider
{
    public string Name => "Sunshine";
    public string ExecutablePath => _options.SunshineExePath;
    
    // Base Sunshine (upstream)
    // Same protocol, different features
}
```

---

## Provider Lifecycle

### Start

```
1. GenerateConfig(seat, configDir)
2. Start process (CreateProcessAsUser)
3. Wait for initialization
4. ParseDisplayId(logPath)
5. UpdateDisplayOutput(configPath, displayId)
6. Restart with display target
7. Verify health
```

### Stop

```
1. Graceful shutdown (close message)
2. Wait for timeout
3. Force terminate if needed
4. Cleanup config (optional)
```

### Restart

```
1. Stop
2. Wait for cleanup
3. Start
4. Re-apply display isolation
```

---

## Provider Discovery

### Configuration

```json
{
  "MultiSeat": {
    "StreamingProvider": "Vibepollo",
    "Providers": {
      "Vibepollo": {
        "ExecutablePath": "C:\\Program Files\\Vibepollo\\sunshine.exe",
        "ConfigDir": "C:\\ProgramData\\Vibepollo"
      },
      "Apollo": {
        "ExecutablePath": "C:\\Program Files\\Apollo\\sunshine.exe",
        "ConfigDir": "C:\\ProgramData\\Apollo"
      }
    }
  }
}
```

### Runtime Selection

```csharp
var provider = providerName switch {
    "Vibepollo" => new VibepolloAdapter(options),
    "Apollo" => new ApolloAdapter(options),
    "Sunshine" => new SunshineAdapter(options),
    _ => throw new ArgumentException($"Unknown provider: {providerName}")
};
```

---

## Migration Path

### Phase 1: Extract Interface

1. Create IStreamingProvider interface
2. Create VibepolloAdapter implementing interface
3. Replace VibepolloManager references with IStreamingProvider
4. Verify all tests pass

### Phase 2: Add Apollo Support

1. Create ApolloAdapter
2. Add configuration for Apollo
3. Test Apollo as provider
4. Verify feature parity

### Phase 3: Provider Switching

1. Add runtime provider selection
2. Add provider health monitoring
3. Add provider failover (optional)

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| VibepolloManager is tightly coupled | VibepolloManager.cs (Vibepollo-specific log parsing) | VERIFIED |
| sunshine.conf format is shared | Apollo and Vibepollo both use it | VERIFIED |
| Helios supports multiple providers | Helios README | VERIFIED |
| No IStreamingProvider exists | Codebase search | VERIFIED (absent) |
| Provider health is HTTP-based | VibepolloServerQuery | VERIFIED |
