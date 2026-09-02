# Display Strategy

**Date**: 2026-08-30
**Purpose**: Compare virtual display options and provide recommendation

---

## Options Compared

### 1. SudoVDA (Current)

| Aspect | Details |
|--------|---------|
| Architecture | IddCx kernel-mode driver |
| HDR | v0.5+ with HDR EDID support |
| High Hz | Yes (driver supports) |
| Multi-display | Yes (multiple instances) |
| Multi-seat | Yes (one per seat) |
| Maintenance | Active (used by Apollo, Vibepollo) |
| License | Unknown (not explicitly stated) |
| Risk | Unknown license terms |
| Current use | ✅ Already integrated |

### 2. Virtual-Display-Driver

| Aspect | Details |
|--------|---------|
| Architecture | IddCx kernel-mode driver |
| HDR | Yes (EDID-based) |
| High Hz | Yes (up to 240Hz) |
| Multi-display | Yes |
| Multi-seat | Yes (multiple instances) |
| Maintenance | Active |
| License | Not explicitly stated |
| Risk | Unknown license, separate from SudoVDA |
| Current use | Alternative discovered in Phase 3 |

### 3. parsec-vdd

| Aspect | Details |
|--------|---------|
| Architecture | IddCx kernel-mode driver |
| HDR | Limited |
| High Hz | Yes |
| Multi-display | Yes |
| Multi-seat | Yes |
| Maintenance | Moderate |
| License | Not explicitly stated |
| Risk | Unknown license |
| Current use | Alternative discovered in Phase 3 |

### 4. Apollo Built-in SudoVDA

| Aspect | Details |
|--------|---------|
| Architecture | SudoVDA integrated in Apollo |
| HDR | Yes (Apollo feature) |
| High Hz | Yes |
| Multi-display | Limited (Issue #874) |
| Multi-seat | No (single-user) |
| Maintenance | Tied to Apollo |
| License | GPLv3 (Apollo) |
| Risk | Cannot separate from Apollo |
| Current use | Apollo uses this |

### 5. Vibepollo Own Driver

| Aspect | Details |
|--------|---------|
| Architecture | Own bundled driver (signed) |
| HDR | Yes |
| High Hz | Yes |
| Multi-display | Yes |
| Multi-seat | No (single-user) |
| Maintenance | Tied to Vibepollo |
| License | GPLv3 (Vibepollo) |
| Risk | Cannot separate from Vibepollo |
| Current use | Vibepollo uses this |

### 6. Duo Custom WDDM

| Aspect | Details |
|--------|---------|
| Architecture | Custom WDDM driver |
| HDR | Yes (supporter feature) |
| High Hz | Yes (up to 500Hz) |
| Multi-display | Yes |
| Multi-seat | Yes |
| Maintenance | Tied to Duo |
| License | Proprietary |
| Risk | Cannot inspect or modify |
| Current use | Duo uses this |

---

## Current SudoVDA Integration

**Source**: SeatManager.cs

**How it works**:
1. VirtualDisplayManager.CreateDisplayAsync() → SudoVDA IPC
2. Vibepollo starts, enumerates displays, writes UUID to log
3. MultiSeat parses UUID from Vibepollo log
4. UUID written to sunshine.conf as output_name
5. Display isolation: SudoVDA primary + RDP shrunk to 640x480
6. Refresh rate clamped via --set-display-hz

**Known issues**:
- Display created lazily (on client connect, not at provisioning)
- TryLateDisplayDetectionAsync retries from health check
- Display isolation state lost on session disconnect/sleep
- Must re-apply after every wake event

---

## Recommendation

### KEEP SUDOVDA

**Reasons**:
1. Already integrated and working
2. Proven with Vibepollo and Apollo
3. IddCx-based (standard Windows driver model)
4. HDR support available (v0.5+)
5. Multiple instances supported
6. No code changes needed

**Risks to mitigate**:
1. License uncertainty — investigate SudoVDA license terms
2. Driver dependency — SudoVDA must be installed
3. Display lazy creation — already handled by TryLateDisplayDetectionAsync

### DO NOT REPLACE

**Reasons**:
1. Virtual-Display-Driver and parsec-vdd are alternatives, not improvements
2. Custom WDDM (Duo) requires driver development expertise
3. Apollo/Vibepollo drivers are tied to their ecosystems
4. SudoVDA is the community standard (used by Apollo, Vibepollo)

### FUTURE: HDR Enablement

**Gap**: EnableHdr is no-op in MultiSeat-Extended

**What's needed**:
1. SudoVDA v0.5+ with HDR EDID
2. Force Windows to rebuild VidPN source mode (FP16 shared-displayable primary)
3. D3DKMTSetVidPnSourceOwner + D3DKMTSetDisplayMode with PreserveVidPn=FALSE
4. Vibepollo HDR encoding support

**Evidence**: Nonary (Vibepollo) demonstrated HDR in terminal session via this approach (issue #15)

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SudoVDA is IddCx-based | Driver architecture | VERIFIED |
| SudoVDA supports HDR v0.5+ | Driver documentation | VERIFIED |
| SudoVDA license unknown | LICENSE file search | VERIFIED (absent) |
| Virtual-Display-Driver is IddCx | GitHub README | VERIFIED |
| parsec-vdd is IddCx | GitHub README | VERIFIED |
| Duo has custom WDDM | README | VERIFIED (public) |
| Apollo uses SudoVDA | README | VERIFIED |
| Vibepollo has own driver | README | VERIFIED |
| Display lazy creation | SeatManager.cs comments | VERIFIED |
| HDR requires VidPN rebuild | issue #15 analysis | VERIFIED |
