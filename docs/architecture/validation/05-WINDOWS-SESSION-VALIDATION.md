# Windows Session Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate RDP session, TermWrap, and Windows session architecture.

---

## 1. RDP Loopback

### Current State

**Evidence**: SessionLauncher.cs
```csharp
var cmdLine = $"mstsc.exe /v:{RdpLoopbackAddress}";
```

**Analysis**: RDP loopback via mstsc to 127.0.0.2.

**VERDICT**: Implemented correctly.

---

## 2. TermWrap

### Current State

**Evidence**: install-prerequisites.ps1

**Analysis**: TermWrap v0.6 installed for concurrent RDP sessions.

**VERDICT**: Implemented correctly.

---

## 3. Windows Session

### Current State

**Evidence**: SessionLauncher.cs
```csharp
var sessionId = await CreateSessionViaRdpLoopbackAsync(accountName, password, geometry, ct);
```

**Analysis**: Session created via RDP loopback.

**VERDICT**: Implemented correctly.

---

## 4. LogonUser

### Current State

**Evidence**: SessionLauncher.cs
```csharp
// Path 2: LogonUser with stored credential
var password = _accounts.GetCredential(accountName);
if (password is null)
    throw new InvalidOperationException($"No credential available for '{accountName}'");
return LogonAndCreatePrimaryToken(accountName, password, sessionId);
```

**Analysis**: LogonUser used as fallback.

**VERDICT**: Implemented correctly.

---

## 5. CreateProcessAsUser

### Current State

**Evidence**: SessionLauncher.cs
```csharp
if (!AdvApi.CreateProcessAsUserW(
    consoleToken, null, cmdLine,
    IntPtr.Zero, IntPtr.Zero, false, flags, envBlock, null,
    ref si, out var pi))
```

**Analysis**: CreateProcessAsUser used for process launch.

**VERDICT**: Implemented correctly.

---

## 6. SessionId → Seat Mapping

### Current State

**Evidence**: SeatManager.cs
```csharp
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
    seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));
```

**Analysis**: SessionId stored in SeatInfo.

**VERDICT**: Implemented correctly.

---

## 7. Race Conditions

### Login/Logout

**Analysis**: SessionLauncher handles existing sessions (Active/Disconnected).

**VERDICT**: Handled.

### Disconnect/Reconnect

**Analysis**: SessionLauncher handles disconnect/reconnect.

**VERDICT**: Handled.

### Session Reuse

**Analysis**: SessionLauncher checks for existing sessions.

**VERDICT**: Handled.

### User Reuse

**Analysis**: AccountManager manages users.

**VERDICT**: Handled.

### Service Restart

**Analysis**: In-memory state lost on restart.

**VERDICT**: NOT handled (state persistence needed).

### Windows Reboot

**Analysis**: In-memory state lost on reboot.

**VERDICT**: NOT handled (state persistence needed).

---

## Summary

| Aspect | Status | Evidence |
|--------|--------|----------|
| RDP loopback | Implemented | SessionLauncher.cs |
| TermWrap | Installed | install-prerequisites.ps1 |
| Windows session | Created | SessionLauncher.cs |
| LogonUser | Used as fallback | SessionLauncher.cs |
| CreateProcessAsUser | Used | SessionLauncher.cs |
| SessionId mapping | Implemented | SeatManager.cs |
| Login/logout | Handled | SessionLauncher.cs |
| Disconnect/reconnect | Handled | SessionLauncher.cs |
| Service restart | NOT handled | In-memory state |
| Windows reboot | NOT handled | In-memory state |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| RDP loopback via mstsc | SessionLauncher.cs | FACT |
| TermWrap installed | install-prerequisites.ps1 | FACT |
| Session created via RDP | SessionLauncher.cs | FACT |
| LogonUser fallback | SessionLauncher.cs | FACT |
| CreateProcessAsUser used | SessionLauncher.cs | FACT |
| SessionId in SeatInfo | SeatManager.cs | FACT |
| In-memory state | ConcurrentDictionary | FACT |
