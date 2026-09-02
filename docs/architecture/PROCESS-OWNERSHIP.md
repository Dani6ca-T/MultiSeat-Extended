# Process Ownership

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define process ownership, lifecycle, and cleanup for all managed processes.

---

## Process Hierarchy

```
MultiSeat.Service (SYSTEM, Session 0)
    │
    ├── SeatManager
    │       │
    │       ├── SessionLauncher
    │       │       └── mstsc.exe (per seat)
    │       │
    │       ├── VibepolloManager
    │       │       └── sunshine.exe (per seat)
    │       │
    │       └── ProcessInjector
    │               └── game.exe (per seat)
    │
    └── Helper processes
            └── MultiSeat.Service.exe --setup-display-isolation
```

---

## Ownership Rules

### Rule 1: Every Process Has an Owner

| Process | Owner | SeatId |
|---------|-------|--------|
| MultiSeat.Service.exe | System | N/A |
| mstsc.exe | Seat | Seat's SessionId |
| sunshine.exe | Seat | Seat's VibepolloProcessId |
| game.exe | Seat | Seat's launched games |
| helper.exe | Seat | Seat's display isolation |

**FACT**: Current implementation tracks VibepolloProcessId per seat.

---

### Rule 2: Owner is Responsible for Cleanup

When a Seat is torn down, ALL processes owned by that Seat must be terminated.

### Rule 3: Orphan Detection

Processes without an owner are orphans and must be adopted or terminated.

---

## Process Launch Pattern

### CreateProcessAsUser

```csharp
// 1. Get interactive session
uint sessionId = WTSGetActiveConsoleSessionId();

// 2. Get user token (for environment block)
WTSQueryUserToken(sessionId, out hUserToken);

// 3. Get SYSTEM token
OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out hSystemToken);

// 4. Duplicate as primary token
DuplicateTokenEx(hSystemToken, MAXIMUM_ALLOWED, TokenPrimary, out hSystemTokenDup);

// 5. Assign to user session
SetTokenInformation(hSystemTokenDup, TokenSessionId, ref sessionId);

// 6. Build user environment
CreateEnvironmentBlock(out hEnvironment, hUserToken, false);

// 7. Launch process
CreateProcessAsUser(hSystemTokenDup, executable, args, ..., hEnvironment, ...);
```

**FACT**: Helios ProcessLauncher uses this exact pattern.

---

## Process Tracking

### PID → Seat Mapping

```csharp
Dictionary<int, Guid> _pidToSeat;  // PID → SeatId
Dictionary<Guid, HashSet<int>> _seatToPids;  // SeatId → PIDs
```

### Tracking Points

| Event | Action |
|-------|--------|
| Provider started | Add PID to mapping |
| Game launched | Add PID to mapping |
| Process exited | Remove PID from mapping |
| Seat torn down | Remove all PIDs for seat |

**INFERENCE**: Based on Helios ProcessManager pattern.

---

## Process Cleanup

### Graceful Shutdown

```
1. Send close message (WM_CLOSE)
2. Wait for timeout (8 seconds)
3. Check if process exited
4. If not, force terminate (TerminateProcess)
```

**FACT**: Helios GracefulShutdown uses this pattern.

### Force Termination

```
1. TerminateProcess(hProcess, EXIT_CODE)
2. WaitForSingleObject(hProcess, TIMEOUT)
3. CloseHandle(hProcess)
```

### Job Object Cleanup

```
1. Close Job Object handle
2. JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE terminates all processes
```

**INFERENCE**: Job Objects guarantee cleanup.

---

## Residual Process Adoption

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

**FACT**: Helios ProcessManager.FindResidualInstancePids uses WMI.

---

## Single Process Constraint

### Rule

Only one provider process per Seat at any time.

### Enforcement

1. On provider start, scan for existing processes
2. If found, adopt or terminate
3. Launch new process
4. Verify only one alive

**FACT**: Helios ProcessManager.EnforceSingleProcessConstraintAsync.

---

## Process Tree

### Provider Process Tree

```
sunshine.exe (PID 1234)
├── encoder thread
├── capture thread
├── audio thread
└── network thread
```

### Game Process Tree

```
game.exe (PID 5678)
├── renderer
├── physics
├── audio
└── network
```

### Cleanup Strategy

- **Provider**: Kill sunshine.exe → all threads die
- **Game**: Kill game.exe → all child processes die
- **Job Object**: Close handle → all processes in job die

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| CreateProcessAsUser pattern | Helios ProcessLauncher.cs | FACT |
| WMI process discovery | Helios ProcessManager.cs | FACT |
| GracefulShutdown pattern | Helios GracefulShutdown.cs | FACT |
| Single process constraint | Helios ProcessManager.cs | FACT |
| Job Object kills process tree | Windows API documentation | FACT |
