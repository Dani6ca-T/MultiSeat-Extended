# Helios Architecture — Source-Level Analysis

**Date**: 2026-08-30
**Purpose**: Detailed architecture analysis of Helios-Sunshine-Manager

---

## Repository

- **URL**: https://github.com/MintCapybara924/Helios-Sunshine-Manager
- **License**: GPLv3
- **Language**: C# (.NET 8, WPF)
- **Status**: Active (v0.8.1)
- **AI Disclosure**: "Developed with assistance of AI (OpenAI Codex, Anthropic Claude)"

---

## Project Structure

```
src/
├── SunshineMultiInstanceManager.App/      # WPF desktop application (UI)
│   └── Helios.App.csproj
├── SunshineMultiInstanceManager.Core/     # Shared library
│   ├── Audio/
│   ├── Display/
│   ├── Process/
│   │   ├── ProcessManager.cs             # Instance lifecycle management
│   │   ├── ProcessLauncher.cs            # CreateProcessAsUser + Scheduled Task
│   │   └── GracefulShutdown.cs           # Graceful process termination
│   ├── Profiles/
│   ├── Scheduler/
│   ├── Storage/
│   │   ├── SettingsStore.cs              # JSON config persistence
│   │   └── Models/
│   │       ├── InstanceConfig.cs         # Per-instance configuration
│   │       └── InstanceRuntimeState.cs   # Runtime state (PID, alive, crash count)
│   └── Update/
└── SunshineMultiInstanceManager.Spawner/  # Windows Service
    ├── Helios.Spawner.csproj
    ├── Program.cs
    ├── ServiceLogger.cs
    └── SpawnerWorker.cs                  # Named Pipe server + process guardian
```

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────┐
│                Helios.App (WPF)                  │
│  ┌───────────────────────────────────────────┐  │
│  │  UI (Fluent/WinUI style)                  │  │
│  │  ├── Instance list (Start/Stop/Open UI)   │  │
│  │  ├── Settings (audio, headless, args)     │  │
│  │  ├── Log viewer (real-time)               │  │
│  │  └── System tray integration              │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│                      ▼                           │
│  ┌───────────────────────────────────────────┐  │
│  │  Named Pipe Client                        │  │
│  │  Pipe: ServiceControlConstants.PipeName   │  │
│  │  Protocol: JSON over line-delimited pipe  │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────┬───────────────────┘
                              │ Named Pipe
                              ▼
┌─────────────────────────────────────────────────┐
│          Helios.Spawner (Windows Service)        │
│          Account: LocalSystem (SYSTEM)           │
│  ┌───────────────────────────────────────────┐  │
│  │  SpawnerWorker : BackgroundService        │  │
│  │  ├── Named Pipe Server                    │  │
│  │  ├── Command handler (start/stop/status)  │  │
│  │  └── Guardian loop (5s interval)          │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│                      ▼                           │
│  ┌───────────────────────────────────────────┐  │
│  │  ProcessManager                           │  │
│  │  ├── StartAllAsync()                      │  │
│  │  ├── StopAllAsync()                       │  │
│  │  ├── RestartInstanceAsync()               │  │
│  │  ├── GuardianLoop (5s)                    │  │
│  │  ├── CrashBackoff (30/60/120s)            │  │
│  │  └── ResidualProcessAdoption             │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│                      ▼                           │
│  ┌───────────────────────────────────────────┐  │
│  │  ProcessLauncher                          │  │
│  │  ├── LaunchViaCreateProcessAsUser()       │  │
│  │  │   ├── WTSGetActiveConsoleSessionId     │  │
│  │  │   ├── WTSQueryUserToken                │  │
│  │  │   ├── DuplicateTokenEx (SYSTEM→Primary)│  │
│  │  │   ├── SetTokenInformation(SessionId)   │  │
│  │  │   ├── CreateEnvironmentBlock           │  │
│  │  │   └── CreateProcessAsUser              │  │
│  │  ├── TryLaunchViaScheduledTask()          │  │
│  │  │   └── TaskScheduler → PowerShell       │  │
│  │  └── LaunchViaProcessStart()              │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
└──────────────────────┼───────────────────────────┘
                       │ CreateProcessAsUser
                       ▼
┌─────────────────────────────────────────────────┐
│         Sunshine/Apollo/Vibepollo Instance       │
│         Account: SYSTEM (via SYSTEM token)       │
│         Session: Interactive console session     │
│         Window: winsta0\default                  │
│         Elevation: SYSTEM (inherently elevated)  │
│         Environment: User's environment block    │
│         Config: Per-instance sunshine.conf       │
└─────────────────────────────────────────────────┘
```

---

## Process Launch Mechanism

### Primary: CreateProcessAsUser (Service Mode)

**Source**: `Helios.Core.Process.ProcessLauncher.cs`

**Steps**:
1. `WTSGetActiveConsoleSessionId()` — Get interactive session ID
2. `WTSQueryUserToken(sessionId)` — Get user token (for environment block only)
3. `OpenProcessToken(GetCurrentProcess())` — Get SYSTEM token
4. `DuplicateTokenEx(SYSTEM, MAXIMUM_ALLOWED, TokenPrimary)` — Create primary token
5. `SetTokenInformation(TokenSessionId=sessionId)` — Assign token to user's session
6. `CreateEnvironmentBlock(userToken)` — Build user's environment variables
7. `CreateProcessAsUser(SYSTEM-in-session)` — Launch process

**Security context**:
- Process runs as SYSTEM (full privileges)
- Assigned to interactive user's session
- Has SeTcbPrivilege (can capture Winlogon desktop)
- Environment from user (correct %APPDATA%, %TEMP%, etc.)

### Fallback: Scheduled Task

**Source**: `ProcessLauncher.TryLaunchViaScheduledTask()`

**Steps**:
1. Create one-shot scheduled task
2. Task runs as current user with InteractiveToken
3. Task launches PowerShell → Start-Process
4. Wait 8s for process discovery via WMI

### Fallback: Process.Start

**Source**: `ProcessLauncher.LaunchViaProcessStart()`

- Requires manager to be elevated
- Direct Process.Start with elevated privilege
- Verifies elevation after launch

---

## Guardian Loop (Crash Recovery)

**Source**: `ProcessManager.RunGuardianLoopAsync()`

**Interval**: 5 seconds

**Behavior**:
1. Check each enabled instance
2. Detect crash (process not alive)
3. Crash backoff:
   - Crashes 1-2: Immediate restart
   - Crash 3: 30s backoff
   - Crash 4: 60s backoff
   - Crash 5+: 120s backoff
4. Reset crash counter if stable for 30s

**Residual Process Adoption**:
- Scan for Sunshine processes via WMI (`Win32_Process`)
- Match by command line (sunshine.conf path) or executable path + instance directory
- Adopt single residual process if not manually stopped
- Force-terminate duplicates (keep one per instance)
- Force-terminate non-elevated processes

---

## Configuration

**Source**: `Helios.Core.Storage.SettingsStore`

**Storage**: JSON file (`settings.json`)

**Per-instance config**:
- `Id` — Unique instance identifier
- `Name` — Display name
- `Port` — Streaming port
- `Enabled` — Auto-start flag
- `ExecutablePath` — Path to sunshine.exe
- `AppsJsonPath` — Per-instance apps.json
- `ExtraArgs` — Additional command-line arguments
- `AudioDevice` — Per-instance audio output device
- `HeadlessMode` — Headless mode flag
- `InstanceDirectory` — Per-instance data directory
- `SunshineConfPath` — Per-instance sunshine.conf

**Runtime state** (transient):
- `Pid` — Process ID
- `IsAlive` — Process alive status
- `ConsecutiveCrashCount` — Crash counter
- `CrashRestartCount` — Total crash restarts
- `ManualStopRequested` — Manual stop flag
- `NextRestartAllowedUtc` — Backoff timestamp

---

## Named Pipe IPC

**Source**: `SpawnerWorker.RunPipeServerLoopAsync()`

**Protocol**: JSON over line-delimited byte stream

**Commands**:
| Command | Input | Output | Effect |
|---------|-------|--------|--------|
| `start-all` | None | Statuses | Start all enabled instances |
| `stop-all` | None | Statuses | Stop all instances |
| `restart-instance` | InstanceId | Statuses | Restart specific instance |
| `stop-instance` | InstanceId | Statuses | Stop specific instance |
| `status` | None | Statuses | Get all instance statuses |

**Pipe Name**: `ServiceControlConstants.PipeName` (exact value not visible)

---

## Conflicting Service Management

**Source**: `SpawnerWorker.EnforceConflictingServicesDisabledAsync()`

**Behavior**:
- Detects and disables `SunshineService` and `ApolloService`
- Runs on startup and every 15 seconds
- Calls `ServiceControllerHelper.StopAndDisable()`

---

## Multi-Instance Support

### Config Isolation

Each instance gets:
- Separate `sunshine.conf` file
- Separate `apps.json` file
- Separate instance directory
- Separate port allocation
- Separate audio device assignment
- Separate credentials (TLS, paired clients)

### Clone Support

**Source**: Release v0.8.1

- Cloned instances get unique device identity (fresh TLS credentials, paired-client state)
- Prevents Moonlight from overwriting source host's pairing
- Cloned instances start in Disabled state by default

### Port Isolation

Each instance has configurable port (user sets during creation).

---

## Key Technical Details

### Elevation Verification

After launching, Helios verifies:
1. `IsProcessElevated()` — Checks `TOKEN_ELEVATION` structure
2. `HasAdministratorCapability()` — Checks `TOKEN_ELEVATION_TYPE`

If verification fails → Force terminate → Throw exception.

### WMI Process Discovery

**Source**: `ProcessManager.FindResidualInstancePids()`

Uses `ManagementObjectSearcher` with query:
```sql
SELECT ProcessId, Name, CommandLine, ExecutablePath
FROM Win32_Process
WHERE Name='sunshine.exe'
```

Matches by:
- `CommandLine` contains `sunshine.conf` path
- OR `ExecutablePath` matches AND `CommandLine` contains instance directory

### Native Windows APIs Used

| API | Purpose | Location |
|-----|---------|----------|
| `WTSGetActiveConsoleSessionId` | Get interactive session | ProcessLauncher |
| `WTSQueryUserToken` | Get user token for env block | ProcessLauncher |
| `OpenProcessToken` | Access process token | ProcessLauncher |
| `DuplicateTokenEx` | Create primary token | ProcessLauncher |
| `SetTokenInformation` | Assign token to session | ProcessLauncher |
| `CreateEnvironmentBlock` | Build user environment | ProcessLauncher |
| `CreateProcessAsUser` | Launch process in session | ProcessLauncher |
| `GetTokenInformation` | Check elevation | ProcessManager |
| `OpenProcess` | Access process for checks | ProcessManager |

---

## Limitations

1. **No session creation** — Does not create Windows sessions
2. **No display isolation** — Does not manage virtual displays
3. **No input isolation** — Does not handle input devices
4. **No RDP management** — Does not manage TermWrap/RDP
5. **No game launching** — Does not launch games
6. **Single-user only** — All instances run in same session
7. **GPLv3 license** — Cannot embed in MIT project
8. **WPF dependency** — Windows only, requires .NET 8 Desktop Runtime

---

## Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| Architecture (App → Spawner → Instances) | README + source | SpawnerWorker.cs, ProcessManager.cs | VERIFIED |
| Named Pipe IPC | Source | SpawnerWorker.RunPipeServerLoopAsync() | VERIFIED |
| CreateProcessAsUser launch | Source | ProcessLauncher.LaunchViaCreateProcessAsUser() | VERIFIED |
| SYSTEM token assignment to session | Source | SetTokenInformation(TokenSessionId) | VERIFIED |
| Guardian loop (5s) | Source | ProcessManager.RunGuardianLoopAsync() | VERIFIED |
| Crash backoff (30/60/120s) | Source | ProcessManager.CheckAndGuardAsync() | VERIFIED |
| WMI process discovery | Source | ProcessManager.FindResidualInstancePids() | VERIFIED |
| Residual process adoption | Source | ProcessManager.TryAdoptResidualRunningProcess() | VERIFIED |
| Per-instance config isolation | Source | InstanceConfig model | VERIFIED |
| Conflicting service disable | Source | SpawnerWorker.EnforceConflictingServicesDisabledAsync() | VERIFIED |
| GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| AI-assisted development | README | "Developed with assistance of AI" | VERIFIED |
