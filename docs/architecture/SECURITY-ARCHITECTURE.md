# Security Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define security boundaries, credential management, and privilege model.

---

## Service Identity

### MultiSeat.Service

| Property | Value |
|----------|-------|
| Account | LocalSystem (SYSTEM) |
| Privileges | SeTcbPrivilege, SeAssignPrimaryTokenPrivilege |
| Session | Session 0 (service session) |

**FACT**: MultiSeat.Service runs as SYSTEM.

---

## User Identity

### Seat Accounts

| Property | Value |
|----------|-------|
| Account type | Local user |
| Groups | Users, Remote Desktop Users |
| Administrator | No (default) |
| Privileges | Standard user |

### GrantSeatAdministrator

| Setting | Default | Risk |
|---------|---------|------|
| GrantSeatAdministrator | false | HIGH (seat gets full host control) |

**FACT**: GrantSeatAdministrator option exists, default OFF.

---

## Credential Boundary

### Where Credentials Exist

| Location | Storage | Lifetime |
|----------|---------|----------|
| DPAPI | Encrypted file | Persistent |
| sunshine_state.json | Certificates, keys | Persistent |
| Windows account | Password hash | Persistent |
| API key | appsettings.json | Persistent |

### Where Credentials Must NOT Exist

| Location | Risk |
|----------|------|
| SeatSpec | API wire model |
| ProviderConfiguration | Config files |
| Command line | Process listing |
| Environment variables | Process inspection |
| Logs | Log files |
| API responses | Network exposure |

**DECISION**: Credentials never cross public models.

---

## DPAPI

### Usage

```csharp
// Encrypt
byte[] encrypted = ProtectedData.Protect(plainData, null, DataProtectionScope.LocalMachine);

// Decrypt
byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
```

### What's Encrypted

| Data | Scope |
|------|-------|
| API key | LocalMachine |
| Credentials | Per-user |

**FACT**: DPAPI uses DataProtectionScope.LocalMachine.

---

## ACL

### File Permissions

| Path | Permission | Grant |
|------|------------|-------|
| SharedGameLibrary | Modify | BUILTIN\Users |
| Seat config | Full Control | Seat account |
| Service config | Read | SYSTEM |

### Directory Permissions

| Path | Permission | Grant |
|------|------------|-------|
| C:\MultiSeatGames | Modify | BUILTIN\Users |
| C:\Users\{Seat} | Full Control | Seat account |

**FACT**: SharedGameLibrary uses icacls for permissions.

---

## API Authentication

### API Key

```
Header: X-API-Key: {key}
```

### Configuration

```json
{
  "MultiSeat": {
    "ApiKey": "secret-key-here"
  }
}
```

### Validation

```csharp
if (request.Headers["X-API-Key"] != expectedApiKey)
    return Unauthorized();
```

**FACT**: API key middleware validates X-API-Key header.

---

## Named Pipe ACL

### Current State

No Named Pipe IPC exists. MultiSeat uses direct method calls.

### Future

If Named Pipe IPC is added:
- Restrict access to SYSTEM and seat accounts
- Use DACL on pipe
- Authenticate clients

---

## Process Privileges

### Service Process

| Privilege | Purpose |
|-----------|---------|
| SeTcbPrivilege | Capture Winlogon desktop |
| SeAssignPrimaryTokenPrivilege | Assign token to session |

### Seat Processes

| Privilege | Purpose |
|-----------|---------|
| Standard user | Seat operations |
| No SeTcbPrivilege | Cannot capture console |

### Provider Processes

| Privilege | Purpose |
|-----------|---------|
| SYSTEM (via token) | Full privileges |
| Assigned to session | Can capture session display |

**FACT**: Provider processes run as SYSTEM in seat session.

---

## Network Exposure

### API

| Binding | Default | Risk |
|---------|---------|------|
| All interfaces | Yes | HIGH (if not firewalled) |
| Loopback only | No | LOW |

### ApiBindLoopbackOnly

| Setting | Default | Risk |
|---------|---------|------|
| ApiBindLoopbackOnly | false | API exposed on all interfaces |

**FACT**: ApiBindLoopbackOnly option exists, default false.

---

## Security Recommendations

### Must Do

1. **Enable ApiBindLoopbackOnly** if dashboard is local-only
2. **Set strong API key** in production
3. **Keep GrantSeatAdministrator = false** unless needed
4. **Use firewall rules** to restrict API access

### Should Do

1. **Restrict seat account privileges** (no admin)
2. **Encrypt credentials** (DPAPI)
3. **Audit API access** (logs)

### Must Not Do

1. **Never put credentials in SeatSpec**
2. **Never put credentials in logs**
3. **Never put credentials in command line**
4. **Never grant seat admin by default**

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Service runs as SYSTEM | Program.cs | FACT |
| GrantSeatAdministrator default false | MultiSeatOptions.cs | FACT |
| DPAPI uses LocalMachine scope | Security implementations | FACT |
| API key middleware | ApiServer.cs | FACT |
| ApiBindLoopbackOnly default false | MultiSeatOptions.cs | FACT |
