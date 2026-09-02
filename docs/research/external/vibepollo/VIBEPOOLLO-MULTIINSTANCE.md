# Vibepollo Multi-Instance Analysis

**Repository**: Nonary/Vibepollo
**Based on**: Source-level analysis of architecture.md, source code, releases
**Date**: 2026-08-30

---

## Current State: Single Instance

Vibepollo is designed as a **single-instance streaming server**. Evidence:

1. **Single process**: `src/main.cpp` creates one daemon process
2. **Single config**: `config::parse()` reads one `sunshine.conf`
3. **Single port block**: `net::map_port()` uses one base port
4. **Single display**: Virtual display management assumes one display target
5. **Single session**: Classic RTSP and WebRTC sessions are mutually exclusive

**Multi-instance is NOT built into Vibepollo.** It is an external orchestration concern.

---

## What Blocks Multi-Instance

### 1. Port Conflicts
**Problem**: Vibepollo uses hardcoded port offsets from `sunshine.port`:
```
sunshine.port + 0  = HTTP (config)
sunshine.port + 1  = HTTPS (Web UI)
sunshine.port - 5  = GFE HTTPS (Moonlight pairing)
sunshine.port + 9  = Video RTP
sunshine.port + 10 = Control ENet
sunshine.port + 11 = Audio RTP
sunshine.port + 12 = Mic RTP
sunshine.port + 26 = RTSP
```

**Solution**: MultiSeat-Extended assigns each seat a 30-port block:
```
Seat 0: 48100-48129 (sunshine.port = 48100)
Seat 1: 48130-48159 (sunshine.port = 48130)
Seat 2: 48160-48189 (sunshine.port = 48160)
...
```

### 2. Config Directory
**Problem**: Vibepollo reads config from current directory or `--config` flag.
- `sunshine.conf` — main config
- `sunshine_state.json` — UUID, pairing state
- `apps.json` — app list
- `credentials.json` — web UI login

**Solution**: MultiSeat-Extended generates per-seat config directories:
```
C:\ProgramData\MultiSeat\vibepollo\
├── MultiSeatSeat01/
│   ├── sunshine.conf
│   └── config/
│       ├── sunshine_state.json
│       └── apps.json
├── MultiSeatSeat02/
│   ├── sunshine.conf
│   └── config/
│       ├── sunshine_state.json
│       └── apps.json
└── shared_credentials.json
```

### 3. Display Target
**Problem**: Vibepollo's `output_name` config key points to one display.
- Multiple Vibepollo instances would all try to capture the same display.

**Solution**: MultiSeat-Extended:
1. Lets Vibepollo create its own virtual display (bundled driver)
2. Parses Vibepollo log to discover the SudoVDA UUID
3. Writes the UUID to `output_name` in the per-seat config
4. Restarts Vibepollo with the correct display target

### 4. Audio Device
**Problem**: Vibepollo captures from one audio device.
- Multiple instances would all capture the same device.

**Solution**: MultiSeat-Extended uses PerSession audio:
- Each RDP session has its own "Remote Audio" endpoint
- Vibepollo captures the session's own endpoint
- No VAC/VoiceMeeter needed

### 5. Pairing State
**Problem**: `sunshine_state.json` contains UUID and pairing data.
- Multiple instances sharing the same file would conflict.

**Solution**: MultiSeat-Extended generates per-seat `sunshine_state.json`:
- Each seat gets its own UUID
- Each seat appears as separate server in Moonlight

### 6. Credentials
**Problem**: Web UI credentials file would be shared.
- Multiple instances sharing the same file would conflict.

**Solution**: MultiSeat-Extended:
- Generates per-seat credentials OR
- Uses shared credentials file (all seats share same web UI login)

---

## Required Changes for Multi-Instance

### By MultiSeat-Extended (already implemented)

| Change | Status | Evidence |
|--------|--------|----------|
| Per-seat config directory | ✅ Implemented | VibepolloConfigBuilder.BuildConfig |
| Per-seat port allocation | ✅ Implemented | PortAllocator (bitmap) |
| Per-seat sunshine.conf generation | ✅ Implemented | VibepolloConfigBuilder |
| Per-seat state file | ✅ Implemented | VibepolloConfigBuilder.EnsureSeatStateFile |
| Per-seat UUID | ✅ Implemented | Each config gets unique sunshine_state.json |
| Per-seat display tracking | ✅ Implemented | VibepolloManager.ParseSudoVdaDisplayId |
| Per-seat process launch | ✅ Implemented | ProcessInjector.LaunchVibepolloInSessionAsync |
| Firewall per-seat | ✅ Implemented | FirewallManager.OpenPortsAsync |

### By Vibepollo (NOT needed)

| Change | Why Not Needed |
|--------|---------------|
| Native multi-instance support | MultiSeat handles orchestration |
| Port conflict detection | MultiSeat allocates non-overlapping ports |
| Display sharing | Each seat has its own virtual display |
| Session management | MultiSeat creates Windows sessions |

---

## Multi-Instance Architecture Diagram

```
MultiSeat-Extended (SYSTEM service)
    │
    ├── Seat 1 (Windows Session 1)
    │   ├── Account: MultiSeatSeat01
    │   ├── Session: RDP loopback → 127.0.0.2
    │   ├── Display: SudoVDA UUID {aaa...}
    │   ├── Audio: Remote Audio endpoint (session 1)
    │   ├── Ports: 48100-48129
    │   └── Vibepollo Instance 1
    │       ├── Config: vibepollo/MultiSeatSeat01/sunshine.conf
    │       ├── State: vibepollo/MultiSeatSeat01/config/sunshine_state.json
    │       ├── Display output: {aaa...} (SudoVDA)
    │       └── Process: sunshine.exe (PID 1234)
    │
    ├── Seat 2 (Windows Session 2)
    │   ├── Account: MultiSeatSeat02
    │   ├── Session: RDP loopback → 127.0.0.2
    │   ├── Display: SudoVDA UUID {bbb...}
    │   ├── Audio: Remote Audio endpoint (session 2)
    │   ├── Ports: 48130-48159
    │   └── Vibepollo Instance 2
    │       ├── Config: vibepollo/MultiSeatSeat02/sunshine.conf
    │       ├── State: vibepollo/MultiSeatSeat02/config/sunshine_state.json
    │       ├── Display output: {bbb...} (SudoVDA)
    │       └── Process: sunshine.exe (PID 5678)
    │
    └── Seat 3 (Windows Session 3)
        ├── Account: MultiSeatSeat03
        ├── Session: RDP loopback → 127.0.0.2
        ├── Display: SudoVDA UUID {ccc...}
        ├── Audio: Remote Audio endpoint (session 3)
        ├── Ports: 48160-48189
        └── Vibepollo Instance 3
            ├── Config: vibepollo/MultiSeatSeat03/sunshine.conf
            ├── State: vibepollo/MultiSeatSeat03/config/sunshine_state.json
            ├── Display output: {ccc...} (SudoVDA)
            └── Process: sunshine.exe (PID 9012)
```

---

## Config Isolation Summary

| Resource | Isolation Method | Owner |
|----------|-----------------|-------|
| Process | Separate `sunshine.exe` per seat | MultiSeat launches |
| Config file | Per-seat `sunshine.conf` | MultiSeat generates |
| State file | Per-seat `sunshine_state.json` | MultiSeat generates |
| UUID | Per-seat (in state file) | Vibepollo generates |
| Ports | Per-seat 30-port blocks | MultiSeat allocates |
| Display | Per-seat SudoVDA virtual display | Vibepollo creates |
| Audio | Per-session Remote Audio endpoint | Windows provides |
| Credentials | Shared OR per-seat | MultiSeat manages |
| Certificates | Per-seat (in state file) | Vibepollo generates |

---

## Conclusion

Multi-instance Vibepollo is fully supported by MultiSeat-Extended's current architecture. The key insight is that **Vibepollo does not need to know about other instances** — each instance runs in its own Windows session with its own config, ports, display, and audio. MultiSeat-Extended handles all orchestration.
