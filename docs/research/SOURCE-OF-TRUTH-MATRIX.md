# Source of Truth Matrix

**Date**: 2026-08-30
**Purpose**: Define which component owns each capability

---

## Matrix

| Capability | Owner | Evidence |
|------------|-------|----------|
| User creation | MultiSeat-Extended | AccountManager.cs |
| User deletion | MultiSeat-Extended | AccountManager.cs |
| User credentials | Windows | Local account system |
| Session creation | RDP/Windows layer | SessionLauncher — mstsc loopback |
| Session monitoring | MultiSeat-Extended | SessionHealthCheck.cs |
| Session disconnect | MultiSeat-Extended | SessionLauncher.DisconnectSession |
| Session logoff | MultiSeat-Extended | SessionLauncher.LogoffSession |
| RDP patching | TermWrap (external) | install-prerequisites.ps1 |
| Seat entity | MultiSeat-Extended | SeatInfo model |
| Seat lifecycle | MultiSeat-Extended | SeatManager.ProvisionSeatAsync |
| Seat state | MultiSeat-Extended | SeatStatus enum |
| Seat configuration | MultiSeat-Extended | SeatRequest + SeatPreset |
| Virtual display creation | SudoVDA driver (external) | VirtualDisplayManager |
| Display assignment | MultiSeat-Extended | UUID from Vibepollo log → output_name |
| Display isolation | MultiSeat-Extended | --setup-display-isolation helper |
| Resolution | MultiSeat-Extended | RdpGeometry at session creation |
| Refresh rate | MultiSeat-Extended | --set-display-hz helper |
| HDR encoding | Streaming provider | Vibepollo NVENC HDR metadata |
| HDR display | SudoVDA driver | HDR EDID (not yet enabled in MultiSeat) |
| Audio isolation | Windows RDP | Per-session Remote Audio endpoint |
| Audio capture | Streaming provider | Vibepollo WASAPI loopback |
| Microphone | **MISSING** | RDP limitation — no mic path |
| Keyboard/Mouse isolation | **MISSING** | InputHookManager is no-op |
| Gamepad forwarding | Streaming provider | Vibepollo Moonlight controller |
| Gamepad isolation | HidHide driver (external) | Session jail feature (undocumented) |
| Game launch | MultiSeat-Extended | ProcessInjector.LaunchInSessionAsync |
| Game process tracking | **MISSING** | No implementation |
| Game crash detection | **MISSING** | No implementation |
| Game RDP compatibility | **MISSING** | No Application Compatibility Layer |
| Streaming server | Vibepollo (external process) | VibepolloManager |
| Streaming configuration | MultiSeat-Extended | VibepolloConfigBuilder |
| Streaming ports | MultiSeat-Extended | PortAllocator |
| Streaming display target | MultiSeat-Extended | output_name in sunshine.conf |
| Streaming health | MultiSeat-Extended | VibepolloServerQuery |
| Process launch (service→session) | MultiSeat-Extended | ProcessInjector — CreateProcessAsUser |
| Token management | MultiSeat-Extended | SessionLauncher — DuplicateTokenEx |
| Process cleanup | MultiSeat-Extended | Best-effort Kill on teardown |
| Crash recovery | MultiSeat-Extended | SessionHealthCheck → VibepolloManager.Restart |
| Credential storage | MultiSeat-Extended | DPAPI |
| File permissions | MultiSeat-Extended | ACL on shared library |
| API authentication | MultiSeat-Extended | API key middleware |
| Web UI | MultiSeat-Extended | React dashboard |
| Steam isolation | **MISSING** | No implementation |
| Mutex handling | **MISSING** | No implementation |

---

## Ownership Summary

| Owner | Capabilities |
|-------|-------------|
| MultiSeat-Extended | 30 |
| Windows/RDP | 4 |
| Vibepollo (external) | 3 |
| SudoVDA (external driver) | 2 |
| TermWrap (external) | 1 |
| HidHide (external driver) | 1 |
| **MISSING** | **10** |

---

## Key Insight

MultiSeat-Extended owns the **orchestration layer** — it coordinates external components (TermWrap, SudoVDA, HidHide, Vibepollo) and Windows primitives (RDP sessions, accounts, ACL). The streaming and driver work is delegated.

The 10 MISSING capabilities represent gaps where MultiSeat-Extended has no solution today:

1. HDR display enablement
2. Microphone path
3. Keyboard/Mouse session isolation
4. Game process tracking
5. Game crash detection
6. Game RDP compatibility patching
7. Steam multi-instance isolation
8. Mutex handling
9. Metrics endpoint
10. Full seat re-provision on crash

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| AccountManager owns user lifecycle | AccountManager.cs | VERIFIED |
| SessionLauncher owns session creation | SessionLauncher.cs | VERIFIED |
| TermWrap owns RDP patching | install-prerequisites.ps1 | VERIFIED |
| SudoVDA owns virtual display | VirtualDisplayManager | VERIFIED |
| Vibepollo owns streaming | VibepolloManager | VERIFIED |
| HidHide owns gamepad isolation | HidHideConfigurator | VERIFIED |
| ProcessInjector owns process launch | ProcessInjector.cs | VERIFIED |
| PortAllocator owns port allocation | PortAllocator.cs | VERIFIED |
| No game process tracking | Codebase search | VERIFIED (absent) |
| No Steam isolation | Codebase search | VERIFIED (absent) |
| No HDR enablement | MultiSeatOptions.EnableHdr = no-op | VERIFIED |
| InputHookManager is no-op | CLAUDE.md "Known Constraints" | VERIFIED |
