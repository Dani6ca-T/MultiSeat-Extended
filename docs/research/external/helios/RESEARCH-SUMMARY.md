# Helios Research Summary

**Date**: 2026-08-30
**Purpose**: Complete research summary for Helios-Sunshine-Manager

---

## 1. What Is Helios

Helios is a Windows multi-instance manager for Sunshine and its forks (Apollo, Vibeshine, Vibepollo). It allows running multiple independent streaming instances on a single Windows machine, each with its own port, configuration, and credentials.

**Key insight**: Helios is NOT a multiseat platform. It manages multiple streaming instances but does NOT create Windows sessions, manage displays, handle input, or isolate audio. It is a streaming instance orchestrator.

---

## 2. Architecture

**Three-layer architecture**:

```
Helios.App (WPF UI)
    ↓ Named Pipe IPC
Helios.Spawner (Windows Service, SYSTEM)
    ↓ CreateProcessAsUser
Sunshine/Apollo/Vibepollo instances
```

- **Helios.App** — WPF desktop application (UI + control)
- **Helios.Core** — Shared library (process management, config, audio, display, updates)
- **Helios.Spawner** — Windows Service (SYSTEM, launches instances via Named Pipe commands)

---

## 3. Process Launch Mechanism

**Primary method**: `CreateProcessAsUser` with SYSTEM token

**Steps**:
1. Get interactive session ID via `WTSGetActiveConsoleSessionId`
2. Get user token via `WTSQueryUserToken` (for environment block)
3. Duplicate SYSTEM token as primary token
4. Assign SYSTEM token to user's session via `SetTokenInformation`
5. Build user's environment block via `CreateEnvironmentBlock`
6. Launch process via `CreateProcessAsUser`

**Security implications**:
- Process runs as SYSTEM (full privileges)
- Assigned to interactive user's session
- Has SeTcbPrivilege (can capture Winlogon desktop)
- Environment from user (correct %APPDATA%, etc.)

**Fallback methods**:
- Scheduled Task (one-shot, PowerShell → Start-Process)
- Process.Start (requires manager to be elevated)

---

## 4. Guardian Loop (Crash Recovery)

**Interval**: 5 seconds

**Behavior**:
- Check each enabled instance
- Detect crash (process not alive)
- Crash backoff:
  - Crashes 1-2: Immediate restart
  - Crash 3: 30s backoff
  - Crash 4: 60s backoff
  - Crash 5+: 120s backoff
- Reset crash counter if stable for 30s

**Residual Process Adoption**:
- Scan for Sunshine processes via WMI
- Match by command line or executable path + instance directory
- Adopt single residual process
- Force-terminate duplicates
- Force-terminate non-elevated processes

---

## 5. Configuration

**Storage**: JSON file (`settings.json`)

**Per-instance config**:
- Instance ID, name, port
- Enabled flag
- Executable path
- Apps.json path
- Extra arguments
- Audio device
- Headless mode
- Instance directory
- Sunshine.conf path

**Runtime state** (transient):
- PID, alive status
- Crash counters
- Manual stop flag
- Backoff timestamp

---

## 6. Named Pipe IPC

**Protocol**: JSON over line-delimited byte stream

**Commands**:
- `start-all` — Start all enabled instances
- `stop-all` — Stop all instances
- `restart-instance` — Restart specific instance
- `stop-instance` — Stop specific instance
- `status` — Get all instance statuses

---

## 7. Multi-Instance Support

**Config isolation**:
- Separate sunshine.conf per instance
- Separate apps.json per instance
- Separate instance directory
- Separate port allocation
- Separate audio device assignment
- Separate credentials (TLS, paired clients)

**Clone support**:
- Cloned instances get unique device identity
- Prevents Moonlight pairing conflicts
- Cloned instances start disabled by default

---

## 8. Conflicting Service Management

**Behavior**:
- Detects and disables `SunshineService` and `ApolloService`
- Runs on startup and every 15 seconds
- Calls `ServiceControllerHelper.StopAndDisable()`

---

## 9. Key Technical Details

### Elevation Verification

After launching, Helios verifies:
1. `IsProcessElevated()` — Checks TOKEN_ELEVATION
2. `HasAdministratorCapability()` — Checks TOKEN_ELEVATION_TYPE

If verification fails → Force terminate → Throw exception.

### WMI Process Discovery

Query:
```sql
SELECT ProcessId, Name, CommandLine, ExecutablePath
FROM Win32_Process
WHERE Name='sunshine.exe'
```

Matches by:
- CommandLine contains sunshine.conf path
- OR ExecutablePath matches AND CommandLine contains instance directory

### Native Windows APIs Used

| API | Purpose |
|-----|---------|
| WTSGetActiveConsoleSessionId | Get interactive session |
| WTSQueryUserToken | Get user token for env block |
| OpenProcessToken | Access process token |
| DuplicateTokenEx | Create primary token |
| SetTokenInformation | Assign token to session |
| CreateEnvironmentBlock | Build user environment |
| CreateProcessAsUser | Launch process in session |
| GetTokenInformation | Check elevation |
| OpenProcess | Access process for checks |

---

## 10. Limitations

1. **No session creation** — Does not create Windows sessions
2. **No display isolation** — Does not manage virtual displays
3. **No input isolation** — Does not handle input devices
4. **No RDP management** — Does not manage TermWrap/RDP
5. **No game launching** — Does not launch games
6. **Single-user only** — All instances run in same session
7. **GPLv3 license** — Cannot embed in MIT project
8. **WPF dependency** — Windows only, requires .NET 8 Desktop Runtime

---

## 11. What MultiSeat-Extended Should Learn

### Named Pipe IPC Pattern
- Clean separation of UI and privileged operations
- JSON protocol over line-delimited pipe
- Could inspire IStreamingProvider abstraction

### Per-Instance Config Isolation
- Separate config directory per instance
- Separate port allocation
- Separate audio device assignment
- Already implemented in MultiSeat-Extended

### Guardian Loop Pattern
- 5s interval health check
- Crash backoff with progressive delays
- Residual process adoption
- Already implemented in MultiSeat-Extended (SessionHealthCheck)

### CreateProcessAsUser with SYSTEM Token
- SYSTEM token assigned to user's session
- Environment block from user token
- Elevation verification after launch
- Already implemented in MultiSeat-Extended (SessionLauncher)

---

## 12. What MultiSeat-Extended Should NOT Adopt

### GPLv3 Code
- Cannot link into MIT project
- Must keep as external process

### WPF UI
- MultiSeat uses React
- Different technology stack

### No Session Management
- Helios doesn't create Windows sessions
- MultiSeat-Extended does (RDP loopback)

### No Display Isolation
- Helios doesn't manage virtual displays
- MultiSeat-Extended does (SudoVDA)

### No Input Isolation
- Helios doesn't handle input devices
- MultiSeat-Extended does (HidHide session jail)

---

## 13. Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| WPF + Service architecture | README + source | SpawnerWorker.cs | VERIFIED |
| Named Pipe IPC | Source | SpawnerWorker.RunPipeServerLoopAsync() | VERIFIED |
| CreateProcessAsUser launch | Source | ProcessLauncher.LaunchViaCreateProcessAsUser() | VERIFIED |
| SYSTEM token to session | Source | SetTokenInformation(TokenSessionId) | VERIFIED |
| Guardian loop (5s) | Source | ProcessManager.RunGuardianLoopAsync() | VERIFIED |
| Crash backoff (30/60/120s) | Source | ProcessManager.CheckAndGuardAsync() | VERIFIED |
| WMI process discovery | Source | ProcessManager.FindResidualInstancePids() | VERIFIED |
| Residual process adoption | Source | ProcessManager.TryAdoptResidualRunningProcess() | VERIFIED |
| Per-instance config isolation | Source | InstanceConfig model | VERIFIED |
| Conflicting service disable | Source | SpawnerWorker.EnforceConflictingServicesDisabledAsync() | VERIFIED |
| GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| AI-assisted development | README | "Developed with assistance of AI" | VERIFIED |
| Clone support with unique identity | Release v0.8.1 | Release notes | VERIFIED |
| Audio device assignment | README | "Per-Instance Audio Routing" | VERIFIED |
| Headless mode support | README | "headless mode" | VERIFIED |
