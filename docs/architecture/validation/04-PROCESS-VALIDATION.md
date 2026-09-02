# Process Validation

**Date**: 2026-08-30
**Status**: VALIDATED

---

## Purpose

Validate process ownership, PID tracking, Job Objects, and CreateProcessAsUser.

---

## 1. Process Ownership

### Current State

| Process | Owner | Tracking |
|---------|-------|----------|
| mstsc.exe | SessionLauncher | _pendingMstsc |
| sunshine.exe | VibepolloManager | _instances |
| game.exe | ProcessInjector | None |
| helper.exe | SessionLauncher | None |

**VERDICT**: Partial tracking. Games and helpers not tracked.

---

## 2. PID Tracking

### Current State

**Evidence**: SeatManager.cs
```csharp
seat.VibepolloProcessId = await _vibepolloManager.StartAsync(seat, ct);
```

**Analysis**: Only Vibepollo PID tracked. No game PID tracking.

**VERDICT**: Incomplete PID tracking.

---

## 3. Job Objects

### Current State

**Evidence**: Codebase search — absent.

**Analysis**: No Job Objects used. Best-effort Kill only.

**VERDICT**: Not implemented.

---

## 4. CreateProcessAsUser

### Current State

**Evidence**: SessionLauncher.cs
```csharp
if (!AdvApi.CreateProcessAsUserW(
    consoleToken, null, cmdLine,
    IntPtr.Zero, IntPtr.Zero, false, flags, envBlock, null,
    ref si, out var pi))
{
    var err = Marshal.GetLastWin32Error();
    throw new Win32Exception(err,
        $"Failed to launch mstsc.exe: error {err}");
}
```

**Analysis**: CreateProcessAsUser used for mstsc and helpers.

**VERDICT**: Implemented correctly.

---

## 5. Windows Token

### Current State

**Evidence**: SessionLauncher.cs
```csharp
if (WtsApi.WTSQueryUserToken((uint)sessionId, out var rawToken))
{
    // Duplicate to primary token
    if (AdvApi.DuplicateTokenEx(
        rawToken,
        AdvApi.MAXIMUM_ALLOWED,
        IntPtr.Zero,
        AdvApi.SecurityImpersonationLevel.SecurityImpersonation,
        AdvApi.TokenType.TokenPrimary,
        out var dupToken))
    {
        // ...
    }
}
```

**Analysis**: Token manipulation implemented correctly.

**VERDICT**: Implemented correctly.

---

## 6. SessionId

### Current State

**Evidence**: SeatManager.cs
```csharp
seat.SessionId = await _sessionLauncher.LaunchSessionAsync(
    seat.AccountName, ct, RdpGeometry.ForClient(seat.Width, seat.Height));
```

**Analysis**: SessionId tracked per seat.

**VERDICT**: Implemented correctly.

---

## 7. Process Tree

### Current State

**Evidence**: VibepolloManager.cs
```csharp
proc.Kill(entireProcessTree: true);
```

**Analysis**: Process tree killed on teardown.

**VERDICT**: Implemented correctly.

---

## 8. Can Provider/Game Escape Job Object?

### Analysis

Since Job Objects are not implemented, this question is N/A.

**VERDICT**: N/A (Job Objects not implemented).

---

## 9. Residual Process Adoption

### Current State

**Evidence**: Codebase search — absent.

**Analysis**: No residual process adoption implemented.

**VERDICT**: Not implemented.

---

## Summary

| Aspect | Status | Evidence |
|--------|--------|----------|
| Process ownership | Partial | SeatManager.cs |
| PID tracking | Incomplete | VibepolloManager only |
| Job Objects | Not implemented | Codebase search |
| CreateProcessAsUser | Implemented | SessionLauncher.cs |
| Windows token | Implemented | SessionLauncher.cs |
| SessionId tracking | Implemented | SeatManager.cs |
| Process tree kill | Implemented | VibepolloManager.cs |
| Residual adoption | Not implemented | Codebase search |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Only Vibepollo PID tracked | SeatManager.cs | FACT |
| No Job Objects | Codebase search | FACT (absent) |
| CreateProcessAsUser used | SessionLauncher.cs | FACT |
| Token manipulation implemented | SessionLauncher.cs | FACT |
| Process tree killed | VibepolloManager.cs | FACT |
| No residual adoption | Codebase search | FACT (absent) |
