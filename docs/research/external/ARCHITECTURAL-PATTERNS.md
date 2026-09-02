# Architectural Patterns

**Date**: 2026-08-30
**Purpose**: Identify common architectural patterns across researched projects

---

## Pattern 1: Service + Manager

**Projects**: MultiSeat-Extended, Helios, Duo

**Implementation**: Windows Service (SYSTEM) + Manager application

**Advantages**:
- Privileged operations (SYSTEM token)
- Process isolation
- Background execution
- Crash recovery

**Disadvantages**:
- Complexity
- IPC overhead
- Security implications

**Evidence**:
- MultiSeat-Extended: SeatManager + ASP.NET Core service
- Helios: Spawner (SYSTEM) + App (WPF)
- Duo: Duo Service (SYSTEM) + Web UI

---

## Pattern 2: Per-Instance Config Isolation

**Projects**: MultiSeat-Extended, Helios, Vibepollo, Apollo

**Implementation**: Separate config directory per instance

**Advantages**:
- Independence
- No conflicts
- Easy management
- Easy cleanup

**Disadvantages**:
- Disk space
- Management overhead

**Evidence**:
- MultiSeat-Extended: Per-seat config in seat directories
- Helios: Per-instance sunshine.conf
- Vibepollo/Apollo: Per-instance config

---

## Pattern 3: Process Lifecycle Management

**Projects**: MultiSeat-Extended, Helios, Duo

**Implementation**: Start/Stop/Restart with health monitoring

**Advantages**:
- Reliability
- Crash recovery
- Auto-restart
- Graceful shutdown

**Disadvantages**:
- Complexity
- Resource overhead

**Evidence**:
- MultiSeat-Extended: SessionHealthCheck (5s), MaxRestartAttempts
- Helios: Guardian loop (5s), crash backoff (30/60/120s)
- Duo: Unknown (proprietary)

---

## Pattern 4: Display Isolation

**Projects**: MultiSeat-Extended (SudoVDA + RDP shrunk), Duo (custom WDDM)

**Implementation**: Virtual display per seat

**Advantages**:
- True isolation
- Independent resolution
- Independent refresh rate
- HDR support

**Disadvantages**:
- Driver dependency
- Performance overhead
- Complexity

**Evidence**:
- MultiSeat-Extended: SudoVDA primary + RDP shrunk
- Duo: Custom WDDM driver
- Apollo: SudoVDA (built-in)
- Vibepollo: Own bundled driver

---

## Pattern 5: Audio Isolation

**Projects**: MultiSeat-Extended (RDP Remote Audio), Duo (per-session)

**Implementation**: Per-session audio endpoint

**Advantages**:
- No VAC needed
- True isolation
- Low overhead

**Disadvantages**:
- No microphone path (MultiSeat-Extended)
- Windows dependency

**Evidence**:
- MultiSeat-Extended: RDP Remote Audio (per-session)
- Duo: Per-session audio (proprietary)
- Helios: Per-instance audio routing

---

## Pattern 6: Input Isolation

**Projects**: Duo (UMDF driver), MultiSeat-Extended (HidHide session jail)

**Implementation**: Session ID filtering

**Advantages**:
- Per-seat input assignment
- Gamepad isolation
- Keyboard/mouse isolation

**Disadvantages**:
- Driver dependency (Duo)
- Undocumented features (HidHide)
- Complexity

**Evidence**:
- Duo: UMDF input driver (proprietary)
- MultiSeat-Extended: HidHide session jail (undocumented feature)

---

## Pattern 7: Streaming Provider Abstraction

**Projects**: None (missing pattern)

**Implementation**: IStreamingProvider interface

**Advantages**:
- Multiple provider support
- Easy provider switching
- Clean separation

**Disadvantages**:
- Complexity
- Overhead

**Evidence**:
- MultiSeat-Extended: VibepolloManager (coupled)
- Helios: Supports multiple providers (Sunshine, Apollo, Vibeshine, Vibepollo)
- Duo: Sunshine (patched, proprietary)

---

## Pattern 8: Named Pipe IPC

**Projects**: Helios

**Implementation**: JSON over line-delimited byte stream

**Advantages**:
- Clean separation
- Low overhead
- Easy to implement
- Windows native

**Disadvantages**:
- Single machine only
- No authentication

**Evidence**:
- Helios: SpawnerWorker ↔ App communication

---

## Pattern 9: Guardian Loop

**Projects**: Helios, MultiSeat-Extended

**Implementation**: Periodic health check with crash recovery

**Advantages**:
- Reliability
- Auto-restart
- Crash detection
- Backoff strategy

**Disadvantages**:
- Resource overhead
- Complexity

**Evidence**:
- Helios: Guardian loop (5s), crash backoff
- MultiSeat-Extended: SessionHealthCheck (5s)

---

## Pattern 10: Residual Process Adoption

**Projects**: Helios

**Implementation**: Adopt orphaned processes

**Advantages**:
- Reliability
- No duplicate processes
- Clean state

**Disadvantages**:
- Complexity
- WMI dependency

**Evidence**:
- Helios: FindResidualInstancePids(), TryAdoptResidualRunningProcess()

---

## Pattern 11: Conflicting Service Disable

**Projects**: Helios

**Implementation**: Detect and disable conflicting services

**Advantages**:
- Clean state
- No conflicts
- Reliable startup

**Disadvantages**:
- Modifies system state
- May break other software

**Evidence**:
- Helios: EnforceConflictingServicesDisabledAsync() (SunshineService, ApolloService)

---

## Pattern 12: Token Manipulation

**Projects**: Helios, MultiSeat-Extended

**Implementation**: SYSTEM token assigned to user session

**Advantages**:
- Full privileges
- User environment
- Session access
- Desktop capture

**Disadvantages**:
- Security implications
- Complexity

**Evidence**:
- Helios: DuplicateTokenEx + SetTokenInformation
- MultiSeat-Extended: CreateProcessAsUser with SYSTEM token

---

## Pattern 13: DLL Proxying

**Projects**: TermWrap

**Implementation**: DLL proxy for termsrv.dll

**Advantages**:
- User-mode only
- Transparent behavior
- Easy to uninstall

**Disadvantages**:
- Registry modification
- Could be detected as malware
- PDB dependency

**Evidence**:
- TermWrap: TermWrap.dll proxies termsrv.dll

---

## Pattern 14: PowerShell Automation

**Projects**: neo_multiseat

**Implementation**: Script-based automation

**Advantages**:
- Simple
- Transparent
- Easy to understand
- No compilation needed

**Disadvantages**:
- Not a full application
- No health monitoring
- No crash recovery

**Evidence**:
- neo_multiseat: PowerShell scripts

---

## Pattern Summary

| Pattern | Projects | MultiSeat-Extended Uses? |
|---------|----------|-------------------------|
| Service + Manager | MultiSeat-Extended, Helios, Duo | Yes |
| Per-Instance Config | MultiSeat-Extended, Helios, Vibepollo, Apollo | Yes |
| Process Lifecycle | MultiSeat-Extended, Helios, Duo | Yes |
| Display Isolation | MultiSeat-Extended, Duo, Apollo, Vibepollo | Yes |
| Audio Isolation | MultiSeat-Extended, Duo, Helios | Yes |
| Input Isolation | Duo, MultiSeat-Extended | Yes |
| Streaming Provider Abstraction | None | No (missing) |
| Named Pipe IPC | Helios | No |
| Guardian Loop | Helios, MultiSeat-Extended | Yes |
| Residual Process Adoption | Helios | No |
| Conflicting Service Disable | Helios | No |
| Token Manipulation | Helios, MultiSeat-Extended | Yes |
| DLL Proxying | TermWrap | Yes (TermWrap) |
| PowerShell Automation | neo_multiseat | No |

---

## Evidence

| Pattern | Source | Evidence | Status |
|---------|--------|----------|--------|
| Service + Manager | Multiple | Architecture diagrams | VERIFIED |
| Per-Instance Config | Multiple | Config isolation | VERIFIED |
| Process Lifecycle | Multiple | Health checks, crash recovery | VERIFIED |
| Display Isolation | Multiple | Virtual displays | VERIFIED |
| Audio Isolation | Multiple | Per-session audio | VERIFIED |
| Input Isolation | Multiple | HidHide, UMDF | VERIFIED |
| Named Pipe IPC | Helios | SpawnerWorker.cs | VERIFIED |
| Guardian Loop | Helios, MultiSeat | ProcessManager.cs, SessionHealthCheck.cs | VERIFIED |
| Residual Process Adoption | Helios | ProcessManager.cs | VERIFIED |
| Conflicting Service Disable | Helios | SpawnerWorker.cs | VERIFIED |
| Token Manipulation | Helios, MultiSeat | ProcessLauncher.cs, SessionLauncher.cs | VERIFIED |
| DLL Proxying | TermWrap | TermWrap.dll | VERIFIED |
| PowerShell Automation | neo_multiseat | Scripts | VERIFIED |
