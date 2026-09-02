# Display Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define display management architecture, backend abstraction, and lifecycle.

---

## Display Backend Abstraction

### IDisplayBackend (Conceptual)

| Operation | Description |
|-----------|-------------|
| CreateDisplay | Create virtual display |
| DestroyDisplay | Remove virtual display |
| SetResolution | Set display resolution |
| SetRefreshRate | Set display refresh rate |
| EnableHdr | Enable HDR (future) |
| GetDisplayId | Get display identifier |

**DECISION**: Display backend is abstracted via IDisplayBackend.

---

## Current Backend: SudoVDA

### Architecture

```
MultiSeat.Service
    │
    ├── VirtualDisplayManager
    │       │
    │       ├── CreateDisplayAsync (SudoVDA IPC)
    │       ├── DestroyDisplayAsync (SudoVDA IPC)
    │       └── ApplyDisplayIsolation (helper.exe)
    │
    └── SudoVDA Driver (IddCx, kernel-mode)
            │
            └── Virtual monitor
```

### Capabilities

| Capability | Status | Evidence |
|------------|--------|----------|
| Virtual display creation | ✅ | VirtualDisplayManager |
| Resolution setting | ✅ | RdpGeometry |
| Refresh rate | ✅ | --set-display-hz helper |
| HDR | ⚠️ | SudoVDA v0.5+ (license unknown) |
| Hotplug | ⚠️ | TryLateDisplayDetectionAsync |
| Multi-monitor | ❌ | Single display per seat |

**FACT**: VirtualDisplayManager uses SudoVDA IPC.

---

## Display Lifecycle

### Creation

```
1. VirtualDisplayManager.CreateDisplayAsync(seat)
   └── SudoVDA IPC → creates virtual monitor
2. Vibepollo starts, enumerates displays
3. Vibepollo writes display UUID to log
4. MultiSeat parses UUID from log
5. UUID written to sunshine.conf as output_name
```

### Isolation

```
1. --setup-display-isolation (helper.exe)
   └── SudoVDA becomes primary display
   └── RDP display shrunk to 640x480
2. --set-display-hz (helper.exe)
   └── Clamps refresh rate to seat.Fps
```

### Late Detection

```
1. Health check detects no display UUID
2. TryLateDisplayDetectionAsync
   └── Reads Vibepollo log for latest display block
   └── Parses UUID from last block
3. If found → apply isolation
4. If not → continue monitoring
```

**FACT**: SeatManager.TryLateDisplayDetectionAsync handles late display.

---

## Display Isolation

### SudoVDA Primary + RDP Shrunk

```
Before isolation:
├── RDP display (1920x1080) ← primary
└── SudoVDA display (1920x1080) ← secondary

After isolation:
├── SudoVDA display (1920x1080) ← primary
└── RDP display (640x480) ← shrunk
```

### Why This Works

- Vibepollo captures SudoVDA (primary)
- RDP display is shrunk (minimal CPU)
- True isolation between seats

**FACT**: SeatManager.ApplyDisplayIsolationAsync implements this.

---

## Display State

### Source of Truth

| Property | Owner | Location |
|----------|-------|----------|
| Display UUID | SudoVDA driver | sunshine_state.json |
| Display assignment | MultiSeat | SeatInfo.DisplayDevicePath |
| Display resolution | MultiSeat | SeatInfo.Width, Height |
| Display refresh rate | MultiSeat | SeatInfo.Fps |
| Display isolation | MultiSeat | helper.exe |

**DECISION**: MultiSeat is source of truth for display assignment.

---

## Future: HDR Enablement

### Gap

EnableHdr is no-op in current implementation.

### What's Needed

1. SudoVDA v0.5+ with HDR EDID
2. Force Windows to rebuild VidPN source mode
3. D3DKMTSetVidPnSourceOwner + D3DKMTSetDisplayMode
4. Vibepollo HDR encoding support

### Status

**OPEN QUESTION**: SudoVDA license terms unknown.

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| VirtualDisplayManager uses SudoVDA | VirtualDisplayManager | FACT |
| Display UUID from Vibepollo log | SeatManager.TryLateDisplayDetectionAsync | FACT |
| Isolation shrinks RDP display | ApplyDisplayIsolationAsync | FACT |
| Late detection retries | TryLateDisplayDetectionAsync | FACT |
| EnableHdr is no-op | MultiSeatOptions.cs | FACT |
