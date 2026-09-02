# Lessons for MultiSeat-Extended

**Date**: 2026-08-30
**Purpose**: What each project teaches us about building a better multiseat platform

---

## 1. Helios

### What They Solve
Multi-instance management for Sunshine/Apollo/Vibepollo.

### How They Solve It
- WPF UI + Windows Service (SYSTEM)
- Named Pipe IPC between App and Spawner
- Per-instance config directories
- Per-instance port allocation
- Per-instance audio routing

### What They Do Better
- **Named Pipe IPC pattern** — Clean separation of UI and privileged operations
- **Provider flexibility** — Supports multiple Sunshine forks
- **Per-instance audio routing** — Assign specific audio device per instance

### What They Do Worse
- **No session management** — Doesn't create Windows sessions
- **No display isolation** — Doesn't manage virtual displays
- **No input isolation** — Doesn't handle input devices
- **GPLv3 license** — Cannot embed in MIT project

### What Concept Is Worth Adopting
- **Named Pipe IPC pattern** — Could inspire IStreamingProvider abstraction
- **Per-instance config isolation** — Already implemented in MultiSeat-Extended

### What Should NOT Be Adopted
- **GPLv3 code** — Cannot link into MIT project
- **WPF UI** — MultiSeat uses React

### Open Questions
- How does Helios handle provider crashes?
- How does Helios handle display assignment?
- How does Helios handle input routing?

---

## 2. Apollo

### What They Solve
Streaming with built-in virtual display and per-client identity.

### How They Solve It
- Fork of Sunshine with additional features
- Built-in SudoVDA integration
- Per-client fixed identity
- Permission management (role-based)

### What They Do Better
- **Built-in virtual display** — No external driver needed
- **Per-client identity** — Each client gets unique identity
- **Permission management** — Role-based access control

### What They Do Worse
- **Single-user only** — No multi-seat support
- **No session management** — Doesn't create Windows sessions
- **GPLv3 license** — Cannot embed in MIT project

### What Concept Is Worth Adopting
- **Per-client identity** — Could be useful for multi-seat
- **Permission management** — Role-based access control

### What Should NOT Be Adopted
- **GPLv3 code** — Cannot link into MIT project
- **Single-user design** — MultiSeat needs multi-user

### Open Questions
- How does Apollo handle multiple clients?
- How does Apollo handle display assignment?
- How does Apollo handle input routing?

---

## 3. TermWrap

### What They Solve
Concurrent RDP sessions on Windows Home/Pro.

### How They Solve It
- DLL proxy that patches termsrv.dll at runtime
- Integrated RDPWrapOffsetFinder for auto offset discovery
- User-mode only (no kernel patches)

### What They Do Better
- **Auto offset discovery** — Survives Windows updates
- **User-mode only** — No kernel patches needed
- **MIT license** — Full compatibility

### What They Do Worse
- **PDB dependency** — Needs symbol server for offset discovery
- **Single purpose** — Only handles RDP patching

### What Concept Is Worth Adopted
- **Auto offset discovery** — Could be applied to other patching scenarios
- **User-mode approach** — Safer than kernel patches

### What Should NOT Be Adopted
- **Nothing** — TermWrap is already integrated and works well

### Open Questions
- How reliable is TermWrap across Windows versions?
- How does TermWrap handle Windows updates?
- What are the performance implications?

---

## 4. rdpWrapper

### What They Solve
RDP setup and configuration.

### How They Solve It
- Pure C# utility
- Inspired by stascorp/rdpwrap
- GUI for RDP configuration

### What They Do Better
- **Pure C#** — No native dependencies
- **GUI** — User-friendly configuration

### What They Do Worse
- **Different approach** — Not a DLL proxy like TermWrap
- **Not used by MultiSeat** — MultiSeat uses TermWrap

### What Concept Is Worth Adopting
- **Nothing** — MultiSeat-Extended already uses TermWrap

### What Should NOT Be Adopted
- **Nothing** — Different approach, not applicable

### Open Questions
- How does rdpWrapper compare to TermWrap?
- What are the advantages of pure C# approach?

---

## 5. neo_multiseat

### What They Solve
Simple multiseat setup via PowerShell scripts.

### How They Solve It
- PowerShell automation of RDPWrap
- User account creation
- Session monitoring
- CSV export

### What They Do Better
- **Simplicity** — Single script, easy to understand
- **Transparency** — No hidden components
- **Automation** — Automated RDPWrap recovery

### What They Do Worse
- **No streaming** — No Moonlight/Sunshine integration
- **No display isolation** — No virtual displays
- **No input isolation** — No input device management
- **Script-based** — Not a full application

### What Concept Is Worth Adopted
- **Automated RDPWrap recovery** — Could be useful for MultiSeat-Extended
- **Live session monitoring** — Real-time session audit

### What Should NOT Be Adopted
- **Script-based approach** — MultiSeat needs full application
- **No streaming integration** — MultiSeat needs Moonlight support

### Open Questions
- How does neo_multiseat handle RDPWrap recovery?
- How does neo_multiseat handle session monitoring?
- What are the limitations of script-based approach?

---

## 6. MultiseatProject

### What They Solve
Unknown — repository not found.

### How They Solve It
Unknown.

### What They Do Better
Unknown.

### What They Do Worse
Unknown.

### What Concept Is Worth Adopting
Unknown.

### What Should NOT Be Adopted
Unknown.

### Open Questions
- Does the repository exist?
- Is it private?
- What was the original purpose?

---

## 7. LuaTools

### What They Solve
Steam plugin management and AppID configuration.

### How They Solve It
- WPF desktop client
- Steam manifest/lua configuration
- Plugin loader

### What They Do Better
- **Steam integration** — Deep Steam plugin support
- **AppID management** — Manage Steam AppIDs

### What They Do Worse
- **NOT RELEVANT** — Not related to multiseat gaming
- **DRM bypass tools** — Legal/ethical concerns

### What Concept Is Worth Adopting
- **Nothing** — Not relevant to MultiSeat-Extended

### What Should NOT Be Adopted
- **Nothing** — Not relevant to MultiSeat-Extended

### Open Questions
- Why was this project included in original research?
- What is the relationship to multiseat gaming?

---

## 8. Vibepollo

### What They Solve
Advanced streaming with virtual display, HDR, and game compatibility.

### How They Solve It
- Fork of Apollo with AI-generated code
- Own bundled virtual display driver
- RTSS integration
- Lossless Scaling integration
- NVIDIA Smooth Motion

### What They Do Better
- **HDR support** — Working HDR streaming
- **Virtual display** — Own bundled driver
- **Game compatibility** — RTSS, Lossless Scaling
- **Active development** — Multiple releases per week

### What They Do Worse
- **Single-user only** — No multi-seat support
- **GPLv3 license** — Cannot embed in MIT project
- **99% AI-generated** — Quality concerns

### What Concept Is Worth Adopted
- **HDR support** — What's needed for MultiSeat-Extended
- **Virtual display management** — How to handle display lifecycle
- **Game compatibility** — RTSS, Lossless Scaling integration

### What Should NOT Be Adopted
- **GPLv3 code** — Cannot link into MIT project
- **AI-generated code** — Quality concerns

### Open Questions
- How does Vibepollo handle HDR?
- How does Vibepollo handle virtual display lifecycle?
- How does Vibepollo handle game compatibility?

---

## 9. Duo

### What They Solve
Full multiseat gaming with custom drivers.

### How They Solve It
- Custom WDDM display driver
- UMDF input driver
- Application Compatibility Layer
- TermWrap (bundled)
- Sunshine (patched)

### What They Do Better
- **HDR support** — Working HDR streaming
- **Game process patching** — Application Compatibility Layer
- **Steam multi-instance** — Built-in support
- **Seamless display adjustment** — No reconnect
- **Custom drivers** — More integrated

### What They Do Worse
- **Proprietary** — Cannot inspect or modify
- **Closed-source** — Cannot verify claims
- **Freemium model** — Paid features

### What Concept Is Worth Adopting
- **Application Compatibility Layer** — Game process patching
- **UMDF input driver** — Session ID filtering
- **Steam multi-instance** — Steam isolation
- **Seamless display adjustment** — Resolution changes without reconnect

### What Should NOT Be Adopted
- **Proprietary code** — Cannot reuse
- **Custom drivers** — Too complex, SudoVDA works
- **Freemium model** — Different distribution model

### Open Questions
- How does Duo implement game process patching?
- How does Duo implement seamless display adjustment?
- How does Duo implement Steam multi-instance?

---

## Summary: Key Lessons

### 1. Open Source Wins
- MultiSeat-Extended's MIT license is a major advantage
- GPLv3 projects (Vibepollo, Apollo, Helios) cannot be embedded
- Proprietary projects (Duo) cannot be inspected or modified

### 2. Simplicity Wins
- TermWrap (simple DLL proxy) beats complex alternatives
- RDP Remote Audio (built-in Windows feature) beats custom audio drivers
- HidHide session jail (undocumented feature) beats custom input drivers

### 3. Integration Wins
- MultiSeat-Extended's 9-step provisioning pipeline is well-designed
- Per-session audio via RDP Remote Audio is elegant
- Display isolation via SudoVDA primary + RDP shrunk is unique

### 4. Gaps Remain
- HDR support needs work
- Game process patching needs research
- Steam multi-instance needs research
- Provider abstraction needs implementation

### 5. Community Knowledge
- TermWrap's auto offset discovery is valuable
- Helios's Named Pipe IPC pattern is reusable
- neo_multiseat's automated RDPWrap recovery is useful
