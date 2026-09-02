# MultiSeat-Extended: Архитектура стриминга

## Обзор

Streaming provider — Vibepollo (форк Sunshine от ClassicOldSong). Каждый seat получает собственный экземпляр Vibepollo с изолированной конфигурацией, портами и дисплеем.

## Vibepollo Integration Points

### Где запускается provider

```
SeatManager.ProvisionSeatAsync()
    → VibepolloManager.StartAsync(seat)
        → VibepolloConfigBuilder.BuildConfig(seat) → sunshine.conf
        → ProcessInjector.LaunchVibepolloInSessionAsync()
            → CreateProcessAsUserW (в seat session)
```

### Под каким user запускается

- **Seat account** — не SYSTEM, не console user
- Запускается внутри seat's Windows session через `CreateProcessAsUser`
- Token: `WTSQueryUserToken(sessionId)` → `DuplicateTokenEx` → `CreateProcessAsUserW`
- Desktop: `WinSta0\Default`

### Как формируется configuration

`VibepolloConfigBuilder.BuildConfig()` генерирует `sunshine.conf`:

```ini
# Server identity
sunshine_name = MultiSeat-{AccountName}-{SeatNumber}

# Network — unique port range
port = {PortBase + OffsetGfeHttp}  # = PortBase + 0

# Display
output_name = {SudoVDA UUID}  # Set after discovery
resolutions = [{Width}x{Height}]
fps = [{30, 60, 90, 120, 144, 160, 240}]  # up to seat.Fps

# Display device auto-config
dd_configuration_option = ensure_active
dd_resolution_option = auto
dd_refresh_rate_option = auto

# Headless mode
headless_mode = enabled

# Encoder
encoder = nvenc
nvenc_preset = {1-7}
nvenc_twopass = enabled/disabled
nvenc_spatial_aq = enabled/disabled
nvenc_latency_over_power = enabled

# Audio — PerSession (NO sink named)
keep_sink_default = disabled
auto_capture_sink = disabled
stream_mic = disabled

# Input
controller = enabled
gamepad = auto
keybindings_enabled = enabled
mouse = enabled
keyboard = enabled

# Security
file_state = {seatDir}/config/sunshine_state.json
credentials_file = {configDir}/shared_credentials.json
origin_web_ui_allowed = lan

# Streaming quality
min_threads = 2
hevc_mode = 2
av1_mode = 2
fec_percentage = 20

# Color
color_space = 1  # Rec. 709
color_range = 1  # Full range
```

### Где хранится configuration

```
C:\ProgramData\MultiSeat\vibepollo\
├── shared_credentials.json          # Общий файл credentials
├── {AccountName}/
│   ├── sunshine.conf                # Per-seat config
│   ├── vibepollo.log                # Requested (may be ignored)
│   └── config/
│       ├── sunshine_state.json      # UUID, pairings
│       └── apps.json                # App list
└── {AccountName2}/
    └── ...
```

### Как выбирается port

```
PortAllocator.Allocate() → bitmap O(1)
    PortBase = 48100 + (seat_index × 30)

Seat 0 → 48100-48129
Seat 1 → 48130-48159
...

Vibepollo offsets:
  -5  GFE HTTPS (Moonlight pairing)
   0  GFE HTTP (config 'port' key)
   1  Web UI HTTPS
   9  Video RTP
  10  Control ENet
  11  Audio RTP
  12  Mic RTP
  26  RTSP
```

### Как определяется provider state

```csharp
// VibepolloManager.IsAlive(seatId)
if (ProcessId <= 0) return false;
using var proc = Process.GetProcessById(ProcessId);
return !proc.HasExited;

// SeatManager.GetSeatServicesAsync() — async проверка
var vibepolloAlive = seat.VibepolloProcessId > 0 && _vibepolloManager.IsAlive(seatId);
if (vibepolloAlive && seat.PortBase > 0)
    server = await _serverQuery.QueryAsync(
        seat.PortBase + Constants.OffsetGfeHttp, ct);
// VibepolloServerQuery — HTTP GET /api/config
```

### Как обрабатывается crash

```csharp
// SessionHealthCheck.CheckSeatAsync()
if (!vibepolloAlive && seat.VibepolloProcessId > 0)
{
    var newPid = await _vibepolloManager.RestartAsync(seat, ct);
    if (newPid > 0)
    {
        seat.VibepolloProcessId = newPid;
        await _seatManager.ApplyDisplayIsolationAsync(seat, ct);
    }
    else
    {
        seat.Status = SeatStatus.Error;
        seat.ErrorMessage = "Vibepollo streaming server crashed and could not be restarted";
    }
}
```

### Restart attempts

```csharp
public const int MaxRestartAttempts = 3;

// VibepolloManager.RestartAsync()
if (prev.RestartCount >= MaxRestartAttempts)
{
    _logger.LogError("Seat {Id}: Vibepollo has crashed {Count} times — giving up.");
    return -1;
}
// Re-launch in same session with same config
var pid = await _processInjector.LaunchVibepolloInSessionAsync(
    seat.SessionId, seat.AccountName,
    _options.VibepolloExePath, prev.ConfigPath, ct);
```

### Как provider останавливается

```csharp
// VibepolloManager.Stop(seat)
_instances.TryRemove(seat.Id, out _);
var proc = Process.GetProcessById(seat.VibepolloProcessId);
proc.Kill(entireProcessTree: true);  // Vibepollo spawns child encoders
proc.WaitForExit(5000);
```

### Как читаются logs

```csharp
// VibepolloManager.GetLogPath(accountName, configDir)
// Resolve by inspection, not by assumption:
// 1. Check seatDir for vibepollo*.log (non-empty)
// 2. Check seatDir/logs/ for vibepollo*.log (non-empty)
// 3. Return newest by LastWriteTimeUtc

// VibepolloLogParser.ParseLastRequestedMode(logText)
// Regex: "requested to \[(\d+)x(\d+)(?: x (\d+))?\]"
```

### API integration

- Vibepollo web UI: `https://localhost:{PortBase+1}`
- Config API: HTTP GET `http://127.0.0.1:{PortBase}`/api/config
- Pairing: managed via `sunshine_state.json`

### Authentication/pairing management

```csharp
// VibepolloConfigBuilder — per-seat state file
var statePath = Path.Combine(seatDir, "config", "sunshine_state.json");
var credPath = Path.Combine(configDir, "shared_credentials.json");

// Shared credentials across seats
EnsureSharedCredentials(configDir);

// Seat-specific UUID (each seat appears as separate server in Moonlight)
EnsureSeatStateFile(seatDir);

// Pairing management
GetPairedClients(seat.AccountName, configDir);
UnpairClient(seat.AccountName, configDir, clientName);
UnpairAllClients(seat.AccountName, configDir);
```

## Apollo/Vibepollo-Specific Coupling

### Прямая зависимость от Vibepollo

1. **VibepolloConfigBuilder** — генерирует `sunshine.conf` (формат Vibepollo)
   - Все ключи конфигурации специфичны для Vibepollo/Sunshine
   - `dd_configuration_option`, `dd_resolution_option`, `dd_refresh_rate_option`
   - `headless_mode`, `nvenc_*`, `encoder`, `keep_sink_default`, `auto_capture_sink`
   - `stream_mic`, `controller`, `gamepad`, `keybindings_enabled`

2. **VibepolloLogParser** — парсит Vibepollo логи
   - Regex для `requested to [WxH]`
   - Parsing `Currently available display devices:` JSON block

3. **VibepolloManager** — lifecycle management
   - `ParseSudoVdaDisplayId()` — парсит Vibepollo log для SudoVDA UUID
   - `ResolveLogPath()` — определяет реальный путь к логу Vibepollo
   - `GetWebUiUrl()` — Vibepollo web UI port calculation

4. **Port offsets** — специфичны для Vibepollo
   - `OffsetGfeHttps = -5` (GFE HTTPS)
   - `OffsetVideo = 9`, `OffsetControl = 10`, `OffsetAudio = 11`
   - `OffsetRtsp = 26`

5. **Config file format** — `sunshine.conf` key=value
   - Не стандартный, специфичный для Sunshine/Vibepollo

6. **State file** — `sunshine_state.json`
   - UUID management, pairing state

7. **Credentials file** — `shared_credentials.json`
   - Vibepollo web UI login

8. **Headless mode trigger** — `headless_mode = enabled`
   - Без этого Vibepollo не создаёт SudoVDA в RDP сессии

9. **Display device auto-config** — `dd_*` keys
   - Специфичные для Vibepollo ключи настройки дисплея

10. **Process identification** — executable path, config path
    - `GetManagedVibepolloPids()` определяет MultiSeat-managed экземпляры

### Что привязано к Vibepollo

- Конфигурационный формат (sunshine.conf)
- Портовые оффсеты (map_port)
- Лог формат и парсинг
- UUID/state файл формат
- Дисплей discovery через лог
- Headless mode триггер
- Display device auto-config ключи

### Что можно абстрагировать

- Streaming provider interface ( Start/Stop/Restart/IsAlive )
- Configuration builder interface
- Log parser interface
- Display discovery interface
- Port allocation (общий для любых providers)
