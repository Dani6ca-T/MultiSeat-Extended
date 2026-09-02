# Reuse Matrix

**Date**: 2026-08-30
**Purpose**: Analyze which techniques can be reused by MultiSeat-Extended

---

## Reuse Categories

- **USE DIRECTLY** — Can use as-is
- **ADAPT** — Can use with modifications
- **REFERENCE ONLY** — Can learn from, cannot use
- **REIMPLEMENT** — Must reimplement independently
- **DO NOT USE** — Should not use

---

## Technique Reuse Analysis

### 1. RDP Wrapper (TermWrap)

| Aspect | Details |
|--------|---------|
| Technique | DLL proxy for termsrv.dll |
| Source | llccd/TermWrap |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already integrated, MIT license, proven |
| Risk | Low (already in use) |
| Evidence | install-prerequisites.ps1 |

---

### 2. CreateProcessAsUser

| Aspect | Details |
|--------|---------|
| Technique | Launch process with SYSTEM token |
| Source | Helios |
| License | GPLv3 (pattern is public domain) |
| Reuse | **REFERENCE ONLY** |
| Reason | Pattern is standard Windows API, can reimplement |
| Risk | Low (standard API) |
| Evidence | Helios: ProcessLauncher.cs |

---

### 3. Named Pipe IPC

| Aspect | Details |
|--------|---------|
| Technique | JSON over line-delimited pipe |
| Source | Helios |
| License | GPLv3 (pattern is public domain) |
| Reuse | **REFERENCE ONLY** |
| Reason | Pattern is standard, can reimplement |
| Risk | Low (standard IPC) |
| Evidence | Helios: SpawnerWorker.cs |

---

### 4. Guardian Loop

| Aspect | Details |
|--------|---------|
| Technique | 5s health check with crash backoff |
| Source | Helios |
| License | GPLv3 (pattern is public domain) |
| Reuse | **REFERENCE ONLY** |
| Reason | Pattern is standard, already implemented in MultiSeat-Extended |
| Risk | Low (already implemented) |
| Evidence | Helios: ProcessManager.cs, MultiSeat: SessionHealthCheck.cs |

---

### 5. Residual Process Adoption

| Aspect | Details |
|--------|---------|
| Technique | Adopt orphaned processes |
| Source | Helios |
| License | GPLv3 (pattern is public domain) |
| Reuse | **ADAPT** |
| Reason | Useful pattern, need to reimplement |
| Risk | Medium (complexity) |
| Evidence | Helios: ProcessManager.cs |

---

### 6. Conflicting Service Disable

| Aspect | Details |
|--------|---------|
| Technique | Detect and disable conflicting services |
| Source | Helios |
| License | GPLv3 (pattern is public domain) |
| Reuse | **REFERENCE ONLY** |
| Reason | Useful for provider switching, but not needed now |
| Risk | Low (optional feature) |
| Evidence | Helios: SpawnerWorker.cs |

---

### 7. SudoVDA Integration

| Aspect | Details |
|--------|---------|
| Technique | IddCx virtual display driver |
| Source | SudoMaker/SudoVDA |
| License | Unknown |
| Reuse | **USE DIRECTLY** |
| Reason | Already integrated, works well |
| Risk | Medium (license unknown) |
| Evidence | MultiSeat-Extended: SudoVDA integration |

---

### 8. Display Isolation (Primary + Shrunk)

| Aspect | Details |
|--------|---------|
| Technique | SudoVDA primary + RDP shrunk |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, unique advantage |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: Display isolation |

---

### 9. RDP Remote Audio

| Aspect | Details |
|--------|---------|
| Technique | Per-session audio endpoint |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, Windows built-in |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: PerSession audio |

---

### 10. HidHide Session Jail

| Aspect | Details |
|--------|---------|
| Technique | Undocumented session ID filtering |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, works well |
| Risk | Medium (undocumented feature) |
| Evidence | MultiSeat-Extended: HidHideConfigurator |

---

### 11. Moonlight Protocol

| Aspect | Details |
|--------|---------|
| Technique | RTSP + RTP streaming |
| Source | Vibepollo, Apollo |
| License | GPLv3 |
| Reuse | **REFERENCE ONLY** |
| Reason | Can use Vibepollo as external process, cannot link |
| Risk | Low (external process) |
| Evidence | Vibepollo: Moonlight protocol |

---

### 12. DXGI Desktop Duplication

| Aspect | Details |
|--------|---------|
| Technique | Desktop capture API |
| Source | Vibepollo, Apollo |
| License | GPLv3 (API is public) |
| Reuse | **REFERENCE ONLY** |
| Reason | API is standard, but capture is Vibepollo's job |
| Risk | Low (external process) |
| Evidence | Vibepollo: DDA capture |

---

### 13. sunshine.conf Format

| Aspect | Details |
|--------|---------|
| Technique | Key-value configuration |
| Source | Vibepollo, Apollo |
| License | GPLv3 (format is simple) |
| Reuse | **USE DIRECTLY** |
| Reason | Simple format, can generate for instances |
| Risk | Low (just configuration) |
| Evidence | Vibepollo: sunshine.conf |

---

### 14. JSON Configuration

| Aspect | Details |
|--------|---------|
| Technique | JSON-based configuration |
| Source | Helios, MultiSeat-Extended |
| License | N/A (standard) |
| Reuse | **USE DIRECTLY** |
| Reason | Already used, standard format |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: appsettings.json |

---

### 15. DPAPI

| Aspect | Details |
|--------|---------|
| Technique | Windows data protection |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, standard Windows API |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: DPAPI usage |

---

### 16. ACL

| Aspect | Details |
|--------|---------|
| Technique | Windows access control |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, standard Windows API |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: ACL usage |

---

### 17. API Key Authentication

| Aspect | Details |
|--------|---------|
| Technique | HTTP API authentication |
| Source | MultiSeat-Extended |
| License | MIT |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, standard pattern |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: API middleware |

---

### 18. Job Objects

| Aspect | Details |
|--------|---------|
| Technique | Process group management |
| Source | None (recommended) |
| License | N/A (standard API) |
| Reuse | **REIMPLEMENT** |
| Reason | Not implemented yet, useful for process isolation |
| Risk | Low (standard API) |
| Evidence | None (recommended) |

---

### 19. WMI Process Discovery

| Aspect | Details |
|--------|---------|
| Technique | WMI process scanning |
| Source | Helios |
| License | GPLv3 (pattern is public) |
| Reuse | **ADAPT** |
| Reason | Useful for process tracking, need to reimplement |
| Risk | Medium (complexity) |
| Evidence | Helios: ProcessManager.cs |

---

### 20. Graceful Shutdown

| Aspect | Details |
|--------|---------|
| Technique | Graceful process termination |
| Source | Helios, MultiSeat-Extended |
| License | GPLv3 (pattern is public) |
| Reuse | **REFERENCE ONLY** |
| Reason | Already implemented, pattern is standard |
| Risk | Low (already implemented) |
| Evidence | Helios: GracefulShutdown.cs |

---

### 21. Per-Instance Config Isolation

| Aspect | Details |
|--------|---------|
| Technique | Separate config directory per instance |
| Source | Helios, MultiSeat-Extended |
| License | N/A (standard practice) |
| Reuse | **USE DIRECTLY** |
| Reason | Already implemented, standard practice |
| Risk | Low (already implemented) |
| Evidence | MultiSeat-Extended: Per-seat config |

---

### 22. Token Manipulation

| Aspect | Details |
|--------|---------|
| Technique | SYSTEM token to user session |
| Source | Helios |
| License | GPLv3 (API is public) |
| Reuse | **REFERENCE ONLY** |
| Reason | Already implemented in MultiSeat-Extended |
| Risk | Low (already implemented) |
| Evidence | Helios: ProcessLauncher.cs, MultiSeat: SessionLauncher |

---

### 23. ViGEmBus

| Aspect | Details |
|--------|---------|
| Technique | Virtual gamepad bus driver |
| Source | ViGEm/ViGEmBus |
| License | MIT |
| Reuse | **DO NOT USE** |
| Reason | Legacy, being replaced by libvirtualhid |
| Risk | Low (deprecated) |
| Evidence | ViGEm/ViGEmBus (legacy) |

---

### 24. libvirtualhid

| Aspect | Details |
|--------|---------|
| Technique | UMDF2 + VHF virtual HID |
| Source | LizardByte/libvirtualhid |
| License | Custom (license required) |
| Reuse | **REFERENCE ONLY** |
| Reason | License requires separate agreement |
| Risk | Medium (license restrictions) |
| Evidence | LizardByte/libvirtualhid |

---

### 25. PowerShell Automation

| Aspect | Details |
|--------|---------|
| Technique | Script-based automation |
| Source | neo_multiseat |
| License | MIT |
| Reuse | **DO NOT USE** |
| Reason | Not suitable for full application |
| Risk | Low (not applicable) |
| Evidence | neo_multiseat: PowerShell scripts |

---

## Summary

### USE DIRECTLY (9)

1. RDP Wrapper (TermWrap) — Already integrated
2. SudoVDA Integration — Already integrated
3. Display Isolation — Already implemented
4. RDP Remote Audio — Already implemented
5. HidHide Session Jail — Already implemented
6. sunshine.conf Format — Simple configuration
7. JSON Configuration — Already used
8. DPAPI — Already implemented
9. ACL — Already implemented

### ADAPT (2)

1. Residual Process Adoption — From Helios
2. WMI Process Discovery — From Helios

### REFERENCE ONLY (9)

1. CreateProcessAsUser — Standard API
2. Named Pipe IPC — Standard IPC
3. Guardian Loop — Already implemented
4. Conflicting Service Disable — Optional feature
5. Moonlight Protocol — External process
6. DXGI Desktop Duplication — External process
7. Graceful Shutdown — Already implemented
8. Token Manipulation — Already implemented
9. libvirtualhid — License required

### REIMPLEMENT (1)

1. Job Objects — Not implemented yet

### DO NOT USE (2)

1. ViGEmBus — Legacy
2. PowerShell Automation — Not suitable

---

## Evidence

| Technique | Source | License | Reuse | Risk | Status |
|-----------|--------|---------|-------|------|--------|
| RDP Wrapper | TermWrap | MIT | USE DIRECTLY | Low | VERIFIED |
| CreateProcessAsUser | Helios | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| Named Pipe IPC | Helios | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| Guardian Loop | Helios | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| Residual Process Adoption | Helios | GPLv3 | ADAPT | Medium | VERIFIED |
| Conflicting Service Disable | Helios | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| SudoVDA | SudoMaker | Unknown | USE DIRECTLY | Medium | VERIFIED |
| Display Isolation | MultiSeat | MIT | USE DIRECTLY | Low | VERIFIED |
| RDP Remote Audio | MultiSeat | MIT | USE DIRECTLY | Low | VERIFIED |
| HidHide Session Jail | MultiSeat | MIT | USE DIRECTLY | Medium | VERIFIED |
| Moonlight Protocol | Vibepollo | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| DXGI Desktop Duplication | Vibepollo | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| sunshine.conf | Vibepollo | GPLv3 | USE DIRECTLY | Low | VERIFIED |
| JSON Configuration | Helios/MultiSeat | N/A | USE DIRECTLY | Low | VERIFIED |
| DPAPI | MultiSeat | MIT | USE DIRECTLY | Low | VERIFIED |
| ACL | MultiSeat | MIT | USE DIRECTLY | Low | VERIFIED |
| API Key Auth | MultiSeat | MIT | USE DIRECTLY | Low | VERIFIED |
| Job Objects | None | N/A | REIMPLEMENT | Low | UNVERIFIED |
| WMI Process Discovery | Helios | GPLv3 | ADAPT | Medium | VERIFIED |
| Graceful Shutdown | Helios/MultiSeat | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| Per-Instance Config | Helios/MultiSeat | N/A | USE DIRECTLY | Low | VERIFIED |
| Token Manipulation | Helios | GPLv3 | REFERENCE ONLY | Low | VERIFIED |
| ViGEmBus | ViGEm | MIT | DO NOT USE | Low | VERIFIED |
| libvirtualhid | LizardByte | Custom | REFERENCE ONLY | Medium | VERIFIED |
| PowerShell Automation | neo_multiseat | MIT | DO NOT USE | Low | VERIFIED |
