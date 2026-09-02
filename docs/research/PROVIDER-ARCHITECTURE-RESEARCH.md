# MultiSeat-Extended: Исследование архитектур Streaming Providers

## Цель

Понять, каким должен быть универсальный streaming provider abstraction. **Не реализовывать его** — только исследовать.

---

## Исследованные Providers

### 1. Sunshine (Upstream)

**Repository**: LizardByte/Sunshine (GPLv3)

**Architecture**:
- Standalone C++ process
- WebRTC signaling + RTSP streaming
- Web UI on HTTPS port
- sunshine.conf configuration
- sunshine_state.json for UUID/pairing

**Key APIs**:
- REST API: /api/config, /api/apps, /api/clients
- Web UI: browser-based configuration
- Moonlight protocol: pairing, streaming, input

**Multi-instance**: Manual (separate config files, different ports)

**Virtual Display**: External tools (SudoVDA, etc.)

---

### 2. Apollo (ClassicOldSong/Apollo)

**Repository**: ClassicOldSong/Apollo (GPLv3)

**Architecture**: Fork of Sunshine

**Key Additions**:
- Built-in SudoVDA integration
- Per-client fixed identity
- Permission management (role-based)
- Clipboard sync
- Client connection/disconnection hooks
- Headless mode

**Multi-instance**: Supported (copy config, change port/name)

**Key Difference from Sunshine**:
- Automatic virtual display per client
- Client-specific display configuration persistence
- Granular permission system

---

### 3. Vibepollo (Nonary/Vibepollo)

**Repository**: Nonary/Vibepollo (GPLv3)

**Architecture**: Fork of Apollo

**Key Additions**:
- AI-generated architecture (99% AI code)
- Automated display management
- RTSS integration
- Lossless Scaling integration
- NVIDIA Smooth Motion
- WGC service capture
- Display layout restoration
- Headless boot optimization

**Multi-instance**: Via external managers (Helios)

**Key Difference from Apollo**:
- More automated display handling
- Better headless support
- Frame generation integration
- Recovery mechanisms

---

### 4. Helios (MintCapybara924/Helios-Sunshine-Manager)

**Repository**: MintCapybara924/Helios-Sunshine-Manager (GPLv3)

**Architecture**: .NET 8 WPF app + SYSTEM service

**Key Components**:
- Helios.App (WPF UI)
- Helios.Core (business logic)
- Helios.Spawner (SYSTEM service)
- Named Pipe IPC (App ↔ Spawner)

**Multi-instance**: Core feature — manages multiple Sunshine/Apollo/Vibepollo instances

**Key Pattern**:
```
Helios.App (UI, user-level)
    ↓ Named Pipes
Helios.Spawner (SYSTEM service)
    ↓ CreateProcessAsUser
Sunshine/Apollo/Vibepollo instances
```

**Relevance to MultiSeat**:
- Clean separation of UI and privileged operations
- Per-instance config isolation
- Per-instance port allocation
- Per-instance audio routing
- Process lifecycle management

---

### 5. Duo (DuoStream/Duo)

**Repository**: DuoStream/Duo (Proprietary)

**Architecture**: Proprietary stack

**Key Components**:
- TermWrap service (SYSTEM)
- Custom WDDM display driver
- UMDF input driver
- Sunshine per user session
- Application Compatibility Layer

**Multi-instance**: Core feature — unlimited with paid tier

**Key Pattern**:
```
Duo Manager (UI)
    ↓
TermWrap service (SYSTEM)
    ↓
Per-user Sunshine + Virtual display + Input isolation
```

**Relevance to MultiSeat**:
- Study architecture only (closed-source)
- Game mutex isolation approach
- Steam multi-instance approach
- Process compatibility layer

---

## Анализ: Что делает каждый provider

### What ALL providers do

| Capability | Sunshine | Apollo | Vibepollo |
|------------|----------|--------|-----------|
| Video encoding | ✅ | ✅ | ✅ |
| Audio capture | ✅ | ✅ | ✅ |
| Input forwarding | ✅ | ✅ | ✅ |
| Moonlight protocol | ✅ | ✅ | ✅ |
| Web UI | ✅ | ✅ | ✅ |
| Pairing | ✅ | ✅ | ✅ |
| Configuration | ✅ | ✅ | ✅ |

### What Apollo adds over Sunshine

| Capability | Sunshine | Apollo |
|------------|----------|--------|
| Virtual display (SudoVDA) | External | Built-in |
| Per-client identity | No | Yes |
| Permission management | No | Yes |
| Headless mode | Basic | Advanced |
| Client hooks | No | Yes |

### What Vibepollo adds over Apollo

| Capability | Apollo | Vibepollo |
|------------|--------|-----------|
| Automated display management | Basic | Advanced |
| RTSS integration | No | Yes |
| Lossless Scaling | No | Yes |
| Frame generation | No | Yes |
| Display restoration | No | Yes |
| WGC service capture | No | Yes |

### What Helios adds (as manager)

| Capability | Single Instance | Helios Managed |
|------------|----------------|----------------|
| Multi-instance | Manual | Automated |
| Config isolation | Manual | Per-instance |
| Port allocation | Manual | Automated |
| Process lifecycle | Manual | Start/Stop/Restart |
| SYSTEM execution | Manual | Automatic |
| Audio routing | Manual | Per-instance |

---

## Универсальный Provider Abstraction: Анализ

### Текущая ситуация в MultiSeat-Extended

```csharp
// SeatManager напрямую вызывает VibepolloManager
seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);

// VibepolloConfigBuilder генерирует sunshine.conf
var configPath = _configBuilder.BuildConfig(seat, _options.VibepolloConfigDir);

// VibepolloManager парсит Vibepollo лог
var displayId = _vibepolloManager.ParseSudoVdaDisplayId(logPath);
```

**Проблема**: Всё привязано к Vibepollo. Замена на другой provider требует изменения SeatManager.

### Что должен абстрагировать IStreamingProvider

```csharp
public interface IStreamingProvider
{
    // Lifecycle
    Task<int> StartAsync(SeatInfo seat, CancellationToken ct);
    void Stop(SeatInfo seat);
    Task<int> RestartAsync(SeatInfo seat, CancellationToken ct);
    bool IsAlive(Guid seatId);
    
    // Configuration
    string BuildConfig(SeatInfo seat, string configDir);
    void UpdateDisplayOutput(string configPath, string displayId);
    
    // State
    int GetRestartCount(Guid seatId);
    string? GetLogPath(string accountName, string configDir);
    string? GetConfigPath(Guid seatId);
    
    // Display discovery
    string? ParseDisplayId(string logPath);
    
    // Pairing
    IReadOnlyList<string> GetPairedClients(string accountName, string configDir);
    bool UnpairClient(string accountName, string configDir, string clientName);
    
    // Server query
    Task<ServerInfo?> QueryAsync(int port, CancellationToken ct);
}
```

### Что НЕ должно входить в abstraction

- Session management (MultiSeat layer)
- User management (MultiSeat layer)
- Port allocation (MultiSeat layer)
- Display isolation (MultiSeat layer)
- Health checking (MultiSeat layer)
- API/Dashboard (MultiSeat layer)
- Security (MultiSeat layer)

### Provider-specific implementations

```
IStreamingProvider
    ├── VibepolloProvider (sunshine.conf, Vibepollo API)
    ├── ApolloProvider (sunshine.conf, Apollo API)
    ├── SunshineProvider (sunshine.conf, Sunshine API)
    └── FutureProvider (unknown protocol)
```

---

## Ключевые выводы

### 1. The boundary is clear

- **Provider**: Encoding, streaming, display creation, audio capture, input forwarding
- **Orchestrator**: Session lifecycle, user management, port allocation, display isolation, health monitoring

### 2. Provider abstraction is feasible

All Sunshine forks (Apollo, Vibepollo) use the same config format (sunshine.conf) and similar APIs. A common interface could work.

### 3. But caution is needed

- Each fork has unique features (Vibepollo's RTSS, Apollo's permissions)
- Config format may diverge in future
- API endpoints may differ

### 4. Recommended approach

1. Start with VibepolloProvider (current)
2. Extract IStreamingProvider interface
3. Implement ApolloProvider as second provider
4. Evaluate if abstraction is worth the complexity

### 5. The orchestration layer is the real value

Any Sunshine fork can stream. Only MultiSeat provides the multiseat orchestration. The provider abstraction is a means to an end, not the end itself.
