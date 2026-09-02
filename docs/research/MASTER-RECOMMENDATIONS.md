# Master Recommendations

**Date**: 2026-08-30
**Purpose**: Summarize all recommendations from research

---

## Keep (What Already Works)

| Component | Evidence | Status |
|-----------|----------|--------|
| 9-step provisioning pipeline | SeatManager.ProvisionSeatAsync | ✅ COMPLETE |
| TermWrap for RDP | install-prerequisites.ps1 | ✅ COMPLETE |
| SudoVDA for virtual display | VirtualDisplayManager | ✅ COMPLETE |
| PerSession audio | MultiSeatOptions.cs | ✅ COMPLETE |
| HidHide session jail | HidHideConfigurator | ✅ COMPLETE |
| Vibepollo for streaming | VibepolloManager | ✅ COMPLETE |
| DPAPI for credentials | Security implementations | ✅ COMPLETE |
| ASP.NET Core API | ApiServer | ✅ COMPLETE |
| React dashboard | Dashboard | ✅ COMPLETE |
| Port allocation (30-port blocks) | PortAllocator | ✅ COMPLETE |
| SessionHealthCheck (5s) | SessionHealthCheck | ✅ COMPLETE |
| Display isolation (primary + shrunk) | ApplyDisplayIsolationAsync | ✅ COMPLETE |
| Shared game library (icacls) | SharedGameLibrary | ✅ COMPLETE |
| Emulator netplay | RetroArch port assignment | ✅ COMPLETE |

---

## Improve (What Needs Enhancement)

| Component | Issue | Recommendation | Effort |
|-----------|-------|----------------|--------|
| Crash recovery | MaxRestartAttempts = 3, no backoff | Add progressive backoff (30/60/120s) | LOW |
| Process cleanup | Best-effort Kill, no Job Objects | Add Job Object isolation | LOW |
| Process tracking | No PID → Seat mapping | Add ProcessTracker | LOW |
| Seat state | In-memory ConcurrentDictionary | Persist to disk | MEDIUM |
| Provider coupling | VibepolloManager is tightly coupled | Create IStreamingProvider | MEDIUM |
| Residual processes | Not adopted or killed | Add WMI scan + adoption | MEDIUM |
| HidHide default | OFF by default | Enable by default with safety | LOW |
| ViGEm option | Confusing EnableViGEmController | Deprecate or remove | LOW |

---

## Replace (What Should Change)

| Component | Current | Target | Reason |
|-----------|---------|--------|--------|
| ViGEm controller | Optional, legacy | Deprecate | Vibepollo handles natively |
| InputHookManager | No-op (Session 0) | Re-architect or remove | Runs from wrong session |

---

## Add (What's Missing)

| Component | Priority | Effort | Evidence |
|-----------|----------|--------|----------|
| Process tracking | P0 | LOW | Codebase search — absent |
| Job Objects | P0 | LOW | Codebase search — absent |
| Progressive backoff | P0 | LOW | Helios pattern |
| Seat state persistence | P1 | MEDIUM | In-memory state |
| IStreamingProvider | P1 | MEDIUM | VibepolloManager coupling |
| Residual process adoption | P1 | MEDIUM | Helios pattern |
| Game crash detection | P1 | LOW | Codebase search — absent |
| Full seat re-provision | P2 | MEDIUM | Error state is terminal |
| GPU selection | P2 | LOW | No adapter config |
| Metrics endpoint | P3 | LOW | No /metrics |
| Conflicting service detection | P3 | LOW | Helios pattern |

---

## Delegate (What Not to Build)

| Component | Delegate To | Reason |
|-----------|-------------|--------|
| Video encoding | Vibepollo | GPU-specific, complex |
| Desktop capture | Vibepollo | GPU-specific, complex |
| Streaming protocol | Vibepollo | Protocol-specific, complex |
| Client pairing | Vibepollo | Certificate management |
| Gamepad forwarding | Vibepollo | Moonlight protocol |
| Audio capture | Vibepollo | WASAPI loopback |
| Virtual display driver | SudoVDA | Kernel-mode, complex |
| RDP patching | TermWrap | termsrv-specific, complex |
| Gamepad isolation | HidHide | Kernel-mode, complex |
| Audio isolation | Windows RDP | Built-in feature |

---

## Research More (What's Not Clear)

| Topic | Question | Priority |
|-------|----------|----------|
| SudoVDA license | What are the exact license terms? | HIGH |
| libvirtualhid license | What does the custom license allow? | MEDIUM |
| HDR enablement | How to rebuild VidPN source mode? | MEDIUM |
| Steam multi-instance | Can --userdatadir work? | MEDIUM |
| K/M isolation | How to re-architect InputHookManager? | LOW |
| Game RDP compatibility | How does Duo's App Compat Layer work? | LOW |

---

## Summary

| Category | Count | Key Items |
|----------|-------|-----------|
| Keep | 14 | Provisioning, TermWrap, SudoVDA, audio, HidHide, Vibepollo |
| Improve | 8 | Backoff, Job Objects, process tracking, persistence, provider abstraction |
| Replace | 2 | ViGEm legacy, InputHookManager no-op |
| Add | 11 | Process tracking, Job Objects, backoff, persistence, provider abstraction |
| Delegate | 10 | Streaming, capture, encoding, pairing, display driver, RDP |
| Research | 6 | SudoVDA license, HDR, Steam, K/M isolation |

---

## Evidence

| Recommendation | Source | Status |
|----------------|--------|--------|
| Progressive backoff needed | Helios ProcessManager | VERIFIED |
| Job Objects needed | Windows API documentation | VERIFIED |
| Process tracking needed | Codebase search — absent | VERIFIED |
| Seat persistence needed | In-memory ConcurrentDictionary | VERIFIED |
| Provider abstraction needed | VibepolloManager coupling | VERIFIED |
| SudoVDA license unknown | LICENSE search — absent | VERIFIED |
