# MultiSeat-Extended: Сравнение RDP решений (RDP Gap Analysis)

## Обзор проектов

| Project | Language | Approach | License | Maintenance |
|---------|----------|----------|---------|-------------|
| MultiSeat-Extended | C# (.NET 9) | Uses RDPWrap/TermWrap as dependency | MIT | Active |
| TermWrap (llccd) | C++ | DLL proxy with dynamic patching | MIT | Active |
| TermWrap Rust (kernalix7) | Rust | Symbol-free runtime analysis | MIT | Active |
| rdpWrapper (redesk-io) | Unknown | Unknown | Unknown | Unknown |
| Duo | C#/Proprietary | Bundled TermWrap | Proprietary | Active |
| neo_multiseat | PowerShell | RDPWrap automation | MIT | Active |

---

## Сравнение архитектур

### MultiSeat-Extended

```
MultiSeat.Service (SYSTEM)
    ↓
RdpWrapper.EnsureMultiSession()
    ↓ (checks if RDPWrap/TermWrap is active)
SessionLauncher.CreateSessionViaRdpLoopbackAsync()
    ↓
mstsc.exe → 127.0.0.2 → termsrv.dll → new session
```

- **Approach**: Delegates to RDPWrap/TermWrap for concurrent session support
- **Patching**: Does NOT patch termsrv.dll itself
- **Detection**: Checks if multi-session is available via registry/file checks
- **Fallback**: Logs error if RDP Wrapper not detected

### TermWrap (llccd) — C++

```
Service Control Manager
    ↓
TermWrap.dll (replaces/wraps termsrv.dll)
    ↓
Dynamic patching at runtime:
  - DefPolicyPatch (session limits)
  - SingleUserPatch (per-user locks)
  - LocalOnlyPatch (license restrictions)
  - PropertyDevicePatch (PnP redirection)
  - SLPolicyPatch (licensing flags)
```

- **Approach**: DLL proxy that hooks termsrv.dll initialization
- **Patching**: Runtime patching via WriteProcessMemory
- **Offset discovery**: Companion RDPWrapOffsetFinder (PDB symbols)
- **Advantages**: No static .ini files needed
- **Disadvantages**: Still needs PDB symbol server for offset discovery

### TermWrap Rust (kernalix7)

```
termwrap-dll / umwrap-dll / endpwrap-dll
    ↓
pelite (PE parser) + iced-x86 (disassembler)
    ↓
Symbol-free runtime analysis:
  - Scans .rdata strings
  - Parses .pdata exception tables
  - Traces cross-references (xrefs)
  - Performs runtime struct-layout analysis
    ↓
Dynamic instruction emitting:
  - capture target register + struct offsets
  - generate mov/branch instructions on the fly
```

- **Approach**: Symbol-free dynamic analysis
- **Patching**: Same as TermWrap C++ but with runtime discovery
- **Offset discovery**: No PDB needed — analyzes binary directly
- **Advantages**: Survives Windows updates without .ini updates
- **Disadvantages**: Complex implementation
- **Extra features**: UmWrap (camera/USB redirection), EndpWrap (audio recording)

### Duo (Proprietary)

```
TermWrap service (SYSTEM)
    ↓
Bundled TermWrap.dll
    ↓
Custom WDDM display driver
    ↓
UMDF input driver
    ↓
Sunshine per user session
```

- **Approach**: Fully integrated, proprietary stack
- **Patching**: Bundled TermWrap (closed-source version)
- **Extra**: Custom drivers for display + input
- **Advantages**: Seamless, all-in-one
- **Disadvantages**: Closed-source, paid features

### neo_multiseat (PowerShell)

```
neo_multiseat.ps1
    ↓
Automated RDPWrap installation/configuration
    ↓
User account creation
    ↓
RDP file generation
    ↓
Live monitoring
```

- **Approach**: Script automation of existing RDPWrap
- **Patching**: Uses stascorp/rdpwrap .ini files
- **Advantages**: Simple, transparent, automates RDPWrap
- **Disadvantages**: Depends on .ini file updates

---

## Сравнение возможностей

| Feature | MultiSeat | TermWrap C++ | TermWrap Rust | Duo | neo_multiseat |
|---------|-----------|--------------|---------------|-----|---------------|
| Concurrent sessions | YES (via RDPWrap) | YES (self-patching) | YES (self-patching) | YES (bundled) | YES (via RDPWrap) |
| Dynamic offset discovery | NO | PARTIAL (PDB) | YES (symbol-free) | UNKNOWN | NO |
| Windows update resilience | LOW (needs .ini) | MEDIUM (needs PDB) | HIGH (no deps) | HIGH (proprietary) | LOW (needs .ini) |
| Camera/USB redirection | NO | NO | YES (UmWrap) | UNKNOWN | NO |
| Audio recording | NO | NO | YES (EndpWrap) | UNKNOWN | NO |
| ARM64 support | NO | NO | YES | UNKNOWN | NO |
| User-mode only | YES | YES | YES | YES | YES |
| Kernel patches | NO | NO | NO | NO | NO |
| License | MIT | MIT | MIT | Proprietary | MIT |

---

## Что MultiSeat может заимствовать

### From TermWrap Rust

1. **Symbol-free offset discovery** — eliminates .ini file dependency
2. **UmWrap** — camera/USB redirection (useful for seat peripherals)
3. **EndpWrap** — audio recording redirection (could enable microphone)
4. **ARM64 support** — future-proofing

**Difficulty**: HIGH — requires integrating Rust DLL into .NET service
**License**: MIT — compatible
**Priority**: MEDIUM — current RDPWrap works, but .ini maintenance is painful

### From Duo

1. **Bundled TermWrap** — no external dependency
2. **Custom WDDM driver** — better display control
3. **UMDF input driver** — better input isolation
4. **Application Compatibility Layer** — game compatibility

**Difficulty**: IMPOSSIBLE — proprietary, closed-source
**License**: Proprietary — cannot reuse
**Priority**: N/A — study architecture only

### From neo_multiseat

1. **Automated RDPWrap recovery** — "Fix RDP" utility
2. **Live monitoring** — real-time session audit
3. **Tailscale integration** — remote access helper
4. **NLA/TLS hardening** — security templates

**Difficulty**: LOW — PowerShell scripts can be adapted
**License**: MIT — compatible
**Priority**: LOW — MultiSeat has own diagnostics

---

## Рекомендации для MultiSeat-Extended

### Short-term (keep current approach)

- Continue using RDPWrap/TermWrap as external dependency
- RdpWrapper.EnsureMultiSession() checks work well
- No need to bundle or patch termsrv.dll

### Medium-term (consider improvements)

1. **Automated RDPWrap recovery** — detect and warn when .ini is stale
2. **Better offset discovery** — consider TermWrap Rust approach
3. **Live monitoring** — add session audit to dashboard

### Long-term (evaluate alternatives)

1. **Bundle TermWrap** — eliminate external dependency
2. **Symbol-free patching** — survive Windows updates automatically
3. **Audio recording** — enable microphone via EndpWrap equivalent

---

## Заключение

MultiSeat-Extended's RDP approach is **adequate but not optimal**. It depends on external RDPWrap/TermWrap, which requires manual .ini updates after Windows patches. TermWrap Rust's symbol-free approach is technically superior but requires significant integration effort. Duo's proprietary approach is the most seamless but cannot be reused.

**Recommendation**: Keep current approach for now; consider TermWrap Rust integration as a medium-term improvement if .ini maintenance becomes painful.
