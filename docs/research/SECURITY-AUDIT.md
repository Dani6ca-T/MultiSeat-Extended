# MultiSeat-Extended: Аудит безопасности

## Обзор

MultiSeat running as SYSTEM — полный контроль над хостом. Каждый security decision критичен.

## Credential Storage

### Account Passwords

```csharp
// AccountManager — DPAPI CurrentUser scope (SYSTEM)
private void SavePersistedAccounts()
{
    var encrypted = ProtectedData.Protect(
        Encoding.UTF8.GetBytes(kv.Value),
        null, DataProtectionScope.CurrentUser);
    // CurrentUser under SYSTEM = SYSTEM's master key
    // Only SYSTEM on this machine can decrypt
}

// Storage: C:\ProgramData\MultiSeat\accounts.json
// ACL: hardened to SYSTEM + Administrators only
HardenStore(tmp);  // Strip BUILTIN\Users grant
```

**Потенциальные проблемы:**
- **MEDIUM**: Administrator can become SYSTEM → read all credentials
- **LOW**: Service reconfig to different account → credentials undecryptable

### API Key

```csharp
// ApiServer.ResolveApiKey()
var keyFile = @"C:\ProgramData\MultiSeat\api-key.txt";
// Generated: URL-safe 32-char random key
// ACL: hardened to SYSTEM + Administrators
HardenKeyFile(keyFile, log);
```

**Потенциальные проблемы:**
- **HIGH**: API is plaintext HTTP — key crosses network in clear when bound beyond loopback
- **MEDIUM**: No HTTPS support at all

### RDP Credentials

```csharp
// RdpCredentialStore — CredWrite/CredRead
// Stored in console user's credential store
// Cleaned up after session creation
```

**Потенциальные проблемы:**
- **LOW**: Brief window during session creation where credential is in console user's store

## Service Execution

### SYSTEM privileges

- Service runs as SYSTEM (`AddWindowsService()`)
- `SeTcbPrivilege` for session token acquisition
- `CreateProcessAsUser` for launching in seat sessions

### Seat Account Privileges

```csharp
// Default: Users + Remote Desktop Users (NOT Administrators)
// GrantSeatAdministrator = false (default)

// ApplySeatGroupMembership():
// - Add to Users group
// - Add to Remote Desktop Users group
// - Remove from Administrators (unless GrantSeatAdministrator)
```

**Security posture:**
- Seat accounts are standard users by default
- Can become SYSTEM only if GrantSeatAdministrator is on
- File ACLs + DPAPI SYSTEM scope stop non-admin users

## API Security

### Authentication

```csharp
// API key auth — enforced for /api/ and /ws/ routes
// Header: X-MultiSeat-Key
// Query: ?key= (for browser WebSocket)
// GET /api/system/auth is always public (auth state check)
// POST /api/system/auth (disable auth) requires key
```

**Потенциальные проблемы:**
- **HIGH**: API key in URL query string (browser WebSocket limitation)
- **MEDIUM**: Plaintext HTTP — key visible to network sniffers
- **LOW**: No rate limiting on auth failures

### CORS

```csharp
// Default: loopback only
// CorsOrigins empty → only localhost:{port}
// Custom origins via MultiSeat:CorsOrigins
```

### Authorization

```csharp
// No UseAuthorization() in pipeline
// AllowAnonymous() grants nothing
// ApiServer.IsAlwaysPublic is the entire rule
// Only GET /api/system/auth is always public
```

## File System Security

### Credential Store

```
C:\ProgramData\MultiSeat\accounts.json
ACL: SYSTEM + Administrators only
DPAPI: CurrentUser (SYSTEM scope)
Write-then-rename (crash-safe)
```

### API Key File

```
C:\ProgramData\MultiSeat\api-key.txt
ACL: SYSTEM + Administrators only
Generated on first run
```

### Vibepollo Config

```
C:\ProgramData\MultiSeat\vibepollo\
Per-seat sunshine.conf files
 sunshine_state.json (UUID, pairings)
shared_credentials.json (web UI login)
```

**Потенциальные проблемы:**
- **MEDIUM**: Shared credentials file readable by all seat accounts
- **LOW**: Per-seat config contains port numbers, display paths

### Seat Presets

```
C:\ProgramData\MultiSeat\multiseat-host.json
Contains: account names, resolution, fps, NVENC preset
```

## Process Security

### CreateProcessAsUser

```csharp
// Token verification:
// 1. EnsureTokenBelongsTo(rawToken, accountName, sessionId)
// 2. Verify token session ID matches expected
// 3. VerifyLandedInSession() — post-launch check
// 4. Kill if wrong session
```

### Console Session Guard

```csharp
// ProcessInjector.EnsureNotConsoleSession()
// Refuses to launch seat's process into console session
// Exception: LaunchInConsoleSessionAsync (explicit opt-out)
```

## Input Security

### HidHide Session Jail

```csharp
// Undocumented HidHide feature: !<sessionId> suffix
// Device visible only inside that session
// Default OFF — persistent kernel-side blacklist
// Risk: console-side Vibepollo pad indistinguishable from seat's
```

### ViGEm Controller

```csharp
// EnableViGEmController = false (default)
// When on: physical XInput → ViGEm virtual controller
// Potential: wrong controller assigned to wrong seat
```

## Network Security

### API Port (9550)

```csharp
// ApiBindLoopbackOnly = false (default in code, true in appsettings)
// Plaintext HTTP — no HTTPS
// Firewall rule created on startup (if not loopback)
```

**Рекомендации:**
- Keep ApiBindLoopbackOnly = true (default in appsettings)
- If LAN access needed: firewall rule, not open port

### Vibepollo Ports

```csharp
// PortBase = 48100 (default)
// Per-seat 30-port blocks
// Firewall opened per-seat, closed on teardown
```

## RDP Security

### NLA (Network Level Authentication)

```csharp
// Disabled on RDP-Tcp listener (required for loopback logon)
// Affects ALL clients, not just loopback
// Mitigation: keep 3389 off the network
```

### Certificate Warnings

```csharp
// Suppressed by machine policy
// Affects every user on host
// Mitigation: keep 3389 off the network
```

## Threat Model

### What an Administrator can do

- Read all seat credentials (can become SYSTEM)
- Access all seat sessions
- Modify any configuration
- Kill any process
- Read API key

### What a non-admin user can do

- Nothing MultiSeat-specific (ACLs + DPAPI protect)
- Cannot read credential store
- Cannot read API key file
- Cannot access seat sessions

### What a seat account can do

- Access its own session
- Read its own Vibepollo config
- Cannot become SYSTEM (unless GrantSeatAdministrator)
- Cannot read other seats' data
- Cannot read credential store (DPAPI SYSTEM scope)

## Security Issues Found

### CRITICAL
- None identified

### HIGH

1. **API key transmitted in plaintext HTTP**
   - Location: ApiServer.cs
   - Impact: API key visible to network sniffers
   - Mitigation: ApiBindLoopbackOnly=true, firewall rules

2. **API key in URL query string**
   - Location: ApiServer.cs (WebSocket auth)
   - Impact: Key in browser history, server logs
   - Mitigation: Header preferred, query as fallback

### MEDIUM

3. **No HTTPS support**
   - Location: ApiServer.cs
   - Impact: All API traffic plaintext
   - Mitigation: Reverse proxy, loopback only

4. **Shared credentials file readable by seat accounts**
   - Location: VibepolloConfigBuilder.cs
   - Impact: Seat can read web UI credentials
   - Mitigation: None currently

5. **RDP NLA disabled machine-wide**
   - Location: prerequisites/install-prerequisites.ps1
   - Impact: All RDP connections affected
   - Mitigation: Keep 3389 off network

6. **Seat accounts in Administrators (when GrantSeatAdministrator=true)**
   - Location: MultiSeatOptions.cs
   - Impact: Full host control, credential store access
   - Mitigation: Default off, document risks

### LOW

7. **No rate limiting on API auth failures**
   - Location: ApiServer.cs
   - Impact: Brute force possible
   - Mitigation: Loopback only, firewall

8. **Credential store migration from LocalMachine scope**
   - Location: AccountManager.cs
   - Impact: Legacy entries readable by any local account
   - Mitigation: Auto-migration on startup

9. **Process token verification warnings**
   - Location: ProcessInjector.cs
   - Impact: Token not verified if GetTokenInformation fails
   - Mitigation: Warning logged, post-launch check

10. **mstsc window briefly visible**
    - Location: WindowHideHelper.cs
    - Impact: Console user sees seat session window
    - Mitigation: WatchAndHideNew() monitors new processes
