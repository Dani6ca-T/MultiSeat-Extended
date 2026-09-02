# Apollo Architecture — Source-Level Analysis

**Date**: 2026-08-30
**Purpose**: Detailed architecture analysis of Apollo streaming server

---

## Repository

- **URL**: https://github.com/ClassicOldSong/Apollo
- **License**: GPLv3
- **Language**: C++ (inherits from Sunshine)
- **Status**: Active (but slower than Vibepollo)
- **Fork of**: LizardByte/Sunshine

---

## What Apollo Is

Apollo is a self-hosted desktop stream host for Artemis (Moonlight Noir). It is a fork of Sunshine with additional features:

- Built-in Virtual Display with HDR support
- Per-client fixed identity
- Permission management (role-based)
- Clipboard sync
- Client connection/disconnection hooks
- Input-only mode
- Dual GPU support
- Headless mode

---

## Key Additions Over Sunshine

### 1. Built-in Virtual Display (SudoVDA)

**Source**: README + Wiki

- Uses SudoVDA (SudoMaker Virtual Display Adapter)
- Auto resolution/framerate matching for Artemis/Moonlight clients
- Virtual display created on stream start, removed on app quit
- Each client gets a **fixed identity** (not random or shared)
- Display configuration remembered by Windows natively

**Evidence**:
- README: "Apollo uses SudoVDA for virtual display"
- README: "Apollo assigns a fixed identity for each Artemis/Moonlight client"

### 2. Per-Client Fixed Identity

**Source**: README

- Each Artemis/Moonlight client gets a unique, persistent identity
- Display configuration automatically remembered by Windows
- Unlike Sunshine (reuses one identity) or random generation

**Evidence**:
- README: "Unlike all other solutions that reuses one identity or generate a random one each time for any virtual display sessions, Apollo assigns a fixed identity for each Artemis/Moonlight client"

### 3. Permission Management

**Source**: README + Wiki

- Role-based access control for clients
- First paired client gets FULL permissions
- New clients get View Streams + List Apps only
- Permissions: View Streams, List Apps, Launch Apps, Mouse Input, Keyboard Input

**Evidence**:
- README: "The FIRST client paired with Apollo will be granted with FULL permissions"

### 4. Clipboard Sync

**Source**: README

- Clipboard synchronization between host and client

### 5. Client Connection/Disconnection Commands

**Source**: README

- Execute commands on client connect/disconnect
- Useful for auto pause/resume games

### 6. Input-Only Mode

**Source**: README

- Stream input without video

### 7. Dual GPU Support

**Source**: README

- Support for dual GPU laptops
- Set Adapter Name to dGPU
- Enable Headless mode
- No dummy plug needed
- Image rendered and encoded directly from dGPU

### 8. HDR Support

**Source**: README

- HDR support from Windows 11 23H2
- Generally supported on 24H2
- Apollo and SudoVDA handle HDR
- Client device capability matters (Apple products recommended)

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────┐
│            Apollo Streaming Server               │
│         (Sunshine fork, GPLv3)                   │
│  ┌───────────────────────────────────────────┐  │
│  │  Capture Engine (DDA/WGC/DXGI)           │  │
│  │  ├── Desktop Duplication                  │  │
│  │  ├── Windows Graphics Capture             │  │
│  │  └── HDR support                          │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│                      ▼                           │
│  ┌───────────────────────────────────────────┐  │
│  │  Encoder (NVENC/AMF/QSV/FFmpeg)          │  │
│  │  ├── H.264/H.265/AV1                      │  │
│  │  └── Hardware acceleration                │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│                      ▼                           │
│  ┌───────────────────────────────────────────┐  │
│  │  Network (RTSP + WebRTC)                  │  │
│  │  ├── Moonlight protocol                   │  │
│  │  └── Client pairing                       │  │
│  └───────────────────────────────────────────┘  │
│                      │                           │
│  ┌───────────────────────────────────────────┐  │
│  │  Virtual Display (SudoVDA)                │  │
│  │  ├── Per-client fixed identity            │  │
│  │  ├── Auto resolution/framerate matching   │  │
│  │  ├── HDR support                          │  │
│  │  └── Created on stream start              │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  Permission System                        │  │
│  │  ├── Role-based access control            │  │
│  │  ├── Per-client permissions               │  │
│  │  └── First client = FULL permissions      │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  Web UI (Configuration)                   │  │
│  │  ├── Client management                    │  │
│  │  ├── Permission management                │  │
│  │  └── Display/audio configuration          │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

---

## Multi-Instance Support

**Source**: README + Wiki + Discussions

### How It Works

- Apollo supports running multiple instances
- Each instance creates its own virtual display
- Each instance has its own port, config, credentials

### Limitations

**Source**: Issue #874

- Virtual display created by Apollo only works for one client
- Gets "confused" with multiple virtual displays from multiple instances
- Not designed for true multiseat gaming

### Comparison to MultiSeat-Extended

| Aspect | Apollo | MultiSeat-Extended |
|--------|--------|-------------------|
| Multi-instance | Yes (manual setup) | Yes (automated) |
| Virtual display per client | Yes (SudoVDA) | Yes (SudoVDA) |
| Session creation | No | Yes (RDP loopback) |
| User management | No | Yes (Windows accounts) |
| Input isolation | No | Yes (HidHide session jail) |
| Game launching | No | Yes (SeatManager) |

---

## Apollo vs Vibepollo

| Feature | Apollo | Vibepollo |
|---------|--------|-----------|
| Base | Sunshine fork | Apollo fork |
| Virtual display | SudoVDA (built-in) | Own bundled driver |
| Per-client identity | Yes (fixed) | Yes (inherited) |
| Permission system | Yes | Yes |
| RTSS integration | No | Yes |
| Lossless Scaling | No | Yes |
| NVIDIA Smooth Motion | No | Yes |
| License | GPLv3 | GPLv3 |
| Development pace | Slower | Very active |
| AI-generated code | No | Yes (99%) |

---

## License

**GPLv3**

**Implications**:
- Cannot embed in MIT project (MultiSeat-Extended)
- Must keep as separate process
- Code modifications must be released under GPLv3
- Can coexist as external streaming server

---

## Key Technical Details

### Virtual Display Lifecycle

1. Client connects to Apollo
2. Apollo creates SudoVDA virtual display
3. Display gets fixed identity for that client
4. Windows remembers display configuration
5. Stream ends → Display removed

### Permission System

- First paired client: FULL permissions
- New clients: View Streams + List Apps only
- Permissions: View Streams, List Apps, Launch Apps, Mouse Input, Keyboard Input
- Configured via Web UI

### HDR Support

- Requires Windows 11 23H2+
- Generally supported on 24H2
- Client device capability matters
- Apple products recommended for HDR

### Dual GPU Support

- Set Adapter Name to dGPU
- Enable Headless mode
- No dummy plug needed
- Image rendered and encoded from dGPU

---

## Evidence

| Claim | Source | Evidence | Status |
|-------|--------|----------|--------|
| Built-in virtual display (SudoVDA) | README | "Apollo uses SudoVDA for virtual display" | VERIFIED |
| Per-client fixed identity | README | "Apollo assigns a fixed identity for each client" | VERIFIED |
| Permission management | README | "FIRST client gets FULL permissions" | VERIFIED |
| Clipboard sync | README | Listed in features | VERIFIED |
| Client connection/disconnection commands | README | Listed in features | VERIFIED |
| Input-only mode | README | Listed in features | VERIFIED |
| Dual GPU support | README | Detailed section | VERIFIED |
| HDR support | README | Detailed section | VERIFIED |
| GPLv3 license | LICENSE file | GPLv3 text | VERIFIED |
| Multi-instance support | README | Wiki link | PARTIALLY VERIFIED |
| Multiple virtual displays issue | Issue #874 | "virtual display gets confused" | VERIFIED |
| Fork of Sunshine | README | "Sunshine fork" | VERIFIED |
| SudoVDA integration | README | "Apollo uses SudoVDA" | VERIFIED |
| Web UI for configuration | README | "A web UI is provided" | VERIFIED |
| Moonlight protocol support | README | "self-hosted desktop stream host for Artemis(Moonlight Noir)" | VERIFIED |
