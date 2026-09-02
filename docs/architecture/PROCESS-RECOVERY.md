# Process Recovery

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define crash detection, backoff, restart limits, and orphan handling.

---

## Crash Detection

### Provider Crash

| Detection Method | Interval | Evidence |
|-----------------|----------|----------|
| Process alive check | 5s | SessionHealthCheck |
| HTTP health check | 5s | VibepolloServerQuery |
| WMI process scan | On demand | Helios pattern |

### Game Crash

| Detection Method | Interval | Evidence |
|-----------------|----------|----------|
| Process exit monitoring | On demand | Process.GetProcessById |
| Exit code check | On exit | Exit code != 0 = crash |

**DECISION**: Game crash detection is P1 priority.

---

## Backoff Policy

### Progressive Backoff

| Crash Count | Backoff | Reset Condition |
|-------------|---------|-----------------|
| 1-2 | Immediate | Stable for 30s |
| 3 | 30 seconds | Stable for 30s |
| 4 | 60 seconds | Stable for 30s |
| 5+ | 120 seconds | Stable for 30s |

### Implementation

```csharp
int backoffSeconds = consecutiveCrashCount switch
{
    <= 2 => 0,
    3 => 30,
    4 => 60,
    _ => 120
};

if (backoffSeconds > 0)
{
    state.NextRestartAllowedUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);
    return; // wait
}
```

**FACT**: Helios ProcessManager uses this pattern.

---

## Restart Limits

### Maximum Restarts

| Limit | Value | Source |
|-------|-------|--------|
| MaxRestartAttempts | 3 | Constants.cs |
| Consecutive crash threshold | 3 | Helios pattern |
| Stable period | 30 seconds | Helios pattern |

### Behavior

```
Crash 1 → Immediate restart
Crash 2 → Immediate restart
Crash 3 → 30s backoff, restart
Crash 4 → 60s backoff, restart
Crash 5 → 120s backoff, restart
Crash 6 → Enter Failed state (MaxRestartAttempts exceeded)
```

**DECISION**: After MaxRestartAttempts, seat enters Failed state.

---

## Orphan Detection

### Detection Method

```sql
SELECT ProcessId, Name, CommandLine, ExecutablePath
FROM Win32_Process
WHERE Name='sunshine.exe'
```

### Matching Rules

1. CommandLine contains sunshine.conf path
2. OR ExecutablePath matches AND CommandLine contains instance directory

### Adoption Logic

```csharp
var residualPids = FindResidualInstancePids(instance);
var alivePids = residualPids.Where(IsAlive).ToList();

if (alivePids.Count == 0) return; // no orphans
if (alivePids.Count == 1) Adopt(alivePids[0]); // single orphan
if (alivePids.Count > 1) TerminateExtras(alivePids); // duplicates
```

**FACT**: Helios ProcessManager.FindResidualInstancePids uses WMI.

---

## Residual Process Adoption

### When to Adopt

| Scenario | Adopt? | Reason |
|----------|--------|--------|
| Provider crash + restart | Yes | Existing process may still work |
| Seat teardown | No | Kill all processes |
| Manual stop | No | User intent is to stop |

### Adoption Flow

```
1. Scan for residual processes (WMI)
2. Match by config path or executable + directory
3. Verify elevation (must be elevated)
4. If single residual → adopt
5. If multiple residuals → keep one, kill others
6. If non-elevated → kill
7. Update PID tracking
```

**FACT**: Helios ProcessManager.TryAdoptResidualRunningProcess.

---

## Recovery Scenarios

### Provider Crash

```
1. Health check detects failure
2. Mark seat as Degraded
3. Apply backoff (progressive)
4. Restart provider
5. Verify health
6. If success → mark Ready
7. If failure → increment crash count
8. If MaxRestartAttempts → mark Failed
```

### Game Crash

```
1. Process exit detected
2. Log crash (exit code)
3. Optionally restart game (configurable)
4. Update game process tracking
```

### Session Disconnect

```
1. Health check detects disconnect
2. Attempt reconnect (mstsc)
3. If success → resume monitoring
4. If failure → log warning, continue
```

### Display Lost

```
1. Health check detects no display UUID
2. Re-detect display (TryLateDisplayDetectionAsync)
3. If found → apply display isolation
4. If not found → continue monitoring
```

---

## Recovery Priority

| Failure | Detection | Recovery | Current Status |
|---------|-----------|----------|----------------|
| Provider crash | ✅ 5s health | ✅ Auto-restart | COMPLETE |
| Game crash | ❌ None | ❌ None | P1 TARGET |
| Session disconnect | ✅ Health | ⚠️ Reconnect | PARTIAL |
| Display lost | ✅ Health | ✅ Late detection | COMPLETE |
| Orphan process | ❌ None | ❌ None | P1 TARGET |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Progressive backoff pattern | Helios ProcessManager | FACT |
| MaxRestartAttempts = 3 | Constants.cs | FACT |
| WMI process discovery | Helios ProcessManager | FACT |
| TryLateDisplayDetectionAsync | SeatManager.cs | FACT |
| Game crash detection missing | Codebase search | FACT (absent) |
