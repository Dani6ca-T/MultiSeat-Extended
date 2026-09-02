# License Validation — Phase 7

## Purpose

Verify that all architectural dependencies are license-compatible with MultiSeat-Extended.

---

## Current Dependencies

### From MultiSeat-Extended

| Dependency | License | Usage |
|------------|---------|-------|
| MultiSeat-Extended | MIT | Core application |
| ASP.NET Core | MIT | Web API, Dashboard |
| React | MIT | Dashboard UI |
| SQLite | Public Domain | Persistence |
| .NET 8 | MIT | Runtime |

---

## External Dependencies (Architecture)

### Tier 1: Required (Integrated)

| Component | License | Link/Bundle | Modify | Distribute | Risk |
|-----------|---------|-------------|--------|------------|------|
| SudoVDA | MIT | ❌ (separate install) | N/A | N/A | LOW |
| HidHide | MIT | ❌ (separate install) | N/A | N/A | LOW |
| TermWrap | MIT | ❌ (separate install) | N/A | N/A | LOW |

**EVIDENCE**: All three are MIT licensed, installed independently.

**COMPATIBLE**: No copyleft concerns.

### Tier 2: Optional (Provider)

| Component | License | Link/Bundle | Modify | Distribute | Risk |
|-----------|---------|-------------|--------|------------|------|
| Vibepollo | GPL-3.0 | ❌ (external process) | N/A | N/A | LOW |
| Apollo | GPL-3.0 | ❌ (external process) | N/A | N/A | LOW |
| Sunshine | GPL-3.0 | ❌ (external process) | N/A | N/A | LOW |

**EVIDENCE**: All streaming providers are GPL-3.0.

**CRITICAL**: Providers run as EXTERNAL PROCESSES, not linked into MultiSeat.

**COMPATIBLE**: Invoking external GPL process does not trigger copyleft for MIT host.

**JUSTIFICATION**: MultiSeat does not link, embed, or distribute GPL code. It starts an external process via `CreateProcessAsUser`.

### Tier 3: Potential Future

| Component | License | Risk | Notes |
|-----------|---------|------|-------|
| libvirtualhid | Unknown | UNKNOWN | Need to verify |
| Virtual-Display-Driver | MIT | LOW | Alternative to SudoVDA |
| parsec-vdd | MIT | LOW | Alternative to SudoVDA |

---

## Copyleft Analysis

### Can MultiSeat (MIT) use Vibepollo (GPL)?

**YES** — as external process invocation.

**REASON**: GPL requires derivative work when linking. Starting an external process is not linking.

**PRECEDENT**: Many MIT-licensed tools invoke GPL processes (e.g., git clients invoking git).

### Can MultiSeat bundle Vibepollo?

**NO** — distributing GPL binaries requires GPL for entire distribution.

**REASON**: GPL Section 2 requires complete source for "the whole program."

**MITIGATION**: MultiSeat distributes only its own MIT binaries. Users install providers separately.

### Can MultiSeat use Vibepollo API?

**YES** — API usage does not create derivative work.

**REASON**: API contracts are not copyrightable (Oracle v. Google, though this is contested).

---

## Driver Licensing

| Driver | License | Kernel module? | Implications |
|--------|---------|----------------|--------------|
| SudoVDA | MIT | No (user-mode UMDF) | Safe |
| HidHide | MIT | No (user-mode filter) | Safe |
| TermWrap | MIT | No (DLL proxy) | Safe |

**NOTE**: No kernel-mode drivers are required by the architecture.

---

## License Compatibility Matrix

```
MultiSeat (MIT)
  ├── ASP.NET Core (MIT) ✅
  ├── React (MIT) ✅
  ├── SQLite (Public Domain) ✅
  ├── SudoVDA (MIT, external) ✅
  ├── HidHide (MIT, external) ✅
  ├── TermWrap (MIT, external) ✅
  ├── Vibepollo (GPL, external process) ✅
  └── Apollo (GPL, external process) ✅
```

**ALL COMPATIBLE**.

---

## Attribution Requirements

| Component | Attribution Required | Location |
|-----------|---------------------|----------|
| SudoVDA | Yes (MIT) | About page |
| HidHide | Yes (MIT) | About page |
| TermWrap | Yes (MIT) | About page |
| Vibepollo | Yes (GPL) | NOT REQUIRED (not distributed) |
| Apollo | Yes (GPL) | NOT REQUIRED (not distributed) |

---

## Risks

### LOW RISK
- MIT dependencies: Fully compatible
- External GPL processes: Safe pattern

### MEDIUM RISK
- Unknown licenses (libvirtualhid): Need verification before adoption

### HIGH RISK
- None identified for current architecture

---

## Conclusion

**ALL CURRENT ARCHITECTURAL DEPENDENCIES ARE LICENSE-COMPATIBLE**.

**KEY INSIGHT**: Provider architecture (external process) avoids GPL contamination.

**ACTION NEEDED**: Verify licenses for Tier 3 components before adoption.

---

*Generated: 2026-08-30*
*Status: VERIFIED against repository license files*
