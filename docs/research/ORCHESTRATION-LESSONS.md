# Orchestration Lessons

**Date**: 2026-08-30
**Purpose**: Extract reusable orchestration patterns from Helios source code

---

## Source: Helios ProcessLauncher.cs

### Lesson 1: SYSTEM Token → User Session Pattern

**Pattern**: Launch process as SYSTEM, assign to user's interactive session

**Steps**:
1. `WTSGetActiveConsoleSessionId()` → get interactive session
2. `WTSQueryUserToken(session)` → get user token (for env block only)
3. `OpenProcessToken(GetCurrentProcess())` → get SYSTEM token
4. `DuplicateTokenEx(SYSTEM, MAXIMUM_ALLOWED, TokenPrimary)` → create primary token
5. `SetTokenInformation(token, TokenSessionId, sessionId)` → assign to session
6. `CreateEnvironmentBlock(userToken)` → build user environment
7. `CreateProcessAsUser(SYSTEM-in-session)` → launch

**Key insight**: The SYSTEM token provides SeTcbPrivilege, enabling capture of the Winlogon (secure) desktop — same capability as standard Sunshine-as-a-service.

**Application to MultiSeat-Extended**: Already implemented in SessionLauncher. Pattern confirmed correct.

---

### Lesson 2: Elevation Verification After Launch

**Pattern**: Verify process elevation after launch, terminate if not elevated

**Implementation**:
```csharp
if (!IsProcessElevated(pid)) {
    GracefulShutdown.ForceTerminate(pid);
    throw new InvalidOperationException("Non-elevated process");
}
if (!HasAdministratorCapability(pid)) {
    GracefulShutdown.ForceTerminate(pid);
    throw new InvalidOperationException("No admin capability");
}
```

**Key insight**: SYSTEM token is inherently elevated, but verification catches edge cases.

**Application to MultiSeat-Extended**: Could add verification after ProcessInjector.LaunchInSessionAsync.

---

## Source: Helios ProcessManager.cs

### Lesson 3: Guardian Loop with Progressive Backoff

**Pattern**: 5s health check with progressive crash backoff

**Backoff schedule**:
- Crashes 1-2: Immediate restart
- Crash 3: 30s backoff
- Crash 4: 60s backoff
- Crash 5+: 120s backoff
- Reset if stable for 30s

**Application to MultiSeat-Extended**: MultiSeat has MaxRestartAttempts = 3 but no progressive backoff. Could adopt Helios's pattern.

---

### Lesson 4: Residual Process Adoption

**Pattern**: Scan for orphaned processes via WMI, adopt single residual

**Implementation**:
1. Query `Win32_Process WHERE Name='sunshine.exe'`
2. Match by command line (sunshine.conf path) or executable path + instance directory
3. Verify elevation (must be elevated)
4. Adopt single residual process
5. Force-terminate duplicates
6. Force-terminate non-elevated processes

**Application to MultiSeat-Extended**: Could detect orphaned Vibepollo processes after crash and adopt them instead of starting new ones.

---

### Lesson 5: Single Process Constraint

**Pattern**: Ensure only one process per instance

**Implementation**:
- Find all alive PIDs matching instance
- Keep one (prefer tracked PID)
- Force-terminate extras
- Log warning about duplicates

**Application to MultiSeat-Extended**: Could prevent multiple Vibepollo instances per seat.

---

### Lesson 6: Manual Stop Respects Intent

**Pattern**: If user requested stop, don't adopt residual processes

**Implementation**:
```csharp
if (state.ManualStopRequested) {
    GracefulShutdown.ForceTerminate(adoptedPid);
    return false; // don't adopt
}
```

**Application to MultiSeat-Extended**: When user stops Vibepollo via API, don't let guardian loop restart it.

---

## Source: Helios SpawnerWorker.cs

### Lesson 7: Named Pipe IPC Pattern

**Pattern**: JSON over line-delimited byte stream for service ↔ app communication

**Commands**: start-all, stop-all, restart-instance, stop-instance, status

**Application to MultiSeat-Extended**: MultiSeat uses direct method calls (no IPC needed — single process). Not directly applicable, but the pattern is clean for future service/app separation.

---

### Lesson 8: Conflicting Service Disable

**Pattern**: Detect and disable services that conflict with the manager

**Implementation**:
- Check `SunshineService`, `ApolloService`
- `ServiceControllerHelper.StopAndDisable()`
- Run on startup + every 15 seconds

**Application to MultiSeat-Extended**: Could detect conflicting Sunshine instances running outside MultiSeat's control.

---

## Source: Helios InstanceConfig

### Lesson 9: Clone Instance with Unique Identity

**Pattern**: Cloned instances get fresh TLS credentials and paired-client state

**Implementation** (v0.8.1):
- Generate new device identity for clone
- Fresh certificates
- Fresh paired clients
- Start in Disabled state

**Application to MultiSeat-Extended**: When duplicating seat presets, generate fresh Vibepollo state.

---

### Lesson 10: Disabled State Prevents Auto-Start

**Pattern**: New instances start disabled to prevent unintended auto-start

**Application to MultiSeat-Extended**: Could add SeatPreset.Enabled flag for auto-start control.

---

## Synthesis: What MultiSeat-Extended Should Adopt

| Pattern | Source | Priority | Effort |
|---------|--------|----------|--------|
| Progressive crash backoff | Helios ProcessManager | P1 | LOW |
| Residual process adoption | Helios ProcessManager | P1 | MEDIUM |
| Single process constraint | Helios ProcessManager | P1 | LOW |
| Elevation verification | Helios ProcessLauncher | P2 | LOW |
| Manual stop intent | Helios ProcessManager | P2 | LOW |
| Clone instance identity | Helios v0.8.1 | P3 | LOW |
| Conflicting service detection | Helios SpawnerWorker | P3 | LOW |
| Disabled state for new instances | Helios v0.8.1 | P3 | LOW |

---

## Evidence

| Claim | Source | File | Status |
|-------|--------|------|--------|
| SYSTEM token → session pattern | Helios | ProcessLauncher.LaunchViaCreateProcessAsUser() | VERIFIED |
| Elevation verification | Helios | ProcessLauncher.IsProcessElevated() | VERIFIED |
| Guardian loop (5s) | Helios | ProcessManager.RunGuardianLoopAsync() | VERIFIED |
| Progressive backoff (30/60/120s) | Helios | ProcessManager.CheckAndGuardAsync() | VERIFIED |
| WMI residual process discovery | Helios | ProcessManager.FindResidualInstancePids() | VERIFIED |
| Single process constraint | Helios | ProcessManager.EnforceSingleProcessConstraintAsync() | VERIFIED |
| Manual stop intent | Helios | ProcessManager.CheckAndGuardAsync() | VERIFIED |
| Named Pipe IPC | Helios | SpawnerWorker.RunPipeServerLoopAsync() | VERIFIED |
| Conflicting service disable | Helios | SpawnerWorker.EnforceConflictingServicesDisabledAsync() | VERIFIED |
| Clone identity | Helios v0.8.1 | Release notes | VERIFIED |
