# Process Architecture

**Date**: 2026-08-30
**Purpose**: Define the ideal process architecture for MultiSeat-Extended

---

## Current Architecture

```
MultiSeat.Service (SYSTEM)
    │
    ├── AccountManager
    │       └── Windows user accounts
    │
    ├── SessionLauncher
    │       └── CreateProcessAsUser → mstsc (RDP loopback)
    │
    ├── VirtualDisplayManager
    │       └── SudoVDA IPC
    │
    ├── VibepolloManager
    │       └── sunshine.exe (per-seat)
    │
    ├── ProcessInjector
    │       └── CreateProcessAsUser → game.exe
    │
    ├── HidHideConfigurator
    │       └── HidHideCLI.exe
    │
    └── SessionHealthCheck
            └── 5s interval monitoring
```

**Known issues**:
1. No process tracking (PID → Seat mapping)
2. No Job Object isolation
3. No residual process adoption
4. No progressive crash backoff
5. Teardown is best-effort (orphan processes possible)

---

## Target Architecture

```
MultiSeat.Service (SYSTEM)
    │
    ├── SeatManager
    │       └── Orchestrates all subsystems
    │
    ├── AccountManager
    │       └── Windows user accounts
    │
    ├── SessionManager
    │       ├── CreateProcessAsUser → mstsc (RDP loopback)
    │       ├── Token management (DuplicateTokenEx, SetTokenInformation)
    │       └── Session monitoring
    │
    ├── DisplayManager
    │       ├── SudoVDA IPC (create/destroy)
    │       ├── Display isolation (primary + shrunk)
    │       └── Refresh rate clamping
    │
    ├── ProviderManager
    │       ├── IStreamingProvider interface
    │       ├── VibepolloAdapter (current)
    │       ├── ApolloAdapter (future)
    │       └── Provider health monitoring
    │
    ├── ProcessTracker
    │       ├── PID → Seat mapping
    │       ├── Job Object isolation
    │       ├── Residual process adoption
    │       └── Process tree cleanup
    │
    ├── InputManager
    │       ├── HidHide session jail
    │       └── Controller routing (optional)
    │
    ├── HealthMonitor
    │       ├── Guardian loop (5s)
    │       ├── Progressive crash backoff
    │       ├── Display re-detection
    │       └── Full seat re-provision
    │
    └── SecurityManager
            ├── DPAPI credentials
            ├── ACL permissions
            └── API key authentication
```

---

## Process Launch Pattern

### Service → Session Process

```
MultiSeat.Service (SYSTEM, Session 0)
    │
    │ WTSGetActiveConsoleSessionId()
    │ WTSQueryUserToken(sessionId)
    │ OpenProcessToken(GetCurrentProcess())
    │ DuplicateTokenEx(SYSTEM, TokenPrimary)
    │ SetTokenInformation(TokenSessionId=sessionId)
    │ CreateEnvironmentBlock(userToken)
    │
    ▼
CreateProcessAsUser(SYSTEM-in-session)
    │
    ├── mstsc.exe (RDP loopback → session)
    ├── sunshine.exe (Vibepollo → session)
    ├── game.exe (ProcessInjector → session)
    └── helper.exe (display isolation → session)
```

### Key Properties

1. **SYSTEM token** provides SeTcbPrivilege (can capture Winlogon desktop)
2. **Assigned to user session** via SetTokenInformation
3. **User environment** via CreateEnvironmentBlock
4. **Elevation verified** after launch (IsProcessElevated)
5. **Job Object** isolates process tree (TARGET)

---

## Process Tracking Pattern

### PID → Seat Mapping

```
Dictionary<Guid, SeatProcessInfo> _processes;

class SeatProcessInfo {
    Guid SeatId;
    int VibepolloPid;
    List<int> GamePids;
    IntPtr JobHandle;  // Job Object
}
```

### Job Object Isolation

```csharp
var jobHandle = CreateJobObject();
var limits = new JOBOBJECT_BASIC_LIMIT_INFORMATION {
    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
};
SetInformationJobObject(jobHandle, JobObjectInfoType, ref limits);
AssignProcessToJobObject(jobHandle, processHandle);
```

**Effect**: When Job Object handle is closed, all processes in the job are terminated.

---

## Residual Process Adoption Pattern

### Detection

```sql
SELECT ProcessId, Name, CommandLine, ExecutablePath
FROM Win32_Process
WHERE Name='sunshine.exe'
```

### Matching

- CommandLine contains sunshine.conf path
- OR ExecutablePath matches AND CommandLine contains instance directory

### Adoption Logic

1. Find all alive PIDs matching instance
2. Verify elevation (must be elevated)
3. Adopt single residual process
4. Force-terminate duplicates
5. Force-terminate non-elevated processes

---

## Progressive Crash Backoff

### Schedule

| Crash Count | Backoff | Reset Condition |
|-------------|---------|-----------------|
| 1-2 | Immediate | Stable for 30s |
| 3 | 30 seconds | Stable for 30s |
| 4 | 60 seconds | Stable for 30s |
| 5+ | 120 seconds | Stable for 30s |

### Implementation

```csharp
int backoffSeconds = consecutiveCrashCount switch {
    <= 2 => 0,
    3 => 30,
    4 => 60,
    _ => 120
};

if (backoffSeconds > 0) {
    state.NextRestartAllowedUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);
    return; // wait
}
```

---

## Teardown Pattern

### Reverse Order with Job Object Cleanup

```
1. Kill launched apps (OnConnectApps)
2. Uninstall input hooks
3. Uncloak HidHide
4. Unassign controllers
5. Destroy controllers
6. Stop Vibepollo
7. Close Job Object (kills all processes in job)
8. Close firewall ports
9. Destroy display
10. Disconnect session
11. Logoff session
12. Release ports
13. Cleanup config
```

### Key Improvement: Job Object

Adding Job Object at step 7 ensures ALL processes in the seat are terminated, including orphaned game processes.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Current architecture lacks process tracking | Codebase search | VERIFIED (absent) |
| Current architecture lacks Job Objects | Codebase search | VERIFIED (absent) |
| Helios has residual process adoption | ProcessManager.cs | VERIFIED |
| Helios has progressive backoff | ProcessManager.cs | VERIFIED |
| Helios has elevation verification | ProcessLauncher.cs | VERIFIED |
| CreateProcessAsUser pattern is correct | ProcessLauncher.cs | VERIFIED |
| Job Object kills process tree | Windows API documentation | VERIFIED |
