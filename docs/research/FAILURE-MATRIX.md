# Failure Matrix

**Date**: 2026-08-30
**Purpose**: Document all failure scenarios, detection, recovery, and terminal states

---

## Failure Scenarios

### 1. Service Crash

| Aspect | Details |
|--------|---------|
| Detection | Windows SCM auto-restart |
| Owner | Windows SCM |
| Recovery | Service restarts, all seats lost (in-memory state) |
| Retry | Automatic (SCM) |
| Terminal state | Service stopped |
| Impact | ALL seats lost |

**Mitigation**: Persist seat state to disk (TARGET).

### 2. Provider Crash (Vibepollo)

| Aspect | Details |
|--------|---------|
| Detection | SessionHealthCheck (5s) — process not alive |
| Owner | MultiSeat-Extended |
| Recovery | Auto-restart Vibepollo |
| Retry | MaxRestartAttempts = 3 |
| Terminal state | Seat enters Error state |
| Impact | Streaming interrupted, seat recoverable |

**Current**: ✅ Handled (auto-restart with limit)

### 3. Game Crash

| Aspect | Details |
|--------|---------|
| Detection | ❌ NOT IMPLEMENTED |
| Owner | ❌ MISSING |
| Recovery | ❌ NOT IMPLEMENTED |
| Retry | N/A |
| Terminal state | Game process gone, seat still Ready |
| Impact | Game stopped, streaming continues |

**Target**: ProcessTracker detects game crash, optionally restarts.

### 4. Session Disconnect

| Aspect | Details |
|--------|---------|
| Detection | SessionHealthCheck — session inactive |
| Owner | MultiSeat-Extended |
| Recovery | Reconnect mstsc (same session ID) |
| Retry | Automatic (health check tick) |
| Terminal state | Session logoff (if intentional) |
| Impact | Display lost temporarily, provider may fail |

**Current**: ⚠️ PARTIAL (session reconnect, but display isolation lost)

### 5. RDP Failure

| Aspect | Details |
|--------|---------|
| Detection | Session creation fails |
| Owner | MultiSeat-Extended + TermWrap |
| Recovery | Provisioning fails, throw exception |
| Retry | Manual (user retries) |
| Terminal state | Seat in Error state |
| Impact | Seat cannot be provisioned |

**Mitigation**: TermWrap auto offset discovery handles most RDP failures.

### 6. Display Failure

| Aspect | Details |
|--------|---------|
| Detection | SudoVDA IPC fails, display UUID not found |
| Owner | VirtualDisplayManager + SudoVDA |
| Recovery | Re-create display, re-detect UUID |
| Retry | TryLateDisplayDetectionAsync (health check tick) |
| Terminal state | Display not available, provider captures primary |
| Impact | Wrong display captured, CPU elevated |

**Current**: ✅ Handled (TryLateDisplayDetectionAsync)

### 7. Audio Failure

| Aspect | Details |
|--------|---------|
| Detection | Vibepollo audio capture fails |
| Owner | Vibepollo + Windows RDP |
| Recovery | Restart Vibepollo |
| Retry | Auto-restart |
| Terminal state | No audio in stream |
| Impact | Audio missing from stream |

**Current**: ✅ Handled (auto-restart)

### 8. Input Failure

| Aspect | Details |
|--------|---------|
| Detection | ❌ NOT IMPLEMENTED |
| Owner | ❌ MISSING |
| Recovery | ❌ NOT IMPLEMENTED |
| Retry | N/A |
| Terminal state | Input not working |
| Impact | Controller/keyboard/mouse not responding |

**Target**: Input health monitoring (low priority).

### 9. Driver Failure

| Aspect | Details |
|--------|---------|
| Detection | SudoVDA/HidHide driver crash |
| Owner | Driver (external) |
| Recovery | Driver restart (requires reboot for kernel drivers) |
| Retry | Manual reboot |
| Terminal state | Driver unavailable |
| Impact | Display/input lost |

**Mitigation**: Kernel driver failures are rare. Document reboot requirement.

### 10. Windows Reboot

| Aspect | Details |
|--------|---------|
| Detection | Service stops |
| Owner | Windows SCM |
| Recovery | Service restarts, seats lost |
| Retry | Automatic (SCM) |
| Terminal state | Fresh start |
| Impact | ALL seats lost |

**Mitigation**: Persist seat state to disk + auto-reprovision on startup (TARGET).

### 11. User Logout

| Aspect | Details |
|--------|---------|
| Detection | Session logoff detected |
| Owner | MultiSeat-Extended |
| Recovery | Re-create session (same seat) |
| Retry | Manual or auto (TARGET) |
| Terminal state | Session terminated |
| Impact | Seat stops, provider stopped |

**Target**: Auto-recreate session on user logout.

### 12. Orphan Process

| Aspect | Details |
|--------|---------|
| Detection | ❌ NOT IMPLEMENTED (WMI scan) |
| Owner | ❌ MISSING |
| Recovery | ❌ NOT IMPLEMENTED (adoption or kill) |
| Retry | N/A |
| Terminal state | Process running without seat |
| Impact | Resource leak, port collision |

**Target**: ProcessTracker with WMI scan + Job Object cleanup.

---

## Recovery Priority Matrix

| Failure | Detection | Recovery | Current Status |
|---------|-----------|----------|----------------|
| Service crash | ✅ SCM | ⚠️ Seats lost | Needs persistence |
| Provider crash | ✅ 5s health | ✅ Auto-restart | COMPLETE |
| Game crash | ❌ None | ❌ None | MISSING |
| Session disconnect | ✅ Health | ⚠️ Reconnect | Partial |
| RDP failure | ✅ Provisioning | ⚠️ Throw | Manual retry |
| Display failure | ✅ Health | ✅ Late detection | COMPLETE |
| Audio failure | ✅ Provider | ✅ Auto-restart | COMPLETE |
| Input failure | ❌ None | ❌ None | MISSING |
| Driver failure | ⚠️ External | ❌ Reboot needed | External |
| Windows reboot | ✅ SCM | ⚠️ Seats lost | Needs persistence |
| User logout | ⚠️ Session | ⚠️ Manual | Needs automation |
| Orphan process | ❌ None | ❌ None | MISSING |

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| Service crash loses all seats | In-memory ConcurrentDictionary | VERIFIED |
| Provider crash is auto-restarted | SessionHealthCheck + VibepolloManager | VERIFIED |
| Game crash is not detected | Codebase search | VERIFIED (absent) |
| Session disconnect handled | TryLateDisplayDetectionAsync | VERIFIED |
| Display failure handled | TryLateDisplayDetectionAsync | VERIFIED |
| Orphan process not handled | Codebase search | VERIFIED (absent) |
| MaxRestartAttempts = 3 | Constants.cs | VERIFIED |
