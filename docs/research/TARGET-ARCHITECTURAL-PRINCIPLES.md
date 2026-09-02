# Target Architectural Principles

**Date**: 2026-08-30
**Purpose**: Define architectural principles derived from research

---

## Principles

### 1. Streaming is a Provider

**Statement**: Streaming server is an external provider, not a core component.

**Rationale**: Vibepollo/Apollo/Sunshine are complex, actively maintained, and GPLv3. They should be orchestrated, not embedded.

**Evidence**: MultiSeat-Extended already launches Vibepollo as external process.

### 2. Seat Owns Orchestration

**Statement**: SeatManager is the single orchestrator for all subsystems.

**Rationale**: Clear ownership prevents distributed coordination complexity.

**Evidence**: SeatManager.ProvisionSeatAsync coordinates all 9 steps.

### 3. Windows Owns Session Primitives

**Statement**: Session creation, token management, and process launch use standard Windows APIs.

**Rationale**: Windows APIs are stable, well-documented, and reliable.

**Evidence**: SessionLauncher uses CreateProcessAsUser, WTS APIs.

### 4. Drivers Own Hardware Virtualization

**Statement**: Virtual display, input isolation, and audio routing use existing drivers.

**Rationale**: Driver development is expensive and risky. Use SudoVDA, HidHide.

**Evidence**: MultiSeat-Extended uses SudoVDA and HidHide.

### 5. Provider Owns Streaming

**Statement**: Provider handles capture, encoding, streaming, and client protocol.

**Rationale**: These are complex, GPU-specific, and protocol-specific.

**Evidence**: Vibepollo handles DDA/WGC capture, NVENC/AMF encoding, RTSP/WebRTC.

### 6. No Provider-Specific Logic in Core

**Statement**: SeatManager has no Vibepollo-specific code.

**Rationale**: Provider abstraction enables switching providers.

**Evidence**: Target: IStreamingProvider interface.

### 7. No Credentials in SeatSpec

**Statement**: Seat configuration does not contain passwords or secrets.

**Rationale**: Credentials belong in DPAPI-protected storage.

**Evidence**: Current implementation uses DPAPI.

### 8. No Unnecessary Proprietary Dependency

**Statement**: Every external dependency must be open source or Windows built-in.

**Rationale**: Transparency, auditability, and community support.

**Evidence**: TermWrap (MIT), SudoVDA (unknown but used), HidHide (MIT).

### 9. External Components Behind Adapters

**Statement**: External components (SudoVDA, HidHide, TermWrap) are accessed through adapter interfaces.

**Rationale**: Enables testing, mocking, and replacement.

**Evidence**: VirtualDisplayManager, HidHideConfigurator, SessionLauncher.

### 10. Best-Effort Teardown with Job Objects

**Statement**: Teardown is best-effort, but Job Objects guarantee process cleanup.

**Rationale**: Individual steps may fail, but the process tree must be cleaned up.

**Evidence**: Current teardown uses try/catch per step. Target adds Job Object.

### 11. Health Checks are Fast and Frequent

**Statement**: Health checks run every 5 seconds.

**Rationale**: Fast detection enables fast recovery.

**Evidence**: SessionHealthCheck (5s), Helios guardian loop (5s).

### 12. Progressive Backoff Prevents Restart Loops

**Statement**: Crash recovery uses progressive backoff (30/60/120 seconds).

**Rationale**: Rapid restart loops waste resources and may worsen the problem.

**Evidence**: Helios ProcessManager backoff pattern.

### 13. State Persists Across Restarts

**Statement**: Seat state is persisted to disk, not just in memory.

**Rationale**: Service restart should not lose all seats.

**Evidence**: Target: disk persistence (currently in-memory).

### 14. Configuration is Generated, Not Edited

**Statement**: MultiSeat generates provider configuration (sunshine.conf), not the user.

**Rationale**: Configuration correctness is critical for streaming.

**Evidence**: VibepolloConfigBuilder generates sunshine.conf.

### 15. Isolation is the Default

**Statement**: Seats are isolated by default (display, audio, input, process).

**Rationale**: Isolation prevents cross-seat interference.

**Evidence**: SudoVDA display isolation, PerSession audio, HidHide session jail.

---

## Anti-Principles (What We Explicitly Avoid)

### 1. NO Monolithic Streaming

Streaming server is external, not embedded.

### 2. NO Provider Lock-in

Provider abstraction enables switching.

### 3. NO Custom Drivers (Unless Necessary)

Use existing drivers (SudoVDA, HidHide).

### 4. NO Game Compatibility Patches

Too complex, anti-cheat risk.

### 5. NO Steam Client Patching

TOS violation, ban risk.

### 6. NO Microservices

Single process is simpler.

### 7. NO Database

File-based config is simpler.

### 8. NO GPL Code in Core

GPLv3 components are external processes.

---

## Evidence

| Principle | Source | Status |
|-----------|--------|--------|
| Streaming is a provider | VibepolloManager (external process) | VERIFIED |
| Seat owns orchestration | SeatManager (9-step pipeline) | VERIFIED |
| Windows owns session primitives | SessionLauncher (CreateProcessAsUser) | VERIFIED |
| Drivers own hardware virtualization | SudoVDA + HidHide | VERIFIED |
| No provider-specific logic in core | Target: IStreamingProvider | RECOMMENDATION |
| No credentials in SeatSpec | DPAPI usage | VERIFIED |
| No unnecessary proprietary dependency | All deps are OSS or Windows built-in | VERIFIED |
| External components behind adapters | VirtualDisplayManager, HidHideConfigurator | VERIFIED |
| Best-effort teardown | TeardownSeatInternalAsync (try/catch) | VERIFIED |
| Health checks are fast (5s) | SessionHealthCheck | VERIFIED |
| Progressive backoff | Helios ProcessManager pattern | VERIFIED |
| State persists across restarts | Target: disk persistence | RECOMMENDATION |
| Configuration is generated | VibepolloConfigBuilder | VERIFIED |
| Isolation is the default | SudoVDA + PerSession + HidHide | VERIFIED |
