# Target Architecture Draft

**Date**: 2026-08-30
**Purpose**: Preliminary architecture diagram based on research findings

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Management API                            │
│              ASP.NET Core REST + WebSocket                    │
│                      React Dashboard                          │
└─────────────────────────────┬───────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────┐
│                      Seat Manager                            │
│               Orchestrates all subsystems                    │
│                  (9-step pipeline)                            │
└───┬───────────┬───────────┬───────────┬───────────┬─────────┘
    │           │           │           │           │
    ▼           ▼           ▼           ▼           ▼
┌───────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐
│ User  │ │ Session │ │ Display │ │ Provider│ │ Process │
│Manager│ │ Manager │ │ Manager │ │ Manager │ │ Tracker │
└───┬───┘ └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘
    │          │           │           │           │
    ▼          ▼           ▼           ▼           ▼
┌───────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐
│Windows│ │   RDP   │ │ SudoVDA │ │Vibepollo│ │Job Object│
│Accounts│ │loopback │ │ Driver  │ │  Apollo │ │  + PID  │
└───────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘
                     │           │
                     ▼           ▼
              ┌─────────────────────────┐
              │    Windows Session       │
              │  ┌─────┬─────┬─────┐   │
              │  │Game │Audio│Input│   │
              │  └─────┴─────┴─────┘   │
              └─────────────────────────┘
```

---

## Components

### 1. Management API

**Technology**: ASP.NET Core + React

**Responsibilities**:
- REST API for CRUD operations
- WebSocket for real-time seat updates
- Dashboard UI
- Authentication (API key)

### 2. Seat Manager

**Technology**: C# (MultiSeat.Service)

**Responsibilities**:
- Orchestrate 9-step provisioning pipeline
- Coordinate all subsystems
- Manage seat lifecycle
- Handle recovery

### 3. User Manager

**Technology**: C# (AccountManager)

**Responsibilities**:
- Create/delete Windows accounts
- Manage group membership
- Store credentials (DPAPI)

### 4. Session Manager

**Technology**: C# (SessionLauncher)

**Responsibilities**:
- Create RDP loopback sessions
- Manage session lifecycle
- Token manipulation (CreateProcessAsUser)

### 5. Display Manager

**Technology**: C# (VirtualDisplayManager) + SudoVDA driver

**Responsibilities**:
- Create/destroy virtual displays
- Apply display isolation (primary + shrunk)
- Set refresh rate

### 6. Provider Manager

**Technology**: C# (IStreamingProvider + adapters)

**Responsibilities**:
- Manage streaming provider lifecycle
- Generate provider configuration
- Monitor provider health
- Support multiple providers

### 7. Process Tracker

**Technology**: C# (ProcessTracker)

**Responsibilities**:
- Track PID → Seat mapping
- Manage Job Objects
- Detect game crashes
- Adopt residual processes

### 8. Input Manager

**Technology**: C# (HidHideConfigurator)

**Responsibilities**:
- Apply HidHide session jail
- Manage controller routing (optional)
- Input device assignment

### 9. Health Monitor

**Technology**: C# (SessionHealthCheck)

**Responsibilities**:
- 5s health check interval
- Progressive crash backoff
- Display re-detection
- Full seat re-provision

---

## Data Flow

### Provisioning

```
API Request → SeatManager
    → AccountManager.CreateAccount()
    → SessionManager.LaunchSession()
    → DisplayManager.CreateDisplay()
    → FirewallManager.OpenPorts()
    → ProviderManager.StartProvider()
    → DisplayManager.ApplyIsolation()
    → InputManager.ApplyJail()
    → HealthMonitor.StartMonitoring()
    → API Response (Ready)
```

### Streaming

```
Moonlight Client → Provider (Vibepollo)
    → Capture (DDA/WGC)
    → Encode (NVENC/AMF)
    → Stream (RTSP/WebRTC)
    → Client receives video/audio/input
```

### Recovery

```
Health Check → Failure Detected
    → Classify (Provider/Display/Session/Full)
    → Apply Backoff (progressive)
    → Restart Component
    → Verify Health
    → Resume Monitoring
```

---

## Key Design Decisions

### 1. Monolith with Modules

Single process with clear module boundaries. No microservices overhead.

### 2. External Streaming Provider

Vibepollo/Apollo/Sunshine runs as external process. MultiSeat orchestrates.

### 3. Existing Drivers

Use SudoVDA for display, HidHide for input. No custom driver development.

### 4. Windows APIs

Use standard Windows APIs (CreateProcessAsUser, WTS, DPAPI, ACL). No wrappers.

### 5. Job Object Isolation

Each seat gets a Job Object for guaranteed process cleanup.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current architecture is monolith | Service structure | VERIFIED |
| Vibepollo runs as external process | VibepolloManager | VERIFIED |
| SudoVDA is kernel-mode driver | Driver architecture | VERIFIED |
| HidHide is kernel-mode driver | Driver architecture | VERIFIED |
| CreateProcessAsUser is standard API | Windows API | VERIFIED |
| Job Objects are standard API | Windows API | VERIFIED |
