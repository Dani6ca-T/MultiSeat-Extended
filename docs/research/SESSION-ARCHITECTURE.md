# MultiSeat-Extended: Архитектура Windows Sessions

## Обзор

MultiSeat создаёт interactive Windows sessions через RDP loopback — единственный надёжный способ создать новые interactive sessions из Session 0 (Windows Service context).

## Session Creation Flow

### Кто создаёт session

`SessionLauncher.CreateSessionViaRdpLoopbackAsync()`:

```
1. Get console session ID
   Kernel32.WTSGetActiveConsoleSessionId()

2. Get console user token
   WTSQueryUserToken(consoleSessionId) → primaryConsoleToken

3. Start window hider (earliest possible)
   WindowHideHelper.WatchAndHideNew("mstsc", startedAfter, adoptSeconds)

4. Store RDP credential
   RdpCredentialStore → CredWrite (TERMSRV/127.0.0.2)

5. Write Default.rdp
   RdpFileBuilder.Build() → Default.rdp (with geometry)

6. Launch mstsc.exe in console session
   ProcessInjector.LaunchInConsoleSessionAsync(
       "mstsc.exe", "/v:127.0.0.2", ct)

7. Poll WTS until account's session appears
   FindExistingSession(accountName) → poll every 500ms

8. Launch keepalive process in new session
   ProcessInjector.LaunchInSessionAsync(sessionId, "MultiSeat.Service.exe", "--keepalive")

9. Save mstsc process
   _pendingMstsc[sessionId] = mstscProcess

10. Cleanup credentials
    RdpCredentialStore.Remove()
```

### Кто контролирует session

- **SessionLauncher** — создаёт, reconnect, disconnect, logoff
- **SeatManager** — вызывает SessionLauncher для seat lifecycle
- **SessionHealthCheck** — мониторит alive/disconnected, reconnect при sleep
- **MultiSeatWorker** — StopAsync() → TeardownAllAsync()

### Кто определяет SessionId

Windows TermService через RDP loopback. SessionLauncher poll'ит `WTSEnumerateSessions` пока не найдёт сессию для нужного аккаунта.

### Кто запускает процессы внутри session

`ProcessInjector.LaunchInSessionAsync()`:

```
1. Get session token
   SessionLauncher.GetSessionToken(sessionId, accountName)
   → WTSQueryUserToken(sessionId)
   → EnsureTokenBelongsTo(rawToken, accountName, sessionId)
   → DuplicateTokenEx → SafeTokenHandle
   → TryGetLinkedElevatedToken (for admin accounts)
   → If WTSQueryUserToken fails → LogonUser with stored credentials

2. Verify token session ID
   GetTokenInformation(TokenSessionId) → verify matches
   SetTokenInformation(TokenSessionId) if mismatch

3. Create environment block
   UserEnv.CreateEnvironmentBlock(out envBlock, token, false)

4. Build command line
   FormatCommandLine(exePath, arguments)

5. Create process
   AdvApi.CreateProcessAsUserW(
       token, cmdLine,
       lpDesktop: "WinSta0\\Default",
       dwFlags: STARTF_USESHOWWINDOW,
       wShowWindow: SW_SHOW,
       creationFlags: CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE | NORMAL_PRIORITY_CLASS)

6. Verify landed in session
   Kernel32.ProcessIdToSessionId(pid, out actualSessionId)
   If wrong session → Kill + throw

7. Wait for startup
   WaitForSingleObject(hProcess, 2000)
```

## RDP Loopback Mechanism

### Почему RDP loopback

- `CreateProcessAsUser` alone does NOT create new sessions from Session 0
- It always launches processes in Session 0 itself
- RDP protocol is required to trigger session creation via termsrv.dll

### Адрес loopback

```csharp
private const string RdpLoopbackAddress = "127.0.0.2";
// Используем 127.0.0.2 чтобы избежать конфликтов с 127.0.0.1/localhost
```

### RDP Wrapper / TermWrap

```csharp
// RdpWrapper.EnsureMultiSession()
// Проверяет termsrv.dll multi-session patch
// Без этого: существующий console session user будет отключён
```

### Default.rdp

```csharp
// RdpFileBuilder.Build(accountName, geometry)
// Генерирует RDP файл с:
// - full address:s:127.0.0.2
// -audiomode:i:0 (play on client — PerSession)
// - Session geometry (width x height)
// - Security hints (NLA off, cert warnings suppressed)
```

## Session States

```csharp
enum WtsConnectState {
    Active,       // Session is connected and interactive
    Connected,    // Session is connected but not interactive
    ConnectQuery, // Session is in the process of connecting
    Shadow,       // Session is being shadowed
    Disconnected, // Session was active but is now disconnected
    Idle,         // Session is idle
    Listening,    // Session is listening for connections
    Reset,        // Session is being reset
    Down,         // Session is down
    Init          // Session is initializing
}
```

### Session Lifecycle

```
Created (RDP loopback) → Active (mstsc connected)
    ↓ DisconnectSession() (kill mstsc)
Active → Disconnected (processes still running)
    ↓ ReconnectSessionAsync() (new mstsc)
Disconnected → Active
    ↓ LogoffSession()
Active/Disconnected → (terminated)
```

## Session Monitoring

### IsSessionAlive()

```csharp
// SessionLauncher.IsSessionAlive(sessionId)
var wtsAlive = FindSessionState(sessionId) is Active or Disconnected;
if (!wtsAlive) return false;

// Check keepalive process
if (_keepalives.TryGetValue(sessionId, out var keepalive) && keepalive.Process.HasExited)
    _logger.LogWarning("Keepalive process for session {Sid} exited");

return true;
```

### IsSessionActive()

```csharp
// SessionLauncher.IsSessionActive(sessionId)
return FindSessionState(sessionId) == WtsApi.WtsConnectState.Active;
```

### Sleep/Wake Handling

```csharp
// SessionHealthCheck.CheckSeatAsync()
if (!_sessionLauncher.IsSessionActive(seat.SessionId))
{
    // Session is Disconnected (PC may have slept)
    _vibepolloManager.KillForReconnect(seat);
    await _sessionLauncher.LaunchSessionAsync(seat.AccountName, ct, geometry);
    await Task.Delay(2000, ct);  // Let display pipeline reinitialize
    var newPid = await _vibepolloManager.RestartAsync(seat, ct);
    await _seatManager.ApplyDisplayIsolationAsync(seat, ct);
}
```

## Session Token Handling

### Token Acquisition

```
Path 1: WTSQueryUserToken(sessionId)
    → Returns filtered (medium-integrity) token for admin accounts
    → For standard users: filtered token is used as-is
    → Verify: EnsureTokenBelongsTo(rawToken, accountName, sessionId)
    → DuplicateTokenEx → primary token
    → TryGetLinkedElevatedToken (for admin accounts)

Path 2: LogonUser with stored credentials
    → Fallback when WTSQueryUserToken fails
    → Returns primary token
```

### Token Verification

```csharp
// ProcessInjector.VerifyLandedInSession()
Kernel32.ProcessIdToSessionId(pid, out actualSessionId);
if (actualSessionId != expectedSessionId)
{
    // Kill the mis-targeted process
    Kernel32.TerminateProcess(hProcess, 1);
    throw new InvalidOperationException("...wrong session...");
}
```

## Key Session Behaviors

### Console Session Isolation

- Seats NEVER live in the console session
- `ProcessInjector.EnsureNotConsoleSession()` refuses console launches
- Console session is for host user only

### Session Persistence

- Windows Session ID preserved across reconnect (mstsc reconnects to same session)
- Anything running in the seat survives reconnect
- Resolution change requires new session (mstsc sets geometry at connect)

### Display Pipeline Access

- Active session required for QueryDisplayConfig / DXGI
- Disconnected sessions return ERROR_ACCESS_DENIED
- Vibepollo needs active session for SudoVDA IPC

### Keepalive Process

- Launched in seat session to anchor it
- `MultiSeat.Service.exe --keepalive` (infinite loop)
- Session dies if keepalive + all processes exit

### mstsc Window Management

- mstsc launched in console session
- Window hidden via WindowHideHelper (SW_HIDE)
- Window re-shows on connect/reconnect/resolution change
- WatchAndHideNew() monitors for new mstsc processes

## Limitations

### RDP Wrapper Dependency

- termsrv.dll must be patched for concurrent sessions
- Without RDP Wrap: existing console user gets disconnected
- RDPWrap breaks after Windows updates to termsrv.dll

### Session Resolution

- Fixed at mstsc connect time
- Cannot change from inside the session
- ChangeDisplaySettingsEx returns success but does nothing
- Resolution change = disconnect + reconnect with new geometry

### DXGI Access

- RDP sessions cannot access DXGI directly
- QueryDisplayConfig returns ACCESS_DENIED for disconnected sessions
- SudoVDA IPC requires active session

### Single GPU

- Multi-GPU not tested
- NVIDIA consumer GPUs: 3-5 concurrent NVENC sessions max
