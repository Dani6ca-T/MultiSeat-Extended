# MultiSeat-Extended: Целевой набор возможностей (Target Capabilities)

## Принцип

Это НЕ архитектура и НЕ implementation plan. Это список того, что конечная система **должна уметь**. Каждая capability привязана к найденным проектам и реальным use cases.

---

## Core Platform

### C-001: N Seats (N > 2)

- **Description**: Поддержка неограниченного количества seats (ограничено только железом)
- **Source**: MultiSeat-Extended (текущий лимит 8), Duo (без лимита в paid tier), neo_multiseat (3-10)
- **Current status**: YES (configurable MaxSeats)
- **Target**: Dynamic allocation без фиксированного потолка

### C-002: N Windows Users

- **Description**: Создание и управление N изолированными Windows аккаунтами
- **Source**: MultiSeat-Extended (AccountManager), Duo, neo_multiseat
- **Current status**: YES
- **Target**: Автоматическое создание, управление, удаление

### C-003: N Concurrent Sessions

- **Description**: N одновременных interactive Windows sessions
- **Source**: MultiSeat-Extended (RDP loopback), Duo (TermWrap), TermWrap, neo_multiseat
- **Current status**: YES (через RDPWrap/TermWrap)
- **Target**: Автоматическое обнаружение и recovery

### C-004: N Streaming Providers

- **Description**: Возможность подключения разных streaming providers
- **Source**: Vibepollo, Apollo, Sunshine, Helios (manages multiple)
- **Current status**: NO (только Vibepollo)
- **Target**: IStreamingProvider abstraction

### C-005: Provider Orchestration

- **Description**: Оркестрация lifecycle streaming providers
- **Source**: Helios (Spawner service), MultiSeat-Extended (VibepolloManager)
- **Current status**: YES (для Vibepollo)
- **Target**: Универсальный provider lifecycle management

---

## Session Management

### S-001: Automatic Session Creation

- **Description**: Автоматическое создание interactive sessions через RDP loopback
- **Source**: MultiSeat-Extended (SessionLauncher), Duo
- **Current status**: YES
- **Target**: Reliable, fast, with proper cleanup

### S-002: Session Monitoring

- **Description**: Мониторинг состояния sessions (Active/Disconnected/Dead)
- **Source**: MultiSeat-Extended (WTS query), Duo
- **Current status**: YES
- **Target**: Real-time monitoring with health checks

### S-003: Session Reconnect

- **Description**: Автоматический reconnect при sleep/wake
- **Source**: MultiSeat-Extended (SessionHealthCheck), Duo
- **Current status**: YES
- **Target**: Seamless reconnect with display restoration

### S-004: Session Cleanup

- **Description**: Полная очистка при teardown
- **Source**: MultiSeat-Extended (TeardownSeatInternalAsync)
- **Current status**: YES (best-effort)
- **Target**: Reliable cleanup with retry logic

---

## Streaming

### ST-001: Hardware Encoding (NVENC/AMF)

- **Description**: Аппаратное кодирование видео
- **Source**: Vibepollo, Apollo, Sunshine, Duo
- **Current status**: YES (через Vibepollo)
- **Target**: Multi-codec support (H.264, HEVC, AV1)

### ST-002: Per-Seat Encoder Settings

- **Description**: Индивидуальные настройки кодировщика на seat
- **Source**: MultiSeat-Extended (NvencPreset per seat), Vibepollo
- **Current status**: YES
- **Target**: Fine-grained quality/latency control

### ST-003: Client Resolution Matching

- **Description**: Разрешение seat = разрешение Moonlight клиента
- **Source**: MultiSeat-Extended (ClientResolutionFollower), Vibepollo, Duo
- **Current status**: YES (via reconnect)
- **Target**: Seamless matching (Duo approach)

### ST-004: High Refresh Rate

- **Description**: Поддержка высоких частот обновления (120Hz, 144Hz, 240Hz+)
- **Source**: MultiSeat-Extended (up to fps limit), Duo (up to 500Hz)
- **Current status**: YES (up to configured fps)
- **Target**: Match client refresh rate

### ST-005: HDR Streaming

- **Description**: HDR10 streaming support
- **Source**: Vibepollo (HEVC Main10), Duo (paid tier)
- **Current status**: NO (probe only, no-op)
- **Target**: Full HDR support when hardware allows

### ST-006: Frame Generation

- **Description**: NVIDIA Smooth Motion / frame interpolation
- **Source**: Vibepollo (NVIDIA Smooth Motion)
- **Current status**: NO
- **Target**: Optional frame generation per seat

---

## Display

### D-001: Virtual Display Per Seat

- **Description**: Виртуальный дисплей для каждого seat
- **Source**: MultiSeat-Extended (SudoVDA), Vibepollo, Duo (custom WDDM)
- **Current status**: YES
- **Target**: Reliable virtual display creation

### D-002: Display Isolation

- **Description**: SudoVDA как primary, RDP shrunk to 640×480
- **Source**: MultiSeat-Extended (unique approach)
- **Current status**: YES
- **Target**: Maintain low CPU usage

### D-003: Resolution Control

- **Description**: Управление разрешением seat
- **Source**: MultiSeat-Extended, Vibepollo, Duo
- **Current status**: YES (via reconnect)
- **Target**: Seamless resolution changes

### D-004: Refresh Rate Control

- **Description**: Управление частотой обновления seat
- **Source**: MultiSeat-Extended, Vibepollo, Duo
- **Current status**: YES
- **Target**: Match client refresh rate

### D-005: Headless Support

- **Description**: Работа без физического монитора
- **Source**: MultiSeat-Extended, Vibepollo, Duo
- **Current status**: YES
- **Target**: Reliable headless operation

### D-006: Display Restoration

- **Description**: Восстановление дисплея после crash/reboot
- **Source**: Vibepollo (display restoration), MultiSeat-Extended (late detection)
- **Current status**: YES (late detection)
- **Target**: Automatic restoration

---

## Audio

### AU-001: Audio Isolation Per Seat

- **Description**: Полная изоляция аудио между seats
- **Source**: MultiSeat-Extended (PerSession), Duo
- **Current status**: YES
- **Target**: No audio bleed between seats

### AU-002: Host Audio Protection

- **Description**: Защита host аудио от seat audio
- **Source**: MultiSeat-Extended (mstsc muted), Duo
- **Current status**: YES
- **Target**: Host audio unaffected by seats

### AU-003: Microphone Passthrough

- **Description**: Передача микрофона от клиента к game
- **Source**: Vibepollo (mic passthrough), Apollo
- **Current status**: NO
- **Target**: Via WebRTC mic support (future)

---

## Input

### IN-001: Keyboard/Mouse Isolation

- **Description**: Изоляция клавиатуры/мыши между seats
- **Source**: Duo (session ID filtering), MultiSeat-Extended (no-op)
- **Current status**: NO (no-op)
- **Target**: Working KB/M isolation

### IN-002: Gamepad Isolation

- **Description**: Изоляция геймпадов между seats
- **Source**: MultiSeat-Extended (HidHide), Duo (UMDF driver)
- **Current status**: YES (optional)
- **Target**: Mandatory isolation

### IN-003: Virtual Controller

- **Description**: Виртуальный контроллер на seat
- **Source**: MultiSeat-Extended (ViGEm), Duo (GameInput API)
- **Current status**: YES (optional)
- **Target**: Native Moonlight forwarding (default)

### IN-004: Controller Assignment

- **Description**: Назначение контроллера на seat
- **Source**: MultiSeat-Extended (InputRouter), Duo
- **Current status**: YES (auto + manual)
- **Target**: Flexible assignment

---

## Game / Process

### GP-001: Game Launching

- **Description**: Запуск игр в seat session
- **Source**: MultiSeat-Extended (ProcessInjector), Duo
- **Current status**: YES
- **Target**: Reliable process injection

### GP-002: Launch-on-Connect

- **Description**: Запуск apps при подключении клиента
- **Source**: MultiSeat-Extended (OnConnectAppLauncher), Duo
- **Current status**: YES
- **Target**: Configurable per seat

### GP-003: Game Mutex Isolation

- **Description**: Обход mutex для одновременных instances
- **Source**: Duo (Application Compatibility Layer)
- **Current status**: NO
- **Target**: Consider for future

### GP-004: Steam Multi-Instance

- **Description**: Множественные instances Steam
- **Source**: Duo (process patching)
- **Current status**: NO
- **Target**: Consider via shared library

### GP-005: Shared Game Library

- **Description**: Общая библиотека игр/ROMs
- **Source**: MultiSeat-Extended (icacls), Duo (Steam multi-box)
- **Current status**: YES
- **Target**: Extend to more platforms

### GP-006: Emulator Netplay

- **Description**: Сетевая игра эмуляторов между seats
- **Source**: MultiSeat-Extended (RetroArch ports)
- **Current status**: YES
- **Target**: Extend to more emulators

---

## Service Management

### SM-001: Windows Service Mode

- **Description**: Запуск как Windows Service (SYSTEM)
- **Source**: MultiSeat-Extended, Vibepollo, Duo, Helios
- **Current status**: YES
- **Target**: Reliable service operation

### SM-002: Auto-Start Seats

- **Description**: Автозапуск seats при старте сервиса
- **Source**: MultiSeat-Extended (SeatPresetStore), Duo
- **Current status**: YES
- **Target**: Configurable per seat

### SM-003: Crash Recovery

- **Description**: Автовосстановление при crash
- **Source**: MultiSeat-Extended (health check + auto-restart), Duo, Helios
- **Current status**: YES
- **Target**: Reliable recovery with limits

### SM-004: Health Monitoring

- **Description**: Периодическая проверка здоровья seats
- **Source**: MultiSeat-Extended (SessionHealthCheck, 5s)
- **Current status**: YES
- **Target**: Comprehensive checks

### SM-005: Orphan Cleanup

- **Description**: Очистка осиротевших процессов
- **Source**: MultiSeat-Extended (WMI query)
- **Current status**: YES
- **Target**: Safe cleanup without killing standalone instances

---

## API / Dashboard

### AD-001: REST API

- **Description**: HTTP API для управления seats
- **Source**: MultiSeat-Extended (ASP.NET Core), Duo (Web UI)
- **Current status**: YES
- **Target**: Comprehensive API

### AD-002: WebSocket Real-Time

- **Description**: Real-time updates через WebSocket
- **Source**: MultiSeat-Extended (/ws/seats), Duo
- **Current status**: YES
- **Target**: Efficient state broadcasting

### AD-003: Web Dashboard

- **Description**: Browser-based management UI
- **Source**: MultiSeat-Extended (React SPA), Duo (Web UI), Helios (WPF)
- **Current status**: YES
- **Target**: Responsive, feature-rich dashboard

### AD-004: API Authentication

- **Description**: Аутентификация API requests
- **Source**: MultiSeat-Extended (API key), Duo (session auth)
- **Current status**: YES
- **Target**: Secure authentication

### AD-005: HTTPS Support

- **Description**: HTTPS для API
- **Source**: Duo (HTTPS), Vibepollo (HTTPS web UI)
- **Current status**: NO
- **Target**: Add HTTPS support

---

## Security

### SC-001: Credential Encryption

- **Description**: Шифрование паролей seats
- **Source**: MultiSeat-Extended (DPAPI), Duo
- **Current status**: YES
- **Target**: Strong encryption

### SC-002: Seat Accounts as Standard Users

- **Description**: Seat accounts без admin привилегий
- **Source**: MultiSeat-Extended, Duo
- **Current status**: YES
- **Target**: Minimal privileges

### SC-003: ACL Hardening

- **Description**: Ограничение доступа к файлам
- **Source**: MultiSeat-Extended (SecureFile)
- **Current status**: YES
- **Target**: Protect sensitive files

### SC-004: Network Isolation

- **Description**: Изоляция сети между seats
- **Source**: Duo, neo_multiseat (Tailscale)
- **Current status**: Partial (loopback option)
- **Target**: Per-seat network isolation

---

## Diagnostics

### DG-001: GPU Monitoring

- **Description**: Мониторинг GPU utilization/temperature
- **Source**: MultiSeat-Extended (GpuMonitor)
- **Current status**: YES
- **Target**: Real-time GPU stats

### DG-002: Metrics Collection

- **Description**: Сбор метрик (CPU, RAM, GPU)
- **Source**: MultiSeat-Extended (MetricsCollector)
- **Current status**: YES
- **Target**: Export to Prometheus/Grafana

### DG-003: Log Management

- **Description**: Управление логами seats/providers
- **Source**: MultiSeat-Extended, Vibepollo, Helios
- **Current status**: YES
- **Target**: Centralized log viewing

### DG-004: Display Diagnostics

- **Description**: Диагностика дисплеев
- **Source**: MultiSeat-Extended (DisplayEnumeratorHelper)
- **Current status**: YES
- **Target**: Comprehensive display info

### DG-005: Audio Diagnostics

- **Description**: Диагностика аудио
- **Source**: MultiSeat-Extended (AudioLoopbackCaptureHelper)
- **Current status**: YES
- **Target**: Audio endpoint analysis

---

## Потребности, вытекающие из исследованных проектов

### From Duo

1. Game mutex isolation
2. Steam multi-instance
3. Application Compatibility Layer
4. Seamless display adjustment
5. HDR support

### From Vibepollo

1. Frame generation (NVIDIA Smooth Motion)
2. RTSS integration
3. Lossless Scaling integration
4. Display restoration

### From Helios

1. Named Pipe IPC pattern
2. Per-instance audio routing
3. Batch operations (Start All/Stop All)

### From TermWrap

1. Dynamic offset discovery
2. Audio recording redirection (EndpWrap)
3. Camera/USB redirection (UmWrap)

### From neo_multiseat

1. Live session monitoring
2. Tailscale integration
3. Automated RDPWrap recovery
