# MultiSeat-Extended: Текущая архитектура

## Обзор

MultiSeat-Extended — это Windows multiseat платформа, позволяющая запускать несколько одновременных Moonlight game-streaming сессий на одном хосте. Каждый seat получает изолированный Windows-аккаунт, виртуальный дисплей (SudoVDA), виртуальное аудиоустройство и выделенный экземпляр Vibepollo streaming-сервера.

## Stack

| Компонент | Технология |
|-----------|-----------|
| Backend | .NET 9 / ASP.NET Core Windows Service |
| Frontend | React + TypeScript (Vite) |
| Shared | MultiSeat.Shared — константы, модели |
| Tests | xUnit + Moq |
| InputHook DLL | C++ / CMake |
| Streaming | Vibepollo (форк Sunshine) |
| Virtual Display | SudoVDA (IddCx драйвер) |
| Virtual Controller | ViGEmBus + Nefarius.ViGEm.Client |
| Gamepad Isolation | HidHide (session jail) |
| RDP | TermWrap (fork of RDPWrap) |

## Solution Structure

```
src/
├── MultiSeat.slnx                    # Solution file
├── MultiSeat.Service/                 # Основной backend (.NET 9 Windows Service)
│   ├── Program.cs                     # Entry point + helper modes
│   ├── MultiSeatWorker.cs             # Background service (главный оркестратор)
│   ├── appsettings.json              # Конфигурация по умолчанию
│   ├── Accounts/                      # Управление Windows аккаунтами
│   ├── Api/                           # ASP.NET Core Minimal API
│   ├── Configuration/                 # MultiSeatOptions, SeatPresetStore
│   ├── Diagnostics/                   # LogFilterInspector, HidHideInspector
│   ├── Display/                       # VirtualDisplayManager, ResolutionNegotiator
│   ├── Emulators/                     # IEmulatorConfigSeeder, RetroArchConfigSeeder
│   ├── Input/                         # InputRouter, HidHide, ControllerManager
│   ├── Interop/                       # P/Invoke (AdvApi, Kernel32, User32, etc.)
│   ├── Monitoring/                    # SessionHealthCheck, GpuMonitor, MetricsCollector
│   ├── Sessions/                      # SeatManager, SessionLauncher, ProcessInjector
│   ├── Storage/                       # SecureFile, SharedLibraryProvisioner
│   ├── Streaming/                     # VibepolloManager, VibepolloConfigBuilder
│   └── [helper .cs files]             # AudioMuteHelper, WindowHideHelper, etc.
├── MultiSeat.Shared/                  # Разделяемые константы и модели
│   ├── Constants.cs                   # Порты, пути, лимиты
│   └── Models/                        # SeatInfo, SeatRequest, SeatServices, etc.
├── MultiSeat.Tests/                   # Unit + integration test files (22 файла)
├── MultiSeat.InputHook/               # C++ DLL для KB/M изоляции
└── MultiSeat.Dashboard/               # React SPA фронтенд
```

## Dependency Map

```
MultiSeat.Tests
    ↓
MultiSeat.Service
    ↓
MultiSeat.Shared
```

- `MultiSeat.Shared` — базовый проект, не зависит ни от чего
- `MultiSeat.Service` — зависит от Shared; содержит весь business logic
- `MultiSeat.Tests` — зависит от Service и Shared

## Entry Point: Program.cs

`Program.cs` выполняет две роли:

1. **Helper mode** — если запущен с аргументами (`--click-dialog`, `--mute-audio`, `--hide-windows`, `--enum-displays`, `--setup-display-isolation`, `--set-display-hz`, `--hidhide`, `--audio-peaks`, `--capture-loopback`, `--log-filters`), выполняет одну задачу и завершается. Helper modes запускаются в целевых сессиях через `CreateProcessAsUser`.

2. **Service mode** — без аргументов создаёт `Host.CreateApplicationBuilder`, регистрирует DI-сервисы, и запускает `MultiSeatWorker` как `BackgroundService`.

## DI Registration (singletons)

Все основные компоненты регистрируются как **Singleton**:

```csharp
builder.Services.AddSingleton<AccountManager>();
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddSingleton<RdpWrapper>();
builder.Services.AddSingleton<ProcessInjector>();
builder.Services.AddSingleton<VirtualDisplayManager>();
builder.Services.AddSingleton<VibepolloManager>();
builder.Services.AddSingleton<VibepolloConfigBuilder>();
builder.Services.AddSingleton<SeatAppManager>();
builder.Services.AddSingleton<OnConnectAppLauncher>();
builder.Services.AddSingleton<ClientResolutionFollower>();
builder.Services.AddSingleton<VibepolloServerQuery>();
builder.Services.AddSingleton<HostVibepolloMonitor>();
builder.Services.AddSingleton<PortAllocator>();
builder.Services.AddSingleton<ControllerManager>();
builder.Services.AddSingleton<InputRouter>();
builder.Services.AddSingleton<InputHookManager>();
builder.Services.AddSingleton<HidHideConfigurator>();
builder.Services.AddSingleton<FirewallManager>();
builder.Services.AddSingleton<SeatPresetStore>();
builder.Services.AddSingleton<GpuMonitor>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<SessionHealthCheck>();
builder.Services.AddSingleton<SharedLibraryProvisioner>();
builder.Services.AddSingleton<IEmulatorConfigSeeder, RetroArchConfigSeeder>();
builder.Services.AddSingleton<SeatManager>();  // ПОСЛЕ всех зависимостей
```

## MultiSeatWorker — Главный Background Service

`MultiSeatWorker` (наследует `BackgroundService`) — центральный оркестратор. Его `ExecuteAsync` выполняет:

1. **Step 0**: Kill orphaned Vibepollo processes от предыдущего запуска
2. **Step 0a**: Normalize managed account privileges (убрать admin у seat accounts)
3. **Step 0b**: Set DWM frame interval для RDP сессий (1ms для высокого FPS)
4. **Step 1**: Verify RDP Wrapper multi-session available
5. **Step 2**: Start input subsystems (HidHide reset, InputRouter, InputHookManager)
6. **Step 3**: Ensure API port open in Windows Firewall
7. **Step 3b**: Provision shared game library
8. **Step 4**: Start embedded API server (ASP.NET Core Minimal API)
9. **Step 5**: Auto-provision seats from presets
10. **Step 6**: Health-check loop (каждые 5 секунд)

## Key Runtime Paths

| Path | Purpose |
|------|---------|
| `C:\Program Files\MultiSeat\` | Service install dir |
| `C:\Program Files\Vibepollo\` | MultiSeat's own Vibepollo install |
| `C:\ProgramData\MultiSeat\` | Runtime data, configs, logs |
| `C:\ProgramData\MultiSeat\logs\` | `audio-helper.log` only |
| `C:\ProgramData\MultiSeat\vibepollo\` | Per-seat Vibepollo config dirs |
| `C:\ProgramData\MultiSeat\accounts.json` | Credential store (DPAPI) |
| `C:\ProgramData\MultiSeat\api-key.txt` | API key file |
| `C:\ProgramData\MultiSeat\multiseat-host.json` | Seat presets |
| `C:\MultiSeatGames\` | Shared game library |

## Configuration Model

### appsettings.json
Основной конфиг. Секция `MultiSeat` → `MultiSeatOptions`. Загружается из exe-директории.

### appsettings.local.json
Host-local overrides. Загружается **последней**, перезаписывает всё остальное. Gitignored.

### SeatPresetStore
Persisted seat definitions. Хранятся в `multiseat-host.json`. Переживают restart сервиса.

### Credential Store
DPAPI-зашифрованные пароли в `accounts.json`. Scope: CurrentUser (SYSTEM). ACL: только SYSTEM + Administrators.

## Windows Service

- Service Name: `MultiSeatService`
- Runs as: SYSTEM
- Logging: Windows Event Log (Application log)
  - Source: `MultiSeat.Service` (application logging)
  - Source: `MultiSeatService` (service lifecycle)

## API Architecture

- ASP.NET Core Minimal API, встроенный в Windows Service
- Default port: 9550 (HTTP, plaintext)
- Auth: API key (X-MultiSeat-Key header or ?key= query param)
- CORS: restricted by default (loopback only)
- WebSocket: /ws/seats — real-time seat state broadcast

### Endpoint Groups

| Group | Path | Purpose |
|-------|------|---------|
| SeatEndpoints | /api/seats | CRUD, provision, teardown, services |
| AccountEndpoints | /api/accounts | Windows account management |
| SystemEndpoints | /api/system | System status, rebuild, auth |
| HostEndpoints | /api/host | Host Vibepollo monitoring |
| InputEndpoints | /api/input | Controller assignment |
| WebSocketHub | /ws/seats | Real-time updates |

## Windows-Specific Parts

- **P/Invoke**: AdvApi (CreateProcessAsUser, token manipulation), Kernel32 (session management), User32 (display APIs), WtsApi (Terminal Services), NetApi (user management), UserEnv (environment blocks)
- **Windows Service**: AddWindowsService() integration
- **Registry**: DWM frame interval, SudoVDA detection
- **DPAPI**: Credential encryption (SYSTEM scope)
- **WMI**: Process management (GetManagedVibepolloPids)
- **Core Audio API**: WASAPI loopback capture, audio endpoint manipulation

## Namespace Map

```
MultiSeat.Service                    # Root namespace
├── Accounts                         # Windows account CRUD
│   └── AccountManager
├── Api                              # HTTP API + WebSocket
│   ├── ApiServer, SeatEndpoints, AccountEndpoints, SystemEndpoints
│   ├── HostEndpoints, InputEndpoints, WebSocketHub
│   └── ApiAuthState, ApiInputValidation
├── Configuration                    # Options + persistence
│   ├── MultiSeatOptions
│   └── SeatPresetStore
├── Diagnostics                      # Inspector tools
│   ├── HidHideInspector
│   └── LogFilterInspector
├── Display                          # Virtual display management
│   ├── VirtualDisplayManager
│   ├── ResolutionNegotiator
│   ├── DisplayEnumeratorHelper
│   └── AdvancedColorHelper
├── Emulators                        # Emulator integration
│   ├── IEmulatorConfigSeeder (interface)
│   └── RetroArchConfigSeeder
├── Input                            # Input subsystem
│   ├── InputRouter                  # XInput → ViGEm bridge
│   ├── ControllerManager            # ViGEm virtual controllers
│   ├── InputHookManager             # KB/M hooks (currently no-op)
│   ├── HidHideConfigurator          # Per-seat gamepad isolation
│   ├── HidHideCli                   # CLI wrapper
│   ├── HidHideSessionJail           # Session jail rules
│   └── HidHideDevice                # Device model
├── Interop                          # P/Invoke declarations
│   ├── AdvApi, Kernel32, User32, WtsApi, NetApi
│   ├── UserEnv, Shell32, ComInterfaces
│   ├── XInput, CredApi
│   ├── MultiSeatInputHookNative
│   └── SafeTokenHandle
├── Monitoring                       # Health checks + metrics
│   ├── SessionHealthCheck           # Main health loop
│   ├── VibepolloServerQuery         # HTTP probe
│   ├── HostVibepolloMonitor         # Standalone Vibepollo detection
│   ├── GpuMonitor                   # NVIDIA GPU stats
│   └── MetricsCollector             # Process + GPU metrics
├── Sessions                         # Session lifecycle
│   ├── SeatManager                  # Top-level orchestrator
│   ├── SessionLauncher              # RDP loopback session creation
│   ├── ProcessInjector              # CreateProcessAsUser wrapper
│   ├── RdpWrapper                   # RDP Wrap/TermWrap detection
│   ├── RdpFileBuilder               # Default.rdp generation
│   ├── RdpCredentialStore           # CredWrite/CredRead wrapper
│   ├── DialogClickHelper            # Button-click automation
│   └── SeatState                    # State transition validation
├── Storage                          # File management
│   ├── SecureFile                   # ACL hardening
│   └── SharedLibraryProvisioner     # Shared games/ROMs
├── Streaming                        # Vibepollo integration
│   ├── VibepolloManager             # Process lifecycle
│   ├── VibepolloConfigBuilder       # sunshine.conf generation
│   ├── VibepolloLogParser           # Log parsing
│   ├── OnConnectAppLauncher         # App launch on client connect
│   ├── ClientResolutionFollower     # Resolution following
│   ├── SeatAppManager               # Per-seat app profiles
│   ├── SeatAppManagerAccessor       # DI bridge
│   ├── PortAllocator                # Port block management
│   └── FirewallManager              # Windows Firewall rules
└── [Root files]
    ├── MultiSeatWorker              # BackgroundService
    ├── Program.cs                   # Entry point
    ├── AudioMuteHelper              # Audio muting
    ├── AudioLoopbackCaptureHelper   # Loopback capture
    ├── DisplayModeHelper            # Display isolation
    └── WindowHideHelper             # mstsc window hiding

MultiSeat.Shared
├── Constants                        # System-wide constants
└── Models
    ├── SeatInfo, SeatRequest, SeatPreset, SeatServices
    ├── AccountInfo, SystemStatus, HostVibepolloInfo
    └── SeatAppProfile
```
