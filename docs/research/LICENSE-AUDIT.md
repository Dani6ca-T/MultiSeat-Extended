# MultiSeat-Extended: Аудит лицензий (License Audit)

## Исследованные проекты

### A1: MultiSeat-Extended (Dani6ca-T/MultiSeat-Extended)

- **License**: MIT
- **Copyright**: vibesoftwarecoder (original MultiSeat)
- **Attribution**: Include MIT license and copyright notice
- **Redistribution**: Permissive — can redistribute in binary and source form
- **Copyleft**: No — modifications do not require source disclosure
- **Linking restrictions**: None
- **Driver licensing**: SudoVDA (separate license, see below)
- **Third-party dependencies**: Nefarius.ViGEm.Client (MIT), .NET 9 (MIT), React (MIT)
- **Compatibility with MultiSeat-Extended**: ✅ Same project

### A2: MultiSeat upstream (vibesoftwarecoder/MultiSeat)

- **License**: MIT
- **Copyright**: vibesoftwarecoder
- **Attribution**: Include MIT license and copyright notice
- **Redistribution**: Permissive
- **Copyleft**: No
- **Linking restrictions**: None
- **Driver licensing**: SudoVDA (separate)
- **Third-party dependencies**: Same as MultiSeat-Extended
- **Compatibility**: ✅ Fork base — full compatibility

---

### B1: Vibepollo (Nonary/Vibepollo)

- **License**: GPLv3
- **Copyright**: Chase Payne (Nonary) + upstream Sunshine/Apollo
- **Attribution**: Must include GPLv3 license and copyright notices
- **Redistribution**: Must distribute source code of GPLv3 components
- **Copyleft**: YES — modifications to Vibepollo code must be released under GPLv3
- **Linking restrictions**: GPLv3 linking implications — if MultiSeat links statically, may need GPLv3 for combined work
- **Driver licensing**: SudoVDA driver has separate license
- **Third-party dependencies**: Sunshine (GPLv3), various NVIDIA SDKs (proprietary terms)
- **Compatibility with MultiSeat-Extended**: ⚠️ Complex — see analysis below

**GPLv3 Compatibility Analysis:**
- MultiSeat-Extended is MIT
- Vibepollo is GPLv3
- If MultiSeat-Extended distributes Vibepollo as a separate process (not linked), no copyleft trigger
- MultiSeat-Extended currently launches Vibepollo as a child process — this is safe
- If MultiSeat-Extended were to embed Vibepollo code directly, GPLv3 would apply to the combined work
- **Recommendation**: Keep Vibepollo as an external process, not linked code

### B2: Helios (MintCapybara924/Helios-Sunshine-Manager)

- **License**: GPLv3
- **Copyright**: MintCapybara924
- **Attribution**: Must include GPLv3 license and copyright notices
- **Redistribution**: Must distribute source code
- **Copyleft**: YES
- **Linking restrictions**: GPLv3 linking implications
- **Driver licensing**: No custom drivers
- **Third-party dependencies**: .NET 8 (MIT), WPF (MIT)
- **Compatibility with MultiSeat-Extended**: ⚠️ Same analysis as Vibepollo — keep as external process

### B3: Apollo (ClassicOldSong/Apollo)

- **License**: GPLv3
- **Copyright**: ClassicOldSong
- **Attribution**: Must include GPLv3 license and copyright notices
- **Redistribution**: Must distribute source code
- **Copyleft**: YES
- **Linking restrictions**: GPLv3 linking implications
- **Driver licensing**: SudoVDA (separate)
- **Third-party dependencies**: Sunshine (GPLv3)
- **Compatibility with MultiSeat-Extended**: ⚠️ Same analysis — external process only

### B4: Apollo Multi Instance Launcher (neo0oen619/apollo-multi-instance-launcher)

- **License**: MIT ("feel free to use it as you wish humans :]")
- **Copyright**: neo0oen619
- **Attribution**: Include MIT license
- **Redistribution**: Permissive
- **Copyleft**: No
- **Compatibility**: ✅ Full compatibility

---

### C1: Duo (DuoStream/Duo)

- **License**: Proprietary / Custom Freemium
- **Copyright**: DuoStream / Black-Seraph
- **Attribution**: Required for binary distribution
- **Redistribution**: NOT allowed — closed source
- **Copyleft**: N/A — proprietary
- **Linking restrictions**: N/A — cannot link
- **Driver licensing**: Custom WDDM + UMDF drivers (proprietary)
- **Third-party dependencies**: Sunshine (GPLv3) — potential license conflict with proprietary distribution
- **Compatibility with MultiSeat-Extended**: ❌ Cannot reuse any code

**Note**: Duo's use of GPL-licensed Sunshine in a proprietary product raises license compliance questions. This is a known issue in the community.

---

### D1: TermWrap (llccd/TermWrap)

- **License**: MIT
- **Copyright**: llccd
- **Attribution**: Include MIT license
- **Redistribution**: Permissive
- **Copyleft**: No
- **Linking restrictions**: None
- **Driver licensing**: No custom drivers (user-mode DLL only)
- **Third-party dependencies**: dbghelp.dll (Windows SDK)
- **Compatibility with MultiSeat-Extended**: ✅ Full compatibility

### D2: TermWrap Rust fork (kernalix7/rdprrap)

- **License**: MIT (assumed from TermWrap lineage)
- **Copyright**: kernalix7
- **Attribution**: Include MIT license
- **Redistribution**: Permissive
- **Copyleft**: No
- **Compatibility**: ✅ Full compatibility

### D3: rdpWrapper (redesk-io/rdpWrapper)

- **License**: Unable to verify — repository may be private or removed
- **Compatibility**: ❓ UNKNOWN

---

### E1: neo_multiseat (neo0oen619/neo_multiseat)

- **License**: MIT
- **Copyright**: neo0oen619
- **Attribution**: Include MIT license
- **Redistribution**: Permissive
- **Copyleft**: No
- **Compatibility**: ✅ Full compatibility

### E2: MultiseatProject (Abdulhanan535/MultiseatProject)

- **License**: Repository not found / does not exist
- **Compatibility**: ❓ UNKNOWN

---

### F1: LuaTools (madoiscool/LuaTools)

- **License**: Not clearly stated in repository
- **Copyright**: madoiscool
- **Compatibility**: ❌ NOT RECOMMENDED — DRM bypass tools, unclear licensing

---

### Additional Projects

### virtual-display-rs (MolotovCherry/virtual-display-rs)

- **License**: Open Source (see repository)
- **Copyright**: MolotovCherry
- **Compatibility**: ⚠️ Potential alternative to SudoVDA — needs license verification

---

## Сводка совместимости

| Project | License | Copyleft | Can Reuse Code | Can Link | Can Distribute |
|---------|---------|----------|----------------|----------|----------------|
| MultiSeat-Extended | MIT | No | ✅ | ✅ | ✅ |
| MultiSeat upstream | MIT | No | ✅ | ✅ | ✅ |
| Vibepollo | GPLv3 | YES | ⚠️ External process only | ⚠️ | ⚠️ Must provide source |
| Helios | GPLv3 | YES | ⚠️ External process only | ⚠️ | ⚠️ Must provide source |
| Apollo | GPLv3 | YES | ⚠️ External process only | ⚠️ | ⚠️ Must provide source |
| Apollo launcher | MIT | No | ✅ | ✅ | ✅ |
| Duo | Proprietary | N/A | ❌ | ❌ | ❌ |
| TermWrap | MIT | No | ✅ | ✅ | ✅ |
| neo_multiseat | MIT | No | ✅ | ✅ | ✅ |
| LuaTools | Unknown | N/A | ❌ | ❌ | ❌ |
| virtual-display-rs | Open Source | ❓ | ⚠️ Verify | ⚠️ | ⚠️ |

---

## Ключевые выводы

### 1. GPLv3 projects (Vibepollo, Helios, Apollo)

- MultiSeat-Extended (MIT) can coexist with GPLv3 processes
- Launching Vibepollo as a child process does NOT trigger copyleft
- Embedding GPLv3 code into MultiSeat-Extended WOULD trigger copyleft
- **Safe pattern**: IPC/process boundary between MIT and GPLv3 components

### 2. Proprietary projects (Duo)

- Cannot reuse any code
- Can study architecture and design patterns (not copyrightable)
- Can implement same functionality independently

### 3. MIT projects (TermWrap, neo_multiseat, Apollo launcher)

- Full compatibility with MultiSeat-Extended
- Can reuse code directly with attribution
- Can modify and redistribute

### 4. SudoVDA driver

- License needs separate verification (not in main repos)
- MultiSeat-Extended already depends on it
- Critical dependency — cannot replace easily

### 5. Recommendations

- Keep Vibepollo/Helios/Apollo as external processes (no code linking)
- Prefer MIT-licensed alternatives when available
- Document all third-party licenses in NOTICES file
- Verify SudoVDA driver license independently
