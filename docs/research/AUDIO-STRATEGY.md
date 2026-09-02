# Audio Strategy

**Date**: 2026-08-30
**Purpose**: Compare audio isolation options and provide recommendation

---

## Current State: PerSession Audio

**Source**: MultiSeatOptions.cs, SeatManager.cs

**How it works**:
1. Windows RDP creates per-session "Remote Audio" endpoint
2. Each seat session has its own audio device
3. Vibepollo captures from session's Remote Audio endpoint
4. No VAC, no VoiceMeeter, no host-side audio routing

**Advantages**:
- True isolation (each session owns its endpoint)
- No host audio disruption
- No additional software needed
- Windows built-in feature

**Limitations**:
- **No microphone path** — RDP doesn't redirect mic from session to host
- One-way audio only (session → host)

---

## Options Compared

### 1. RDP Remote Audio (Current)

| Aspect | Details |
|--------|---------|
| Mechanism | Windows RDP per-session audio endpoint |
| Playback | ✅ Yes (session → stream) |
| Microphone | ❌ No (RDP limitation) |
| Isolation | ✅ True (per-session endpoint) |
| VAC needed | No |
| License | Windows built-in |
| Risk | Low |
| Current use | ✅ Active |

### 2. Virtual Audio Cable (VAC)

| Aspect | Details |
|--------|---------|
| Mechanism | Kernel-mode virtual audio driver |
| Playback | ✅ Yes |
| Microphone | ✅ Yes (bidirectional) |
| Isolation | ⚠️ Manual routing required |
| VAC needed | Yes (VB-CABLE, VoiceMeeter) |
| License | Commercial (VB-CABLE free, VoiceMeeter paid) |
| Risk | **Host audio disruption** — earlier SharedHost mode collapsed endpoint nodes |
| Current use | ❌ Removed (legacy mode) |

### 3. WASAPI Loopback

| Aspect | Details |
|--------|---------|
| Mechanism | Windows Audio Session API loopback capture |
| Playback | ✅ Yes (capture from any endpoint) |
| Microphone | ⚠️ Limited (capture from mic endpoint) |
| Isolation | ⚠️ Depends on endpoint selection |
| VAC needed | No |
| License | Windows built-in |
| Risk | Low |
| Current use | ✅ Vibepollo uses WASAPI for capture |

### 4. Vibepollo WebRTC Mic (Future)

| Aspect | Details |
|--------|---------|
| Mechanism | WebRTC data channel for mic audio |
| Playback | ✅ Yes |
| Microphone | ✅ Yes (bypasses host audio stack) |
| Isolation | ✅ Per-client |
| VAC needed | No |
| License | GPLv3 (Vibepollo) |
| Risk | Beta (v1.19.0+) |
| Current use | ❌ Not yet stable |

---

## Recommendation

### KEEP PerSession Audio

**Reasons**:
1. Already integrated and working
2. True isolation (no VAC needed)
3. No host audio disruption
4. Windows built-in (no additional dependency)

**Known limitation**: No microphone path

### DO NOT REVIVE SharedHost Mode

**Reasons**:
1. Collapsed host audio endpoint nodes (27 → 1)
2. Required AudioEndpointBuilder restart
3. Capped seats at 4 (one cable each)
4. VAC dependency (VB-CABLE + VoiceMeeter Potato)

### WAIT for Vibepollo WebRTC Mic

**Reasons**:
1. Bypasses host audio stack entirely
2. Per-client isolation
3. No VAC needed
4. Similar architecture to PerSession

**Timeline**: Vibepollo 1.19.x (beta, not yet stable)

### DO NOT BUILD Custom Audio Driver

**Reasons**:
1. RDP Remote Audio works for playback
2. Vibepollo WebRTC mic will solve microphone
3. Custom audio driver is complex and risky
4. No open-source virtual audio driver with per-session isolation exists

---

## Microphone Gap Analysis

| Approach | Mic Support | Isolation | VAC Needed | Risk |
|----------|-------------|-----------|------------|------|
| RDP Remote Audio | ❌ No | N/A | No | None |
| VAC (SharedHost) | ✅ Yes | ⚠️ Manual | Yes | Host disruption |
| WASAPI capture | ⚠️ Limited | ⚠️ Manual | No | Low |
| Vibepollo WebRTC mic | ✅ Yes | ✅ Per-client | No | Beta |

**Conclusion**: Wait for Vibepollo WebRTC mic stabilization. Do not implement custom audio solution.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| PerSession uses RDP Remote Audio | MultiSeatOptions.cs comments | VERIFIED |
| No microphone path | MultiSeatOptions.cs comments | VERIFIED |
| SharedHost collapsed endpoints | MultiSeatOptions.cs comments | VERIFIED |
| Vibepollo uses WASAPI loopback | Vibepollo research | VERIFIED |
| Vibepollo WebRTC mic is beta | Vibepollo v1.19.0 release | VERIFIED |
| No VAC needed with PerSession | SeatManager.cs (no audio device assignment) | VERIFIED |
