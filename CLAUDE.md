# MultiSeat — Codebase Guide

MultiSeat runs multiple simultaneous Moonlight game-streaming sessions on one Windows host. Each seat gets an isolated Windows account, virtual display (SudoVDA), virtual audio device, and a dedicated Apollo streaming instance, managed from a web dashboard.

## Stack

- **Backend:** .NET 9 / ASP.NET Core Windows Service (`src/MultiSeat.Service`) — runs as SYSTEM
- **Frontend:** React + TypeScript dashboard (`src/MultiSeat.Dashboard`) — Vite build, served on port 9550
- **Shared:** `src/MultiSeat.Shared` — constants, port layout, shared types
- **Tests:** `src/MultiSeat.Tests`
- **InputHook DLL:** `src/MultiSeat.InputHook` — C++/CMake, keyboard/mouse session isolation
- **Solution:** `src/MultiSeat.slnx`

## Key Service Components

| Class | Responsibility |
|---|---|
| `SeatManager` | Seat lifecycle — provision / teardown |
| `SessionLauncher` | RDP loopback session creation, mstsc window management |
| `ApolloManager` | Per-seat Apollo process management |
| `VirtualDisplayManager` | SudoVDA virtual display attach/detach |
| `AudioRouter` | Virtual audio device assignment per seat |
| `InputRouter` | XInput/ViGEm controller routing |
| `HidHideConfigurator` | Controller cloaking via HidHide |
| `InputHookManager` | Keyboard/mouse session isolation (InputHook DLL) |
| `AccountManager` | Windows local account CRUD |
| `ApiServer` | ASP.NET Core HTTP API + WebSocket |

## Port Layout

Each seat reserves 10 ports: `PortBase + (seat_index × 10)`. Default `PortBase = 47984`.
- Seat 0 → 47984–47993, Seat 1 → 47994–48003, etc.

## Audio Device Layout

- Seat 0 → VB-CABLE basic "CABLE Input"
- Seat 1 → VoiceMeeter "VoiceMeeter Input"
- Seat 2 → VoiceMeeter "VoiceMeeter Aux Input"
- Seat 3 → VoiceMeeter "VoiceMeeter VAIO3 Input"

VoiceMeeter must be running — `AudioRouter` auto-starts it. Registered in `HKLM\Run` for auto-start at boot.

## Install / Deploy

Two separate scripts — prereqs and service deploy are intentionally split:

```powershell
# Step 1: Install all prerequisites (drivers, audio devices, RDPWrap, etc.)
.\prerequisites\install-prerequisites.ps1
# Reboot if prompted, then re-run to confirm clean.
# Log: prerequisites\prereq.log

# Step 2: Build and deploy the MultiSeat service (run from scripts\)
.\scripts\install-service.ps1

# Remove the service
.\scripts\install-service.ps1 -Uninstall
```

## Key Runtime Paths

| Path | Purpose |
|---|---|
| `C:\Program Files\MultiSeat\` | Service install dir |
| `C:\ProgramData\MultiSeat\` | Runtime data, Apollo configs, logs |
| `C:\ProgramData\MultiSeat\logs\` | Service logs |
| `C:\ProgramData\MultiSeat\apollo\` | Per-seat Apollo config dirs |

## Architecture Notes

- Service runs as **SYSTEM** — all process creation uses `CreateProcessAsUser` with appropriate session tokens.
- Sessions are created via **RDP loopback** (`127.0.0.2`) — the only reliable way to create new interactive sessions from Session 0.
- `mstsc.exe` is launched in the console session and kept alive (hidden on the virtual display) to hold the seat session in **Active** state so display APIs work. Call `DisconnectSession()` once Apollo has initialized.
- Temp RDP files go to `C:\ProgramData\MultiSeat\` (not `%TEMP%`) so the console user's token can read them.
- `WTSQueryUserToken` returns a filtered (medium-integrity) token for admin accounts — `SessionLauncher` fetches the linked elevated token via `GetTokenLinkedToken` for SudoVDA IPC access.

## Known Constraints

- NVIDIA consumer GPUs: 3–5 concurrent NVENC sessions max.
- RDPWrap breaks after Windows updates to `termsrv.dll` — re-run prereq script to refresh `rdpwrap.ini`.
- mstsc window for each seat must never be manually disconnected (session goes Disconnected, display APIs stop working).
- Single GPU only — multi-GPU not tested.
- Windows 11 build 26100+ / x64 only.
- VoiceMeeter audio drivers only register after a reboot post-install.

## Required Prerequisites

- Apollo (Sunshine fork with multi-instance support) — NOT upstream Sunshine
- SudoVDA virtual display driver
- VB-CABLE basic (seat 0 audio)
- VoiceMeeter Potato (seats 1–3 audio)
- HidHide v1.5.230 (controller isolation)
- ViGEmBus v1.22.0 EXE — not MSI (virtual controller bus)
- RDPWrap (multi-session RDP on Windows Home/Pro)
- .NET 9 SDK
- Node.js 20+
