# Audio Architecture

**Date**: 2026-08-30
**Status**: FROZEN

---

## Purpose

Define audio architecture, backend options, and isolation model.

---

## Current Architecture: PerSession

### How It Works

```
Seat Session
    │
    ├── Windows RDP creates "Remote Audio" endpoint
    │
    ├── Vibepollo captures from session's Remote Audio
    │
    └── Stream to Moonlight client
```

### Key Properties

| Property | Value | Evidence |
|----------|-------|----------|
| Isolation | True (per-session endpoint) | Windows RDP |
| VAC needed | No | PerSession mode |
| Host audio disruption | None | No host-side routing |
| Microphone | No | RDP limitation |

**FACT**: PerSession audio uses Windows RDP Remote Audio endpoints.

---

## Audio Backend Abstraction

### IAudioBackend (Conceptual)

| Operation | Description |
|-----------|-------------|
| GetCaptureDevice | Get audio capture device for session |
| GetPlaybackDevice | Get audio playback device for session |
| MuteHostAudio | Mute host audio (optional) |

**DECISION**: Audio backend is abstracted via IAudioBackend.

---

## Current Backend: PerSession (RDP Remote Audio)

### Architecture

```
MultiSeat.Service
    │
    ├── AudioLoopbackCaptureHelper
    │       └── WASAPI loopback capture
    │
    └── Windows RDP
            └── Per-session Remote Audio endpoint
```

### Capabilities

| Capability | Status | Evidence |
|------------|--------|----------|
| Playback isolation | ✅ | Per-session endpoint |
| Microphone | ❌ | RDP limitation |
| Device assignment | N/A | Windows manages |
| Routing | ✅ | Vibepollo captures session endpoint |

**FACT**: PerSession audio is the only supported mode.

---

## Audio Lifecycle

### Creation

```
1. Session created (RDP loopback)
   └── Windows creates Remote Audio endpoint
2. Vibepollo starts
   └── Captures from session's Remote Audio
3. Stream to client
   └── Audio flows through Vibepollo
```

### Teardown

```
1. Vibepollo stops
   └── Releases audio capture
2. Session logged off
   └── Remote Audio endpoint destroyed
```

---

## RustDesk Interference

### Problem

RustDesk.exe runs in every session and opens the default render endpoint in exclusive WASAPI mode, causing AUDCLNT_E_DEVICE_IN_USE for Vibepollo's loopback.

### Solution

```
1. Write RustDesk2.toml with enable-audio=N
2. Kill RustDesk processes in seat session
3. RustDesk re-reads config on next launch
```

**FACT**: SeatManager suppresses RustDesk audio capture.

---

## Future: Microphone Path

### Gap

No microphone path exists. RDP does not redirect mic from session to host.

### Options

| Option | Feasibility | Status |
|--------|-------------|--------|
| Vibepollo WebRTC mic | High | Beta (v1.19.0+) |
| VAC (SharedHost) | Low | Removed (host disruption) |
| Custom audio driver | Low | Too complex |

### Recommendation

**WAIT** for Vibepollo WebRTC mic stabilization.

**DECISION**: Microphone is deferred pending Vibepollo WebRTC mic.

---

## Audio Isolation Model

### Per-Session Isolation

```
Seat 1
├── Remote Audio endpoint 1
└── Vibepollo captures endpoint 1

Seat 2
├── Remote Audio endpoint 2
└── Vibepollo captures endpoint 2

Seat 3
├── Remote Audio endpoint 3
└── Vibepollo captures endpoint 3
```

### Properties

- True isolation (each session owns its endpoint)
- No VAC needed
- No host audio disruption
- One-way only (no microphone)

---

## Evidence

| Claim | Source | Status |
|-------|--------|--------|
| PerSession uses RDP Remote Audio | MultiSeatOptions.cs | FACT |
| No microphone path | MultiSeatOptions.cs comments | FACT |
| RustDesk interference handled | SeatManager.cs | FACT |
| Vibepollo WebRTC mic is beta | Vibepollo v1.19.0 | FACT |
| SharedHost removed | MultiSeatOptions.cs comments | FACT |
