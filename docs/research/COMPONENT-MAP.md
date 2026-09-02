# MultiSeat-Extended: Карта компонентов

## Зависимости между проектами

```
MultiSeat.Tests
    ├──→ MultiSeat.Service (тестирует)
    └──→ MultiSeat.Shared (тестирует)

MultiSeat.Service
    ├──→ MultiSeat.Shared (использует модели и константы)
    ├──→ Microsoft.Extensions.Hosting.WindowsServices (Windows Service)
    ├──→ Nefarius.ViGEm.Client (виртуальные контроллеры)
    ├──→ System.Security.Cryptography.ProtectedData (DPAPI)
    ├──→ System.Diagnostics.PerformanceCounter (метрики)
    └──→ System.Management (WMI)

MultiSeat.Shared
    └── (нет зависимостей)
```

## Зависимости между компонентами Service

### SeatManager — Центральный оркестратор

SeatManager依赖所有其他 компоненты:

```
SeatManager
    ├──→ AccountManager (Windows accounts)
    ├──→ SessionLauncher (RDP loopback sessions)
    ├──→ ProcessInjector (CreateProcessAsUser)
    ├──→ VirtualDisplayManager (SudoVDA)
    ├──→ VibepolloManager (streaming server)
    ├──→ VibepolloConfigBuilder (configuration)
    ├──→ PortAllocator (port blocks)
    ├──→ FirewallManager (Windows Firewall)
    ├──→ ControllerManager (ViGEm controllers)
    ├──→ InputRouter (XInput routing)
    ├──→ InputHookManager (KB/M hooks)
    ├──→ HidHideConfigurator (gamepad isolation)
    ├──→ OnConnectAppLauncher (app launch on connect)
    ├──→ VibepolloServerQuery (HTTP probe)
    └──→ IEmulatorConfigSeeder[] (emulator config)
```

### MultiSeatWorker — Background Service

```
MultiSeatWorker
    ├──→ SeatManager (seat lifecycle)
    ├──→ RdpWrapper (RDP Wrap detection)
    ├──→ SessionHealthCheck (health checks)
    ├──→ InputRouter (XInput polling)
    ├──→ InputHookManager (KB/M hooks)
    ├──→ HidHideConfigurator (gamepad isolation)
    ├──→ FirewallManager (API port)
    ├──→ SeatPresetStore (autostart)
    ├──→ SharedLibraryProvisioner (shared games)
    └──→ AccountManager (privilege normalization)
```

### SessionLauncher — Session Creation

```
SessionLauncher
    ├──→ AccountManager (credentials)
    ├──→ RdpCredentialStore (CredWrite/CredRead)
    ├──→ RdpFileBuilder (Default.rdp)
    ├──→ WtsApi (Terminal Services API)
    ├──→ Kernel32 (session management)
    └──→ AdvApi (token manipulation)
```

### ProcessInjector — Process Launch

```
ProcessInjector
    ├──→ SessionLauncher (token acquisition)
    ├──→ AdvApi (CreateProcessAsUserW)
    ├──→ UserEnv (CreateEnvironmentBlock)
    └──→ Kernel32 (process/thread APIs)
```

### VibepolloManager — Streaming Lifecycle

```
VibepolloManager
    ├──→ VibepolloConfigBuilder (config generation)
    ├──→ ProcessInjector (process launch in session)
    └──→ MultiSeatOptions (paths, ports)
```

### VibepolloConfigBuilder — Configuration Generation

```
VibepolloConfigBuilder
    ├──→ MultiSeatOptions (all settings)
    ├──→ Constants (port offsets)
    └──→ IServiceProvider (optional, for Playnite)
```

### VirtualDisplayManager — Display Management

```
VirtualDisplayManager
    ├──→ ProcessInjector (enum-displays helper)
    ├──→ ResolutionNegotiator (resolution validation)
    └──→ MultiSeatOptions (display settings)
```

### InputRouter — Input Routing

```
InputRouter
    ├──→ ControllerManager (ViGEm virtual controllers)
    └──→ XInput (physical controller polling)
```

### HidHideConfigurator — Gamepad Isolation

```
HidHideConfigurator
    ├──→ HidHideCli (CLI wrapper)
    ├──→ HidHideSessionJail (session jail rules)
    ├──→ HidHideDevice (device model)
    └──→ MultiSeatOptions (HidHide settings)
```

### SessionHealthCheck — Health Monitoring

```
SessionHealthCheck
    ├──→ SessionLauncher (session alive check)
    ├──→ VibepolloManager (process alive check)
    ├──→ SeatManager (display isolation, late detection)
    ├──→ OnConnectAppLauncher (client connect/disconnect)
    └──→ ClientResolutionFollower (resolution following)
```

### AccountManager — Account Management

```
AccountManager
    ├──→ NetApi (NetUserAdd, NetUserDel, etc.)
    ├──→ ProtectedData (DPAPI)
    ├──→ SecureFile (ACL hardening)
    └──→ UserEnv (CreateProfile)
```

## API → Service Dependencies

```
ApiServer
    ├──→ SeatManager
    ├──→ RdpWrapper
    ├──→ SessionLauncher
    ├──→ AccountManager
    ├──→ GpuMonitor
    ├──→ MetricsCollector
    ├──→ SessionHealthCheck
    ├──→ HostVibepolloMonitor
    ├──→ VirtualDisplayManager
    └──→ SeatPresetStore
```

## Модели данных

### SeatInfo — Основная модель seat

```csharp
public sealed class SeatInfo {
    Guid Id;
    string AccountName;
    int SessionId;
    SeatStatus Status;
    int Width, Height, Fps;
    string? DisplayDevicePath;
    int PortBase;
    int VibepolloProcessId;
    int RetroArchNetplayPort;
    int ViGEmControllerIndex;
    DateTimeOffset CreatedAt;
    DateTimeOffset? ReadyAt;
    string? ErrorMessage;
    string? LaunchApp;
    bool AutoStart;
    NvencQualityPreset NvencPreset;
    // Vibepollo Advanced Features
    bool? EnablePlaynite;
    bool? EnableRtss;
    bool? EnableLosslessScaling;
    int HdrMode;
    string? ProvisioningStep;
}
```

### SeatStatus — State Machine

```
Idle → Provisioning → Configuring → Ready → Streaming
  ↑                                          ↓
  └─── TearingDown ←───────────────────────┘
  ↑
  └─── Error → TearingDown → Idle
```

### SeatServices — Runtime Status

```csharp
public sealed class SeatServices {
    bool Vibepollo;          // Process alive
    bool VibepolloReachable; // HTTP probe answered
    bool VibepolloStreaming; // Client actively streaming
    int VibepolloRestarts;
    bool Display;            // SudoVDA UUID known
    bool Audio;              // PerSession = always true
    bool AudioManaged;       // false in PerSession
    bool Controller;         // ViGEm controller exists
    bool ControllerManaged;  // EnableViGEmController
    bool InputHooks;
    bool Firewall;
    bool Session;
}
```

## IPC / Communication Flow

```
Dashboard (React SPA)
    ↓ HTTP/WS
ApiServer (ASP.NET Core Minimal API, port 9550)
    ↓ DI injection
SeatManager, AccountManager, etc. (Singletons)
    ↓ P/Invoke
Windows APIs (CreateProcessAsUser, WTS*, NetApi32, etc.)
    ↓
Windows Sessions (RDP loopback)
    ↓
Vibepollo instances (per-seat)
    ↓
Moonlight clients
```

## Helper Mode Flow

```
SeatManager → SessionLauncher.RunHelperInSeatSession()
    → ProcessInjector.LaunchInSessionAsync()
        → CreateProcessAsUserW (multi-seat service.exe --helper-mode ...)
            → Helper executes in seat session
            → Returns result (exit code, file output)
```
