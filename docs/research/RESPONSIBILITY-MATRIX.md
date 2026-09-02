# Responsibility Matrix

**Date**: 2026-08-30
**Purpose**: Define owner, inputs, outputs, dependencies, failure modes, and recovery for each subsystem

---

## Subsystems

### 1. User Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (AccountManager) |
| Input | SeatRequest.AccountName |
| Output | Windows user account with correct groups |
| Dependencies | Windows Local Security Authority |
| Failure mode | Account creation fails (admin required) |
| Recovery owner | MultiSeat-Extended — throw, provision fails |
| License | MIT |

### 2. Session Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (SessionLauncher) |
| Input | AccountName + RdpGeometry |
| Output | Windows SessionId |
| Dependencies | TermWrap, mstsc, Windows Terminal Services |
| Failure mode | RDP loopback fails (TermWrap missing, NLA enabled) |
| Recovery owner | MultiSeat-Extended — throw, provision fails |
| License | MIT (MultiSeat) + MIT (TermWrap) |

### 3. Display Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (VirtualDisplayManager) + SudoVDA driver |
| Input | SeatInfo (resolution, fps) |
| Output | Virtual display (SudoVDA) assigned to seat |
| Dependencies | SudoVDA driver installed, Vibepollo IPC for UUID |
| Failure mode | SudoVDA not installed, display creation fails |
| Recovery owner | MultiSeat-Extended — best-effort, logged |
| License | MIT (MultiSeat) + Unknown (SudoVDA) |

### 4. Audio Manager

| Aspect | Details |
|--------|---------|
| Owner | Windows RDP + Vibepollo |
| Input | Session creation |
| Output | Per-session Remote Audio endpoint |
| Dependencies | Windows RDP audio redirection |
| Failure mode | Audio endpoint not created, RustDesk interference |
| Recovery owner | MultiSeat-Extended — RustDesk config suppression |
| License | MIT (MultiSeat) + GPLv3 (Vibepollo) |

### 5. Input Manager

| Aspect | Details |
|--------|---------|
| Owner | Vibepollo (gamepad) + HidHide (isolation) + MultiSeat (routing) |
| Input | Moonlight client input packets |
| Output | Injected input in seat session |
| Dependencies | Vibepollo input injection, HidHide driver (optional) |
| Failure mode | HidHide session jail fails, duplicate controllers |
| Recovery owner | MultiSeat-Extended — UncloakForSession, DestroyController |
| License | MIT (MultiSeat) + GPLv3 (Vibepollo) + MIT (HidHide) |

### 6. Streaming Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (VibepolloManager) + Vibepollo |
| Input | SeatInfo + sunshine.conf |
| Output | Running Vibepollo process on port block |
| Dependencies | Vibepollo executable, SudoVDA display, RDP session |
| Failure mode | Vibepollo crash, display not found, encoder probe fail |
| Recovery owner | MultiSeat-Extended — auto-restart with MaxRestartAttempts |
| License | MIT (MultiSeat) + GPLv3 (Vibepollo) |

### 7. Process Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (ProcessInjector) |
| Input | SessionId + executable path |
| Output | Game process running in seat session |
| Dependencies | CreateProcessAsUser, Windows token APIs |
| Failure mode | Token duplication fails, elevation check fails |
| Recovery owner | MultiSeat-Extended — throw, provision fails |
| License | MIT |

### 8. Port Allocator

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (PortAllocator) |
| Input | Request for port block |
| Output | PortBase (30-port block) |
| Dependencies | None |
| Failure mode | No free port blocks (MaxSeats reached) |
| Recovery owner | MultiSeat-Extended — throw "Maximum seat count reached" |
| License | MIT |

### 9. Firewall Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (FirewallManager) |
| Input | SeatInfo.PortBase |
| Output | Windows Firewall rules |
| Dependencies | Windows Firewall API, admin privileges |
| Failure mode | Firewall rule creation fails |
| Recovery owner | MultiSeat-Extended — best-effort, logged |
| License | MIT |

### 10. Health Monitor

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (SessionHealthCheck) |
| Input | SeatInfo (PID, status) |
| Output | Health status, crash detection, display re-detection |
| Dependencies | VibepolloServerQuery, VirtualDisplayManager |
| Failure mode | Health check itself fails |
| Recovery owner | Background service — continues checking |
| License | MIT |

### 11. Security Manager

| Aspect | Details |
|--------|---------|
| Owner | MultiSeat-Extended (DPAPI + ACL + API key) |
| Input | Credentials, file paths, API requests |
| Output | Encrypted credentials, permissions, authenticated requests |
| Dependencies | Windows DPAPI, file system ACL |
| Failure mode | DPAPI encryption fails, ACL modification fails |
| Recovery owner | MultiSeat-Extended — throw on critical, best-effort on non-critical |
| License | MIT |

---

## Cross-Cutting Concerns

| Concern | Owner | Failure Mode |
|---------|-------|-------------|
| Windows reboot | MultiSeat-Extended | All seats lost (in-memory state) |
| Service crash | Windows SCM | Auto-restart service, seats lost |
| Driver failure | Driver (SudoVDA/HidHide) | Display/input lost, logged |
| Network failure | Vibepollo | Stream interrupted, client disconnects |
| GPU failure | Vibepollo | Encoder fails, stream falls back or stops |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| AccountManager owns users | AccountManager.cs | VERIFIED |
| SessionLauncher owns sessions | SessionLauncher.cs | VERIFIED |
| VirtualDisplayManager owns displays | VirtualDisplayManager | VERIFIED |
| VibepolloManager owns streaming | VibepolloManager.cs | VERIFIED |
| ProcessInjector owns process launch | ProcessInjector.cs | VERIFIED |
| PortAllocator owns ports | PortAllocator.cs | VERIFIED |
| SessionHealthCheck owns health | SessionHealthCheck.cs | VERIFIED |
| DPAPI owns credentials | Security implementations | VERIFIED |
| MaxRestartAttempts = 3 | Constants.cs | VERIFIED |
| HealthCheckInterval = 5000ms | MultiSeatOptions.cs | VERIFIED |
