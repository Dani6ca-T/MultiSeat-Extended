# Historical Lessons

**Date**: 2026-08-30
**Purpose**: Document lessons learned from issues, PRs, and releases across projects

---

## TermWrap Lessons

### Lesson 1: Windows Updates Break RDP Wrappers

**Source**: TermWrap releases, rdpwrap abandonment

**Problem**: Windows updates change termsrv.dll, breaking RDP wrappers

**Solution**: Auto offset discovery (PDB symbols)

**Evidence**:
- rdpwrap: Abandoned due to manual .ini files
- TermWrap: "patch offsets are automatically searched"
- TermWrap v0.5: "Fix total failure on 10.0.26100.7523"

**Lesson**: Auto-discovery is essential for long-term maintenance

---

### Lesson 2: DLL Proxying Can Be Detected as Malware

**Source**: TermWrap installation

**Problem**: DLL proxying modifies system behavior

**Solution**: User-mode only, transparent behavior, easy uninstall

**Evidence**:
- TermWrap: "Copy the dlls" + registry entries
- Uninstall: "Revert_to_default.reg" + delete files

**Lesson**: Keep modifications transparent and reversible

---

### Lesson 3: Server/Home Editions Need Extra Wraps

**Source**: TermWrap README

**Problem**: Server/Home editions lack features enabled in Professional/Enterprise

**Solution**: UmWrap (camera/USB) and EndpWrap (audio recording)

**Evidence**:
- TermWrap: "UmWrap is only needed on server and home editions"
- TermWrap: "EndpWrap is only needed on server and home editions"

**Lesson**: Feature availability varies by Windows edition

---

## Helios Lessons

### Lesson 4: Conflicting Services Must Be Disabled

**Source**: Helios SpawnerWorker

**Problem**: SunshineService and ApolloService conflict with Helios

**Solution**: Detect and disable conflicting services

**Evidence**:
- Helios: `ConflictingServiceNames = ["SunshineService", "ApolloService"]`
- Helios: `EnforceConflictingServicesDisabledAsync()` runs on startup and every 15s

**Lesson**: Always check for conflicting services

---

### Lesson 5: Residual Processes Cause Issues

**Source**: Helios ProcessManager

**Problem**: Sunshine processes can survive crashes or manual stops

**Solution**: Residual process adoption with WMI discovery

**Evidence**:
- Helios: `FindResidualInstancePids()` uses WMI
- Helios: `TryAdoptResidualRunningProcess()` adopts single residual
- Helios: Force-terminates duplicates

**Lesson**: Always scan for residual processes

---

### Lesson 6: Non-Elevated Processes Must Be Terminated

**Source**: Helios ProcessManager

**Problem**: Non-elevated Sunshine processes cannot capture desktop

**Solution**: Verify elevation after launch, terminate if not elevated

**Evidence**:
- Helios: `IsProcessElevated()` checks TOKEN_ELEVATION
- Helios: `HasAdministratorCapability()` checks TOKEN_ELEVATION_TYPE
- Helios: Force-terminates non-elevated processes

**Lesson**: Always verify elevation for desktop capture

---

### Lesson 7: Crash Backoff Prevents Rapid Restart Loops

**Source**: Helios ProcessManager

**Problem**: Rapid restart loops waste resources

**Solution**: Progressive backoff (30/60/120 seconds)

**Evidence**:
- Helios: Crash 3 → 30s backoff
- Helios: Crash 4 → 60s backoff
- Helios: Crash 5+ → 120s backoff
- Helios: Reset if stable for 30s

**Lesson**: Implement progressive backoff for crash recovery

---

### Lesson 8: Clone Instances Need Unique Identity

**Source**: Helios v0.8.1 release

**Problem**: Cloned instances share TLS credentials and paired clients

**Solution**: Fresh TLS credentials and paired-client state for clones

**Evidence**:
- Helios v0.8.1: "Cloned instances now keep a unique device identity"
- Helios v0.8.1: "prevents Moonlight from overwriting the source host's pairing"

**Lesson**: Always generate unique identity for cloned instances

---

### Lesson 9: Disabled State Prevents Unintended Auto-Start

**Source**: Helios v0.8.1 release

**Problem**: Cloned instances start automatically, causing conflicts

**Solution**: Cloned instances start in Disabled state

**Evidence**:
- Helios v0.8.1: "Cloned instances start in the Disabled state by default"

**Lesson**: New instances should start disabled

---

## Apollo Lessons

### Lesson 10: Per-Client Fixed Identity Solves Display Configuration

**Source**: Apollo README

**Problem**: Random or shared identity causes display configuration issues

**Solution**: Fixed identity per client, remembered by Windows

**Evidence**:
- Apollo: "Apollo assigns a fixed identity for each Artemis/Moonlight client"
- Apollo: "display configuration will be automatically remembered and managed by Windows natively"

**Lesson**: Fixed identity per client is better than random/shared

---

### Lesson 11: Multiple Virtual Displays Cause Conflicts

**Source**: Apollo Issue #874

**Problem**: Multiple virtual displays from multiple instances get confused

**Solution**: Not fully solved (Apollo limitation)

**Evidence**:
- Apollo Issue #874: "the virtual display created by Apollo only works for one client, or gets confused"

**Lesson**: Multiple virtual displays need careful management

---

### Lesson 12: First Client Gets Full Permissions

**Source**: Apollo README

**Problem**: New clients should not have full permissions by default

**Solution**: First client gets FULL permissions, new clients get limited

**Evidence**:
- Apollo: "The FIRST client paired with Apollo will be granted with FULL permissions"

**Lesson**: Implement progressive permission granting

---

## Duo Lessons (Inferred from Public Sources)

### Lesson 13: HDR Requires Client Device Support

**Source**: Apollo README, Duo features

**Problem**: HDR streaming depends on client device capability

**Solution**: Document client requirements, recommend Apple products

**Evidence**:
- Apollo: "For client devices, usually Apple products that have HDR capability can be trusted"
- Duo: HDR as supporter feature ($10+ Patreon)

**Lesson**: HDR is client-dependent, not just host-dependent

---

### Lesson 14: Game Process Patching Is Essential for Compatibility

**Source**: Duo features

**Problem**: Some games refuse to run in RDP sessions

**Solution**: Application Compatibility Layer (process patching)

**Evidence**:
- Duo: "Application Compatibility Layer — patching games that refuse to work in RDP"

**Lesson**: Game compatibility requires process patching

---

### Lesson 15: Steam Multi-Instance Requires Special Handling

**Source**: Duo features

**Problem**: Steam mutex prevents multiple instances

**Solution**: Built-in Steam multi-instance support

**Evidence**:
- Duo: "Steam multi-instance — Built-in support"

**Lesson**: Steam isolation needs special mechanisms

---

### Lesson 16: Seamless Display Adjustment Avoids Reconnect

**Source**: Duo features

**Problem**: Display changes require stream reconnect

**Solution**: Seamless display adjustment

**Evidence**:
- Duo: "Seamless display adjustment — No reconnect"

**Lesson**: Display changes should not require reconnect

---

## MultiSeat-Extended Lessons

### Lesson 17: Display Isolation Reduces CPU Usage

**Source**: MultiSeat-Extended implementation

**Problem**: RDP display uses CPU for rendering

**Solution**: SudoVDA primary + RDP shrunk

**Evidence**:
- MultiSeat-Extended: SudoVDA primary + RDP shrunk implementation

**Lesson**: Virtual display as primary reduces CPU overhead

---

### Lesson 18: Per-Session Audio Eliminates VAC

**Source**: MultiSeat-Extended implementation

**Problem**: Virtual Audio Cable adds complexity and overhead

**Solution**: RDP Remote Audio (per-session)

**Evidence**:
- MultiSeat-Extended: PerSession audio implementation

**Lesson**: Use Windows built-in features when possible

---

### Lesson 19: HidHide Session Jail Works But Is Undocumented

**Source**: MultiSeat-Extended implementation

**Problem**: Gamepad isolation needs session-aware filtering

**Solution**: HidHide session jail (undocumented feature)

**Evidence**:
- MultiSeat-Extended: HidHideConfigurator

**Lesson**: Undocumented features work but may break in future updates

---

### Lesson 20: Health Checks Must Be Fast

**Source**: MultiSeat-Extended, Helios

**Problem**: Slow health checks miss crashes

**Solution**: 5-second interval

**Evidence**:
- MultiSeat-Extended: SessionHealthCheck (5s)
- Helios: Guardian loop (5s)

**Lesson**: 5-second interval is optimal for health checks

---

## Cross-Project Lessons

### Lesson 21: GPLv3 Cannot Be Embedded in MIT Projects

**Source**: License analysis

**Problem**: GPLv3 copyleft prevents embedding in MIT projects

**Solution**: Keep GPLv3 components as external processes

**Evidence**:
- Vibepollo, Apollo, Helios: GPLv3
- MultiSeat-Extended: MIT
- MultiSeat-Extended: VibepolloManager launches Vibepollo as external process

**Lesson**: License compatibility is critical

---

### Lesson 22: Proprietary Components Cannot Be Inspected

**Source**: Duo research

**Problem**: Proprietary code cannot be verified or modified

**Solution**: Use open-source alternatives

**Evidence**:
- Duo: Proprietary, no source code
- MultiSeat-Extended: Open-source alternatives (TermWrap, SudoVDA, HidHide)

**Lesson**: Prefer open-source for inspectability

---

### Lesson 23: Auto-Discovery Beats Manual Configuration

**Source**: TermWrap, rdpwrap

**Problem**: Manual configuration (`.ini` files) breaks on updates

**Solution**: Auto-discovery (PDB symbols)

**Evidence**:
- rdpwrap: Abandoned due to manual .ini files
- TermWrap: Auto offset discovery

**Lesson**: Auto-discovery is essential for long-term maintenance

---

### Lesson 24: Guardian Loops Prevent Silent Failures

**Source**: Helios, MultiSeat-Extended

**Problem**: Processes can crash silently

**Solution**: Periodic health checks with auto-restart

**Evidence**:
- Helios: Guardian loop (5s)
- MultiSeat-Extended: SessionHealthCheck (5s)

**Lesson**: Always implement health checks

---

### Lesson 25: Per-Instance Isolation Is Critical

**Source**: All projects

**Problem**: Shared state causes conflicts

**Solution**: Separate config, ports, credentials per instance

**Evidence**:
- Helios: Per-instance sunshine.conf
- MultiSeat-Extended: Per-seat config
- Vibepollo/Apollo: Per-instance config

**Lesson**: Isolate everything per instance

---

## Summary

| Lesson | Source | Evidence | Status |
|--------|--------|----------|--------|
| Auto-discovery beats manual | TermWrap, rdpwrap | TermWrap releases | VERIFIED |
| DLL proxying can be detected | TermWrap | Installation docs | VERIFIED |
| Server/Home need extra wraps | TermWrap | README | VERIFIED |
| Conflicting services must be disabled | Helios | SpawnerWorker.cs | VERIFIED |
| Residual processes cause issues | Helios | ProcessManager.cs | VERIFIED |
| Non-elevated processes must be terminated | Helios | ProcessManager.cs | VERIFIED |
| Crash backoff prevents restart loops | Helios | ProcessManager.cs | VERIFIED |
| Clone instances need unique identity | Helios | v0.8.1 release | VERIFIED |
| Disabled state prevents auto-start | Helios | v0.8.1 release | VERIFIED |
| Per-client fixed identity solves display | Apollo | README | VERIFIED |
| Multiple virtual displays cause conflicts | Apollo | Issue #874 | VERIFIED |
| First client gets full permissions | Apollo | README | VERIFIED |
| HDR requires client device support | Apollo, Duo | README, features | VERIFIED |
| Game process patching is essential | Duo | Features | VERIFIED |
| Steam multi-instance requires special handling | Duo | Features | VERIFIED |
| Seamless display adjustment avoids reconnect | Duo | Features | VERIFIED |
| Display isolation reduces CPU | MultiSeat-Extended | Implementation | VERIFIED |
| Per-session audio eliminates VAC | MultiSeat-Extended | Implementation | VERIFIED |
| HidHide session jail is undocumented | MultiSeat-Extended | Implementation | VERIFIED |
| Health checks must be fast (5s) | MultiSeat-Extended, Helios | Implementation | VERIFIED |
| GPLv3 cannot be embedded in MIT | License analysis | License files | VERIFIED |
| Proprietary cannot be inspected | Duo research | No source code | VERIFIED |
| Auto-discovery beats manual config | TermWrap, rdpwrap | Releases | VERIFIED |
| Guardian loops prevent silent failures | Helios, MultiSeat-Extended | Implementation | VERIFIED |
| Per-instance isolation is critical | All projects | Implementation | VERIFIED |
