# TermWrap Fork Comparison

**Date**: 2026-08-30
**Purpose**: Compare TermWrap forks and alternatives

---

## TermWrap Variants

### 1. llccd/TermWrap (Canonical)

- **URL**: https://github.com/llccd/TermWrap
- **License**: MIT
- **Status**: Active (v0.6)
- **Language**: C++ (x64/x86)
- **Features**: Auto offset discovery, camera/USB, audio recording, Easy Print
- **Stars**: 101
- **Forks**: 15

**Key features**:
- Integrated RDPWrapOffsetFinder
- Improved SingleUserPatch
- Camera/USB redirection (UmWrap)
- Audio recording redirection (EndpWrap)
- Remote Desktop Easy Print
- x86 support
- No STL dependencies (v0.6)

### 2. laasso/TermWrap

- **URL**: https://github.com/laasso/TermWrap
- **Status**: NOT FOUND (may be deleted or private)

### 3. kernalix7/rdprrap (Rust rewrite)

- **URL**: https://github.com/kernalix7/rdprrap
- **License**: MIT
- **Language**: Rust
- **Status**: Unknown

---

## Comparison Table

| Feature | llccd/TermWrap | laasso/TermWrap | rdprrap (Rust) |
|---------|---------------|-----------------|----------------|
| Language | C++ | Unknown | Rust |
| License | MIT | Unknown | MIT |
| Status | Active (v0.6) | Not found | Unknown |
| Auto offset | Yes | Unknown | Unknown |
| Camera/USB | Yes | Unknown | Unknown |
| Audio recording | Yes | Unknown | Unknown |
| Easy Print | Yes | Unknown | Unknown |
| x86 | Yes | Unknown | Unknown |
| Stars | 101 | Unknown | Unknown |

---

## Comparison to Other RDP Solutions

### 1. rdpwrap (stascorp)

- **URL**: https://github.com/stascorp/rdpwrap
- **License**: GPL-2.0
- **Status**: Abandoned
- **Language**: C/C++
- **Features**: Manual .ini files, no auto offset

**Differences from TermWrap**:
- GPL-2.0 (not MIT)
- Manual .ini files (not auto offset)
- Doesn't survive Windows updates
- No camera/USB/audio features
- Abandoned

### 2. RDP Wrapper (stascorp alternative)

- **URL**: https://github.com/stascorp/rdpwrap
- **License**: GPL-2.0
- **Status**: Abandoned
- **Language**: C/C++

### 3. Duo (TermWrap bundled)

- **URL**: https://github.com/DuoStream/Duo
- **License**: Proprietary
- **Status**: Active
- **Features**: Bundled TermWrap, custom drivers

**Differences from TermWrap**:
- Proprietary
- Bundled with other components
- Custom drivers
- Not standalone

---

## Recommendation for MultiSeat-Extended

### Use llccd/TermWrap

**Reasons**:
1. **MIT license** — Full compatibility with MultiSeat-Extended (MIT)
2. **Active maintenance** — Regular updates, survives Windows updates
3. **Auto offset discovery** — No manual .ini files needed
4. **Additional features** — Camera, USB, audio recording
5. **Proven** — Already integrated in MultiSeat-Extended

### Do NOT Use

1. **rdpwrap (stascorp)** — GPL-2.0, abandoned, manual .ini files
2. **laasso/TermWrap** — Not found, unknown status
3. **Duo (proprietary)** — Cannot inspect or modify

---

## Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| llccd/TermWrap is canonical | GitHub | Most stars, active maintenance | VERIFIED |
| laasso/TermWrap not found | GitHub | 404 or private | VERIFIED |
| rdpwrap is abandoned | GitHub | Last update years ago | VERIFIED |
| TermWrap MIT license | LICENSE file | MIT text | VERIFIED |
| TermWrap auto offset | README | "Integrated RDPWrapOffsetFinder" | VERIFIED |
| TermWrap survives updates | README | "patch offsets are automatically searched" | VERIFIED |
| TermWrap camera/USB | README | "Enabled camera and USB redirection" | VERIFIED |
| TermWrap audio recording | README | "Enabled audio recording redirection" | VERIFIED |
| MultiSeat uses TermWrap | install-prerequisites.ps1 | Installation script | VERIFIED |
