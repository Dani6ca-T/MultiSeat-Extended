# MultiSeat-Extended: Проблемы архитектуры

## Проблемы

### A-001: Apollo/Vibepollo tightly coupled to Seat lifecycle

- **Severity**: HIGH
- **Location**: Streaming/VibepolloConfigBuilder.cs, Streaming/VibepolloManager.cs
- **Why it's a problem**: Невозможно заменить streaming provider без переписывания конфигурации и lifecycle management
- **Consequences**:
  - Все ключи конфигурации специфичны для Vibepollo/Sunshine
  - Лог формат и парсинг специфичны для Vibepollo
  - Портовые оффсеты специфичны для Vibepollo
  - Display discovery через Vibepollo лог
  - Формат state/credentials файлов специфичен

### A-002: Display logic coupled to streaming provider

- **Severity**: MEDIUM
- **Location**: Display/VirtualDisplayManager.cs, Sessions/SeatManager.cs
- **Why it's a problem**: Display discovery зависит от Vibepollo лог парсинга; display isolation зависит от Vibepollo output_name
- **Consequences**:
  - Late display detection читает Vibepollo лог
  - ApplyDisplayIsolationAsync зависит от seat.DisplayDevicePath (set from Vibepollo log)
  - DisplayenumeratorHelper запускается через ProcessInjector (не через display API)

### A-003: No streaming provider abstraction

- **Severity**: HIGH
- **Location**: SeatManager.cs, VibepolloManager.cs, VibepolloConfigBuilder.cs
- **Why it's a problem**: SeatManager напрямую вызывает VibepolloManager, VibepolloConfigBuilder — нет interface для streaming provider
- **Consequences**:
  - Добавление нового provider требует изменения SeatManager
  - Невозможно иметь multiple providers одновременно
  - Тестирование требует реальный Vibepollo

### A-004: Configuration contains provider-specific fields

- **Severity**: MEDIUM
- **Location**: Configuration/MultiSeatOptions.cs
- **Why it's a problem**: MultiSeatOptions содержит VibepolloExePath, VibepolloConfigDir, NvencPreset — специфичные для Vibepollo
- **Consequences**:
  - Другой provider не может использовать эти поля
  - Конфигурация не абстрагирована

### A-005: Port allocation coupled to Vibepollo offsets

- **Severity**: LOW
- **Location**: Shared/Constants.cs, Streaming/PortAllocator.cs
- **Why it's a problem**: Port offsets определены в Constants (OffsetGfeHttps, OffsetVideo, etc.) — специфичны для Vibepollo
- **Consequences**:
  - Другой provider может использовать другие порты
  - PortAllocator блокирует 30 портов на seat (может быть избыточно)

### A-006: InputHookManager is architecturally dead

- **Severity**: LOW
- **Location**: Input/InputHookManager.cs, MultiSeatOptions.cs
- **Why it's a problem**: EnableKeyboardMouseIsolation = false (default), and the implementation is a no-op because hooks run in Session 0
- **Consequences**:
  - Код существует но не работает
  - Вводит в заблуждение (казается что isolation работает)
  - Need re-architecture to run inside seat session

### A-007: No game process tracking

- **Severity**: MEDIUM
- **Location**: Sessions/SeatManager.cs
- **Why it's a problem**: SeatManager не отслеживает PID запущенной игры отдельно
- **Consequences**:
  - Невозможно автоматически перезапустить crashed game
  - LaunchApp хранит путь но не PID
  - Health check не может проверить alive ли game

### A-008: Single process for all subsystems

- **Severity**: LOW
- **Location**: Program.cs, MultiSeatWorker.cs
- **Why it's a problem**: Windows Service содержит API server, health checks, input routing — всё в одном процессе
- **Consequences**:
  - Crash одного subsystem крашит всё
  - Невозможно обновлять subsystems независимо
  - Scaling limited by single process

### A-009: No graceful degradation

- **Severity**: MEDIUM
- **Location**: Sessions/SeatManager.cs (ProvisionSeatAsync)
- **Why it's a problem**: Если один step provisioning fails, весь seat telescope fails (best-effort cleanup)
- **Consequences**:
  - Частичный provisioning не сохраняется
  - Повторная попытка начинает сначала
  - Нет "degraded mode" для seat

### A-010: Teardown is best-effort only

- **Severity**: MEDIUM
- **Location**: Sessions/SeatManager.cs (TeardownSeatInternalAsync)
- **Why it's a problem**: Каждый step teardown обёрнут в try-catch с пустым catch
- **Consequences**:
  - Остаточные ресурсы (ports, sessions, processes) могут не освободиться
  - Нет retry logic для teardown
  - Нет verification что cleanup succeeded

### A-011: No configuration validation

- **Severity**: LOW
- **Location**: Configuration/MultiSeatOptions.cs
- **Why it's a problem**: MultiSeatOptions не валидируется при загрузке
- **Consequences**:
  - Невалидные значения (MaxSeats < 0, PortBase < 1024) не отлавливаются
  - Ошибки проявляются runtime

### A-012: WebSocket broadcasts full SeatInfo

- **Severity**: MEDIUM
- **Location**: Api/WebSocketHub.cs, Sessions/SeatManager.cs
- **Why it's a problem**: /ws/seats транслирует полные SeatInfo объекты — account names, session ids, ports, Vibepollo PIDs
- **Consequences**:
  - Любойwho can reach the port может прочитать всю информацию
  - Нет фильтрации sensitive данных

### A-013: No API versioning

- **Severity**: LOW
- **Location**: Api/ApiServer.cs
- **Why it's a problem**: API не имеет версионирования
- **Consequences**:
  - Breaking changes ломают существующих клиентов
  - Dashboard и API должны обновляться синхронно

### A-014: No health check for API server

- **Severity**: LOW
- **Location**: Monitoring/SessionHealthCheck.cs
- **Why it's a problem**: Health check мониторит seats но не API server
- **Consequences**:
  - API может упасть без обнаружения
  - Dashboard покажет "connected" но API не отвечает

### A-015: No metrics export

- **Severity**: LOW
- **Location**: Monitoring/MetricsCollector.cs
- **Why it's a problem**: MetricsCollector собирает данные но не экспортирует их
- **Consequences**:
  - Невозможно мониторить через Prometheus/Grafana
  - Данные доступны только через API
