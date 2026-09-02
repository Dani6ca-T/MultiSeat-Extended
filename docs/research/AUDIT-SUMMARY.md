# MultiSeat-Extended: Итоговый отчёт аудита

## 1. Что представляет собой проект сейчас

MultiSeat-Extended — это Windows multiseat платформа на базе .NET 9 Windows Service, позволяющая запускать несколько одновременных Moonlight game-streaming сессий на одном хосте. Каждый seat получает изолированный Windows-аккаунт, виртуальный дисплей (SudoVDA), виртуальное аудиоустройство (per-session Remote Audio) и выделенный экземпляр Vibepollo (форк Sunshine). Управляется через встроенный ASP.NET Core API и React dashboard.

Проект является форком MultiSeat (vibesoftwarecoder) и развивается как open-source альтернатива DuoStream.

## 2. Что уже умеет

- ✅ Управление Windows аккаунтами (создание, привязка, удаление)
- ✅ Создание interactive Windows sessions через RDP loopback
- ✅ Per-seat Vibepollo streaming server с изолированной конфигурацией
- ✅ Виртуальные дисплеи SudoVDA с display isolation
- ✅ Per-session audio (полная изоляция без VAC/VoiceMeeter)
- ✅ Port allocation (30 портов на seat, без коллизий)
- ✅ Windows Firewall management
- ✅ ViGEm virtual controllers (опционально)
- ✅ HidHide session jail для gamepad isolation (опционально)
- ✅ Launch-on-connect apps (Steam Big Picture и др.)
- ✅ Client resolution following
- ✅ Shared game library (Steam + ROMs)
- ✅ Emulator netplay (RetroArch)
- ✅ Health check loop с auto-recovery
- ✅ WebSocket real-time updates
- ✅ API key authentication
- ✅ DPAPI credential encryption
- ✅ Playnite, RTSS, Lossless Scaling интеграция
- ✅ HDR probe (диагностика)
- ✅ NVENC quality presets
- ✅ Auto-provisioning from presets
- ✅ Coexistence with standalone Vibepollo
- ✅ 22 test files (unit + integration)

## 3. Что отсутствует

- ❌ Streaming provider abstraction (только Vibepollo)
- ❌ HTTPS для API
- ❌ Microphone path (PerSession trade-off)
- ❌ Keyboard/mouse session isolation (InputHookManager = no-op)
- ❌ Game process tracking (PID)
- ❌ Multi-GPU support
- ❌ Metrics export (Prometheus/Grafana)
- ❌ API versioning
- ❌ Configuration validation
- ❌ Graceful degradation for partial provisioning
- ❌ Teardown retry logic
- ❌ Rate limiting
- ❌ Request logging
- ❌ Dashboard i18n
- ❌ Docker/containers support
- ❌ Linux support (Windows-only by design)

## 4. Главные архитектурные проблемы (Top 10)

| # | Problem | Severity |
|---|---------|----------|
| 1 | Vibepollo tightly coupled to Seat lifecycle | HIGH |
| 2 | No streaming provider abstraction | HIGH |
| 3 | Display logic coupled to streaming provider | MEDIUM |
| 4 | Configuration contains provider-specific fields | MEDIUM |
| 5 | No game process tracking | MEDIUM |
| 6 | WebSocket broadcasts full SeatInfo | MEDIUM |
| 7 | No graceful degradation | MEDIUM |
| 8 | Teardown is best-effort only | MEDIUM |
| 9 | InputHookManager is architecturally dead | LOW |
| 10 | Port allocation coupled to Vibepollo offsets | LOW |

## 5. Главные технические ограничения (Top 10)

| # | Limitation | Type |
|---|-----------|------|
| 1 | RDP Wrapper dependency + breaks on updates | Windows |
| 2 | Session resolution fixed at connect time | Windows |
| 3 | CreateProcessAsUser cannot create sessions from Session 0 | Windows |
| 4 | NVIDIA consumer GPU: 3-5 NVENC sessions max | Driver |
| 5 | SudoVDA display created only on client connect | Provider |
| 6 | No Vibepollo API for seat management | Provider |
| 7 | Single machine-wide default audio device | Windows |
| 8 | DXGI ACCESS_DENIED for disconnected sessions | Windows |
| 9 | Vibepollo ignores log_path config key | Provider |
| 10 | HidHide session jail is undocumented | Driver |

## 6. Главные возможности для развития (Top 10)

| # | Opportunity | Impact |
|---|------------|--------|
| 1 | Streaming provider abstraction (IStreamingProvider) | HIGH — enables DuoStream, other backends |
| 2 | Multi-seat beyond 8 (dynamic port allocation) | HIGH — true N-seat platform |
| 3 | HTTPS for API (Let's Encrypt, self-signed) | HIGH — security |
| 4 | Game process tracking + auto-restart | MEDIUM — reliability |
| 5 | Microphone via Vibepollo WebRTC | MEDIUM — feature completeness |
| 6 | Multi-GPU support | MEDIUM — hardware flexibility |
| 7 | Metrics export (Prometheus) | MEDIUM — observability |
| 8 | API versioning | LOW — stability |
| 9 | Keyboard/mouse isolation (re-architecture) | LOW — security |
| 10 | Docker/container support | LOW — deployment flexibility |

## 7. Что нельзя ломать

### GOOD-001: SeatManager provisioning pipeline
- Well-structured 9-step pipeline with clear dependencies
- Each step has error handling and state tracking
- Best-effort teardown is intentional design

### GOOD-002: PerSession audio design
- Eliminates VAC/VoiceMeeter dependency
- True audio isolation between seats
- No host audio subsystem wedging

### GOOD-003: Port allocation scheme
- 30-port blocks prevent collisions
- Bitmap allocator is O(1)
- Coexistence with standalone Vibepollo

### GOOD-004: DPAPI credential encryption
- SYSTEM scope prevents non-admin access
- Auto-migration from legacy scope
- ACL hardening on store files

### GOOD-005: Session health check loop
- Monitors session, Vibepollo, display
- Auto-recovery for common failures
- Sleep/wake handling

### GOOD-006: Display isolation
- SudoVDA as session primary
- RDP display shrunk to 640×480
- Reduces TermService CPU from ~70% to <5%

### GOOD-007: Vibepollo coexistence
- Own install directory
- Own port range
- Never kills standalone Vibepollo

### GOOD-008: Console session guard
- Refuses to launch seat processes into console session
- Post-launch verification
- Kills mis-targeted processes

### GOOD-009: API authentication
- API key with auto-generation
- Per-endpoint auth middleware
- Loopback-only option

### GOOD-010: Launch-on-connect apps
- Solves controller detection timing
- Configurable per seat
- Optional kill on disconnect

## 8. Предварительная целевая архитектура (High-Level)

```
┌─────────────────────────────────────────────────────────┐
│                    MultiSeat Core                        │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ SeatManager │  │ SessionMngr  │  │ AccountMngr   │  │
│  └──────┬──────┘  └──────┬───────┘  └───────────────┘  │
│         │                │                              │
│  ┌──────▼────────────────▼───────┐                      │
│  │     IStreamingProvider        │ ← NEW ABSTRACTION   │
│  │  ┌─────────┐ ┌────────────┐  │                      │
│  │  │Vibepollo│ │DuoStream   │  │                      │
│  │  │Provider │ │Provider    │  │                      │
│  │  └─────────┘ └────────────┘  │                      │
│  └───────────────────────────────┘                      │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │DisplayManager│  │  InputRouter │  │  HealthCheck │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│                                                         │
│  ┌──────────────────────────────────────────────────┐   │
│  │              Configuration Layer                  │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌──────────┐ │   │
│  │  │GlobalConfig │  │ SeatConfig  │  │Provider  │ │   │
│  │  │             │  │             │  │Config    │ │   │
│  │  └─────────────┘  └─────────────┘  └──────────┘ │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                              │
                    ┌─────────▼─────────┐
                    │   ASP.NET Core    │
                    │   Minimal API     │
                    │   + WebSocket     │
                    └─────────┬─────────┘
                              │
                    ┌─────────▼─────────┐
                    │  React Dashboard  │
                    └───────────────────┘
```

### Key Changes

1. **IStreamingProvider interface** — abstracts Vibepollo, enables DuoStream
2. **Provider-specific config** — separate from global MultiSeatOptions
3. **Dynamic port allocation** — remove MaxSeats ceiling
4. **Game process tracking** — PID tracking, auto-restart
5. **API versioning** — /api/v1/, /api/v2/
6. **HTTPS support** — certificate management
7. **Metrics export** — Prometheus endpoint

## 9. Следующий этап

### Исследование внешних проектов

1. **Vibepollo** — понять API, config format, limitations глубже
2. **DuoStream** — как решает те же проблемы, что абстрагирует
3. **TermWrap** — замена RDPWrap, совместимость
4. **Helios** — альтернативный streaming backend
5. **neo_multiseat** — другие подходы к multiseat
6. **MultiseatProject** — сравнение архитектур
7. **DuoController** — controller management подходы
8. **Apollo multi-instance launcher** — multi-instance patterns
9. **LuaTools** — scripting и автоматизация
10. **Другие найденные проекты** — emerging solutions

### Приоритеты

1. Определить IStreamingProvider interface
2. Спроектировать configuration abstraction
3. Оценить DuoStream integration effort
4. Спланировать multi-GPU support
5. Определить metrics/monitoring strategy
