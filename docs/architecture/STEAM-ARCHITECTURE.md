# Steam Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define Steam integration boundaries and limitations.

---

## Steam Integration Boundaries

### What MultiSeat Can Control

| Aspect | Control | Method |
|--------|---------|--------|
| Steam library location | ✅ | SharedGameLibrary (icacls) |
| Steam launch arguments | ✅ | LaunchApp configuration |
| Steam process tracking | ✅ | ProcessTracker (target) |
| Steam process cleanup | ✅ | Job Object |

### What Steam Controls

| Aspect | Control | Limitation |
|--------|---------|------------|
| Steam client mutex | ❌ | Prevents multiple instances |
| Steam userdata | ❌ | Per-account, locked |
| Steam IPC | ❌ | Named pipes, proprietary |
| Steam cloud sync | ❌ | Per-account |

### What Cannot Be Safely Controlled

| Aspect | Risk | Reason |
|--------|------|--------|
| Steam client patching | HIGH | TOS violation, ban risk |
| Mutex manipulation | HIGH | Anti-cheat detection |
| Steamworks SDK patching | HIGH | Proprietary, ban risk |

---

## Steam Multi-Instance

### Problem

Steam client uses named mutex to prevent multiple instances.

### Current State

| Approach | Status | Feasibility |
|----------|--------|-------------|
| Separate userdata | Unknown | Needs testing |
| Separate installations | Possible | Wasteful |
| Process patching | Risky | TOS violation |
| --userdatadir flag | Unknown | Needs testing |

### Recommendation

**DO NOT BUILD** Steam multi-instance without thorough research.

**DECISION**: Steam multi-instance is P3 (research needed).

---

## Shared Game Library

### How It Works

```
C:\MultiSeatGames\
├── SteamLibrary\
│   └── steamapps\
│       └── common\
│           └── [games]
└── ROMs\
    └── [ROM files]
```

### Permissions

```csharp
icacls "C:\MultiSeatGames" /grant BUILTIN\Users:(OI)(CI)M
```

### Behavior

- All seat accounts can read/write
- Steam library shared across seats
- ROMs shared across seats
- One download, multiple plays

**FACT**: SharedGameLibrary uses icacls for permissions.

---

## Steam Process Management

### Tracking

```
Steam.exe (PID 1234)
├── steam.exe (child)
├── steamservice.exe
└── game.exe (launched game)
```

### Cleanup

```
1. Kill game.exe (graceful)
2. Kill steam.exe (graceful)
3. Job Object ensures cleanup
4. Remove from ProcessTracker
```

---

## Steam Configuration

### Per-Seat Steam

```
Seat 1
├── C:\Users\Seat1\AppData\Roaming\Steam\
│   └── [Steam userdata]
└── C:\MultiSeatGames\SteamLibrary\
    └── [Shared games]

Seat 2
├── C:\Users\Seat2\AppData\Roaming\Steam\
│   └── [Steam userdata]
└── C:\MultiSeatGames\SteamLibrary\
    └── [Shared games]
```

### Key Point

Each seat has its own Steam userdata (via Windows account), but shares the game library.

---

## Limitations

### Known Limitations

1. **No Steam multi-instance** — One Steam client per seat
2. **No shared Steam userdata** — Each seat has own userdata
3. **No Steam cloud sync** — Per-account, not shared
4. **No Steam IPC manipulation** — Proprietary

### Accepted Limitations

1. **Game installation** — Each seat may need to "install" shared games
2. **Steam updates** — Each seat updates independently
3. **Steam login** — Each seat logs in independently

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| SharedGameLibrary exists | SeatManager.cs | FACT |
| icacls grants BUILTIN\Users | SharedGameLibrary | FACT |
| Steam uses mutex | Steam client analysis | INFERENCE |
| No Steam multi-instance | Codebase search | FACT (absent) |
| No Steam IPC manipulation | Research | FACT (absent) |
