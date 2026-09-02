# TermWrap Architecture — Source-Level Analysis

**Date**: 2026-08-30
**Purpose**: Detailed architecture analysis of TermWrap RDP patching solution

---

## Repository

- **URL**: https://github.com/llccd/TermWrap
- **License**: MIT
- **Language**: C++ (x64/x86)
- **Status**: Active (v0.6)
- **Author**: llccd
- **Fork relationship**: Rewrite of stascorp/rdpwrap (NOT a fork)
- **Stars**: 101
- **Forks**: 15

---

## What TermWrap Is

TermWrap is a DLL proxy that patches `termsrv.dll` at runtime to enable concurrent RDP sessions on Windows Home/Professional editions. It is a rewrite of the original rdpwrap (stascorp/rdpwrap) with automatic offset discovery.

---

## Key Features

### 1. Integrated RDPWrapOffsetFinder

**Source**: README

- Patch offsets are automatically searched
- No .ini files needed
- Survives Windows updates
- Uses PDB symbols for offset discovery

### 2. Improved SingleUserPatch

**Source**: README

- Patches all two possible locations
- More reliable than original rdpwrap

### 3. Camera and USB Redirection

**Source**: README

- Enabled for all SKUs by additional wrap of `UmRdpService`
- Requires UmWrap (server and home editions only)
- Professional/Enterprise editions already have features enabled

### 4. Audio Recording Redirection

**Source**: README

- Enabled for all SKUs by additional wrap of `rdpendp.dll`
- Requires EndpWrap (server and home editions only)
- Gets loaded in all applications that play/record remote audio
- May cause some applications to crash

### 5. Remote Desktop Easy Print

**Source**: Release v0.4

- Enabled in v0.4

### 6. x86 Support

**Source**: Release v0.3

- Added in v0.3

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────┐
│                TermWrap                          │
│  ┌───────────────────────────────────────────┐  │
│  │  TermWrap.dll (DLL proxy)                 │  │
│  │  ├── Proxies termsrv.dll                  │  │
│  │  ├── Patches at runtime                   │  │
│  │  ├── Auto offset discovery (PDB symbols)  │  │
│  │  └── Survives Windows updates             │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  UmWrap.dll (optional)                    │  │
│  │  ├── Wraps UmRdpService.dll               │  │
│  │  ├── Enables camera redirection           │  │
│  │  ├── Enables USB redirection              │  │
│  │  └── Server/Home editions only            │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  EndpWrap.dll (optional)                  │  │
│  │  ├── Wraps rdpendp.dll                    │  │
│  │  ├── Enables audio recording redirection  │  │
│  │  ├── Server/Home editions only            │  │
│  │  └── May cause app crashes                │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  Zydis.dll (disassembler)                 │  │
│  │  └── Used for offset discovery            │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────┐
│         Windows Terminal Services                │
│  ┌───────────────────────────────────────────┐  │
│  │  termsrv.dll                              │  │
│  │  ├── DefPolicyPatch (concurrent sessions) │  │
│  │  ├── SingleUserPatch (single user)        │  │
│  │  ├── LocalOnlyPatch (local only)          │  │
│  │  └── Other patches                        │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  UmRdpService.dll (optional wrap)         │  │
│  │  └── Camera/USB redirection               │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  rdpendp.dll (optional wrap)              │  │
│  │  └── Audio recording redirection          │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

---

## Installation

**Source**: README

### Files to Copy

```
%ProgramFiles%\RDP Wrapper\
├── TermWrap.dll       # Main DLL proxy
├── UmWrap.dll         # Optional: camera/USB redirection
├── EndpWrap.dll       # Optional: audio recording redirection
├── Zydis.dll          # Disassembler for offset discovery
├── Install_termwrap_umwrap.reg    # Registry (with UmWrap)
└── Install_termwrap_only.reg      # Registry (without UmWrap)
```

### Registry Entries

**Install_termwrap_umwrap.reg** (with UmWrap):
- Registers TermWrap.dll as termsrv.dll proxy
- Registers UmWrap.dll as UmRdpService.dll proxy

**Install_termwrap_only.reg** (without UmWrap):
- Registers TermWrap.dll as termsrv.dll proxy only

### Uninstall

1. Merge `Revert_to_default.reg`
2. Reboot system
3. Delete files in `%ProgramFiles%\RDP Wrapper`

---

## How It Works

### DLL Proxying

TermWrap works by placing itself in the DLL search path before `termsrv.dll`. When Windows tries to load `termsrv.dll`, it loads TermWrap instead. TermWrap then:

1. Loads the original `termsrv.dll`
2. Patches specific locations at runtime
3. Forwards all calls to the original DLL

### Auto Offset Discovery

**Source**: README

- Integrated RDPWrapOffsetFinder
- Uses PDB symbols from Microsoft Symbol Server
- Automatically finds patch offsets
- No .ini files needed
- Survives Windows updates

### Patching Mechanism

TermWrap patches the following in `termsrv.dll`:

1. **DefPolicyPatch** — Enables concurrent sessions
2. **SingleUserPatch** — Patches all two possible locations
3. **LocalOnlyPatch** — Removes local-only restriction
4. **Other patches** — Various RDP restrictions

---

## Windows Version Compatibility

**Source**: Releases

| Version | Date | Changes |
|---------|------|---------|
| v0.1 | 2024-02-05 | First release |
| v0.2 | 2024-06-12 | Fix PropertyAddr not found on 10.0.22621.3374 |
| v0.3 | 2024-10-07 | Add x86 support |
| v0.4 | 2024-06-30 | Enable Remote Desktop Easy Print |
| v0.5 | 2024-12-23 | Read redirection settings in registry; Fix total failure on 10.0.26100.7523 |
| v0.6 | 2025-05-26 | Support new DefPolicyPatch using register r9d; Remove STL dependencies |

**Supported Windows versions**:
- Windows 10 (various builds)
- Windows 11 (various builds)
- Auto-discovery means it should work with future updates

---

## Security Implications

### Privileges Required

- Administrator privileges to install
- Modifies system DLLs (termsrv.dll proxy)
- Modifies registry entries

### DLL Proxying Risks

- Could be detected as malware by antivirus
- Modifies system behavior
- Requires careful DLL search order manipulation

### Mitigations

- User-mode only (no kernel patches)
- Transparent behavior (forwards all calls)
- Easy to uninstall (revert registry, delete files)

---

## Comparison to Original rdpwrap

| Aspect | rdpwrap (stascorp) | TermWrap (llccd) |
|--------|-------------------|------------------|
| License | GPL-2.0 | MIT |
| Offset discovery | Manual (.ini files) | Automatic (PDB symbols) |
| Survives updates | No (needs .ini update) | Yes (auto-discovery) |
| Camera/USB | No | Yes (UmWrap) |
| Audio recording | No | Yes (EndpWrap) |
| Easy Print | No | Yes (v0.4) |
| x86 support | Yes | Yes (v0.3+) |
| Maintenance | Abandoned | Active |
| Language | C/C++ | C++ |

---

## Relevance to MultiSeat-Extended

### Already Integrated

**Source**: install-prerequisites.ps1

- MultiSeat-Extended installs TermWrap v0.6
- No changes needed
- Proven, MIT licensed

### Benefits

1. **Auto offset discovery** — Survives Windows updates
2. **User-mode only** — Safer than kernel patches
3. **MIT license** — Full compatibility
4. **Active maintenance** — Regular updates
5. **Additional features** — Camera, USB, audio recording

### Limitations

1. **PDB dependency** — Needs symbol server for offset discovery
2. **Single purpose** — Only handles RDP patching
3. **Registry modification** — Changes system state
4. **DLL proxying** — Could be detected as malware

---

## Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| DLL proxy for termsrv.dll | README | "Copy the dlls" | VERIFIED |
| Auto offset discovery | README | "Integrated RDPWrapOffsetFinder" | VERIFIED |
| Survives Windows updates | README | "patch offsets are automatically searched" | VERIFIED |
| Camera/USB redirection | README | "Enabled camera and USB redirection" | VERIFIED |
| Audio recording redirection | README | "Enabled audio recording redirection" | VERIFIED |
| Easy Print support | Release v0.4 | "Enable Remote Desktop Easy Print" | VERIFIED |
| x86 support | Release v0.3 | "Add x86 support" | VERIFIED |
| MIT license | LICENSE file | MIT text | VERIFIED |
| Active maintenance | Releases | v0.6 (2025-05-26) | VERIFIED |
| Windows 10/11 support | README | "Windows 10+" | VERIFIED |
| Registry modification | README | "merge .reg files" | VERIFIED |
| User-mode only | README | No kernel patches mentioned | VERIFIED |
