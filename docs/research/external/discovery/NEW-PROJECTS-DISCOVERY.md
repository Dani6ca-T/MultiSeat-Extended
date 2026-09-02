# New Projects Discovery

**Date**: 2026-08-30
**Purpose**: Document projects found during Windows low-level technology research

---

## Projects Already Known

These were already in the original research:
- MultiSeat-Extended (Dani6ca-T/MultiSeat-Extended)
- MultiSeat upstream (vibesoftwarecoder/MultiSeat)
- Vibepollo (Nonary/Vibepollo)
- Duo (DuoStream/Duo) — proprietary
- Helios (MintCapybara924/Helios-Sunshine-Manager)
- Apollo (ClassicOldSong/Apollo)
- TermWrap (llccd/TermWrap)
- TermWrap Rust (kernalix7/rdprrap)
- rdpWrapper (redesk-io/rdpWrapper) — unknown
- neo_multiseat (neo0oen619/neo_multiseat)
- MultiseatProject (Abdulhanan535/MultiseatProject) — not found
- LuaTools (madoiscool/LuaTools)
- virtual-display-rs (MolotovCherry/virtual-display-rs)
- Apollo Multi Instance Launcher (neo0oen619/apollo-multi-instance-launcher)

---

## New Projects Discovered

### 1. VirtualDrivers/Virtual-Display-Driver

| Field | Value |
|-------|-------|
| **URL** | https://github.com/VirtualDrivers/Virtual-Display-Driver |
| **What it does** | Creates virtual monitors in Windows using IddCx |
| **Why relevant** | Alternative to SudoVDA for virtual displays |
| **License** | Not explicitly stated |
| **Technology** | IddCx UMDF driver |
| **Potential value** | HIGH — mature, popular, supports HDR |

### 2. SudoMaker/SudoVDA

| Field | Value |
|-------|-------|
| **URL** | https://github.com/SudoMaker/SudoVDA |
| **What it does** | Virtual display driver for Windows |
| **Why relevant** | Already used by Apollo/Vibepollo/MultiSeat |
| **License** | Not explicitly stated |
| **Technology** | IddCx UMDF driver |
| **Potential value** | ALREADY IN USE |

### 3. nomi-san/parsec-vdd

| Field | Value |
|-------|-------|
| **URL** | https://github.com/nomi-san/parsec-vdd |
| **What it does** | Virtual display for game streaming |
| **Why relevant** | Alternative IddCx implementation |
| **License** | Not explicitly stated |
| **Technology** | IddCx API |
| **Potential value** | LOW — less active |

### 4. LizardByte/libvirtualhid

| Field | Value |
|-------|-------|
| **URL** | https://github.com/LizardByte/libvirtualhid |
| **What it does** | Virtual HID gamepad/keyboard/mouse via UMDF2 + VHF |
| **Why relevant** | Modern ViGEmBus replacement, used by Sunshine |
| **License** | Custom (license required for Windows driver) |
| **Technology** | UMDF2 + Virtual HID Framework |
| **Potential value** | HIGH — modern, active, used by Sunshine |

### 5. nomi-san/void-drivers

| Field | Value |
|-------|-------|
| **URL** | https://github.com/nomi-san/void-drivers |
| **What it does** | UMDF virtual HID devices |
| **Why relevant** | Alternative virtual input device implementation |
| **License** | Not explicitly stated |
| **Technology** | UMDF + VHF |
| **Potential value** | MEDIUM — newer, less proven |

### 6. VirtualDrivers/Virtual-Audio-Driver

| Field | Value |
|-------|-------|
| **URL** | https://github.com/VirtualDrivers/Virtual-Audio-Driver |
| **What it does** | Virtual speaker + microphone for Windows |
| **Why relevant** | Alternative audio device for streaming |
| **License** | Not explicitly stated |
| **Technology** | WDM virtual audio device |
| **Potential value** | LOW — RDP Remote Audio already works |

### 7. Tylemagne/PC-Terminalizer

| Field | Value |
|-------|-------|
| **URL** | https://github.com/Tylemagne/PC-Terminalizer |
| **What it does** | Free open-source multi-seat tool for Windows/Linux |
| **Why relevant** | Alternative multiseat approach |
| **License** | Not explicitly stated |
| **Technology** | PowerShell scripts |
| **Potential value** | LOW — simpler approach, less featured |

### 8. adyusuf/Virtual-Display-Driver

| Field | Value |
|-------|-------|
| **URL** | https://github.com/adyusuf/Virtual-Display-Driver |
| **What it does** | IddCx virtual display driver |
| **Why relevant** | Fork/variant of Virtual-Display-Driver |
| **License** | Not explicitly stated |
| **Technology** | IddCx UMDF driver |
| **Potential value** | LOW — derivative work |

### 9. rdp/virtual-audio-capture-grabber

| Field | Value |
|-------|-------|
| **URL** | https://github.com/rdp/virtual-audio-capture-grabber-device |
| **What it does** | Virtual audio capture device |
| **Why relevant** | Audio capture alternative |
| **License** | Not explicitly stated |
| **Technology** | Virtual audio device |
| **Potential value** | LOW — RDP Remote Audio already works |

### 10. xaviersykora/vmulti-win11

| Field | Value |
|-------|-------|
| **URL** | https://github.com/xaviersykora/vmulti-win11 |
| **What it does** | Windows 11 compatible Virtual Multiple HID Driver |
| **Why relevant** | Multitouch, mouse, digitizer, keyboard, joystick |
| **License** | Not explicitly stated |
| **Technology** | UMDF HID driver |
| **Potential value** | MEDIUM — multiple HID interfaces |

---

## Projects NOT Found (Confirmed Missing)

- **HydraSeat** — No such project found
- **MultiseatProject (Abdulhanan535)** — Repository not found
- **rdpWrapper (redesk-io)** — Repository may be private/removed

---

## Summary

| Category | New Projects Found | Already Known |
|----------|-------------------|---------------|
| Virtual Display | 4 | 2 |
| Input/HID | 3 | 2 |
| Audio | 2 | 0 |
| RDP/Session | 0 | 3 |
| Multiseat | 1 | 3 |
| **Total** | **10** | **10** |

### Most Valuable Discoveries

1. **libvirtualhid** — Modern ViGEmBus replacement, used by Sunshine
2. **Virtual-Display-Driver** — Mature, popular alternative to SudoVDA
3. **void-drivers** — Newer UMDF virtual HID implementation
4. **vmulti-win11** — Multiple HID interfaces in one driver

### Key Insight

The Windows multiseat ecosystem is fragmented:
- **Display**: Multiple IddCx implementations (SudoVDA, Virtual-Display-Driver)
- **Input**: ViGEmBus being replaced by libvirtualhid
- **Audio**: RDP Remote Audio is the standard approach
- **RDP**: TermWrap is the standard for concurrent sessions

MultiSeat-Extended already uses the best available open-source components. The main gaps are:
1. HDR support (driver + encoding)
2. Game compatibility (process patching)
3. Steam multi-instance
4. Seamless display adjustment
