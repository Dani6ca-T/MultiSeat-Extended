# Streaming Boundary

**Date**: 2026-08-30
**Purpose**: Define what belongs to the streaming provider vs MultiSeat-Extended

---

## Streaming Provider Owns

These capabilities should remain the responsibility of Vibepollo (or any future provider):

| Capability | Owner | Evidence |
|------------|-------|----------|
| Video encoding | Provider | NVENC, AMF, FFmpeg |
| Audio encoding | Provider | Opus, AAC |
| Video capture | Provider | DDA, WGC, DXGI |
| Audio capture | Provider | WASAPI loopback |
| Network streaming | Provider | RTSP, WebRTC, Moonlight protocol |
| Client pairing | Provider | Certificate exchange, PIN |
| Client protocol | Provider | Moonlight GFE protocol |
| Input injection | Provider | SendInput, ViGEm |
| Gamepad forwarding | Provider | Moonlight controller packets |
| Web UI (provider config) | Provider | sunshine.conf editing |
| Encoder probing | Provider | GPU capability detection |
| Display enumeration | Provider | Display UUID discovery |
| Virtual display creation | Provider | SudoVDA IPC |
| HDR metadata | Provider | HDR10 metadata handling |
| Session streaming state | Provider | Streaming flag, client count |

---

## MultiSeat-Extended Owns

These capabilities should remain the responsibility of MultiSeat-Extended:

| Capability | Owner | Evidence |
|------------|-------|----------|
| Seat lifecycle | MultiSeat | SeatManager.ProvisionSeatAsync |
| User management | MultiSeat | AccountManager |
| Session creation | MultiSeat | SessionLauncher (RDP loopback) |
| Display assignment | MultiSeat | UUID → output_name config |
| Display isolation | MultiSeat | --setup-display-isolation |
| Audio isolation | MultiSeat | PerSession mode selection |
| Input isolation | MultiSeat | HidHide session jail |
| Port allocation | MultiSeat | PortAllocator |
| Provider process lifecycle | MultiSeat | VibepolloManager.Start/Stop |
| Provider configuration | MultiSeat | VibepolloConfigBuilder |
| Provider health monitoring | MultiSeat | VibepolloServerQuery |
| Firewall rules | MultiSeat | FirewallManager |
| Credential storage | MultiSeat | DPAPI |
| API/Dashboard | MultiSeat | ASP.NET Core + React |
| Crash recovery | MultiSeat | SessionHealthCheck → restart |
| Game launch | MultiSeat | ProcessInjector |
| Game library sharing | MultiSeat | SharedGameLibrary (icacls) |
| Emulator netplay | MultiSeat | RetroArch port assignment |

---

## Boundary Rules

### 1. Provider Interface Contract

```
MultiSeat                          Provider
    │                                  │
    │  1. Generate sunshine.conf       │
    │─────────────────────────────────→│
    │                                  │
    │  2. Start process                │
    │─────────────────────────────────→│
    │                                  │
    │  3. Query health (HTTP)          │
    │←─────────────────────────────────│
    │                                  │
    │  4. Stop process                 │
    │─────────────────────────────────→│
    │                                  │
```

### 2. Configuration Boundary

**MultiSeat generates**:
- sunshine.conf (port, output_name, encoder, resolution, fps, apps)
- apps.json (game definitions)

**Provider reads**:
- sunshine.conf
- apps.json
- sunshine_state.json (pairing, certificates)

**Provider writes**:
- sunshine_state.json
- Log files
- apps.json (if modified via Web UI)

### 3. Display Boundary

**MultiSeat manages**:
- SudoVDA display creation/destruction
- Display UUID discovery (from Vibepollo log)
- Display isolation (primary + shrunk)
- Refresh rate clamping

**Provider manages**:
- Display enumeration at startup
- Display capture targeting
- Display mode selection
- HDR metadata

### 4. Audio Boundary

**MultiSeat manages**:
- PerSession audio mode selection
- RustDesk audio suppression
- VAC elimination

**Provider manages**:
- WASAPI loopback capture
- Audio encoding
- Audio streaming

### 5. Input Boundary

**MultiSeat manages**:
- HidHide session jail (gamepad isolation)
- ViGEm controller creation (optional, legacy)
- Controller routing (optional, legacy)

**Provider manages**:
- Moonlight input reception
- SendInput injection
- Gamepad forwarding
- Keyboard/mouse injection

---

## Anti-Patterns to Avoid

### 1. Provider-Specific Logic in Core

❌ **BAD**: `if (provider is Vibepollo) { ... }`

✅ **GOOD**: `provider.Configure(seatConfig)`

### 2. Provider Configuration Leaking

❌ **BAD**: MultiSeat directly writes Vibepollo-specific config keys

✅ **GOOD**: MultiSeat generates a config object, provider adapter translates

### 3. Provider Process Management Coupling

❌ **BAD**: VibepolloManager knows about Vibepollo's log format

✅ **GOOD**: Provider adapter handles log parsing

### 4. Provider Health Check Coupling

❌ **BAD**: VibepolloServerQuery queries Vibepollo's specific HTTP endpoint

✅ **GOOD**: Provider adapter implements IHealthCheck

---

## Provider Abstraction Proposal

```csharp
public interface IStreamingProvider
{
    string Name { get; }
    
    // Lifecycle
    Task<int> StartAsync(SeatInfo seat, CancellationToken ct);
    void Stop(SeatInfo seat);
    bool IsAlive(Guid seatId);
    int GetRestartCount(Guid seatId);
    
    // Configuration
    void GenerateConfig(SeatInfo seat, string configDir);
    void UpdateDisplayOutput(string configPath, string displayId);
    void CleanupConfig(string accountName, string configDir);
    
    // Health
    Task<ProviderHealth> QueryHealthAsync(int port, CancellationToken ct);
    
    // Display
    string? ParseDisplayId(string logPath);
    string GetLogPath(string accountName, string configDir);
    string? GetConfigPath(Guid seatId);
}
```

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| VibepolloManager is tightly coupled | VibepolloManager.cs (Vibepollo-specific log parsing) | VERIFIED |
| VibepolloConfigBuilder generates sunshine.conf | VibepolloConfigBuilder.cs | VERIFIED |
| Provider health is HTTP-based | VibepolloServerQuery | VERIFIED |
| Display UUID comes from provider log | SeatManager.TryLateDisplayDetectionAsync | VERIFIED |
| No provider abstraction exists | Codebase search for IStreamingProvider | VERIFIED (absent) |
| Helios supports multiple providers | Helios README + SpawnerWorker | VERIFIED |
