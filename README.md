# MultiSeat

**Multi-seat headless game streaming for Windows using Moonlight/Apollo.**

MultiSeat lets you run multiple simultaneous Moonlight game-streaming sessions on a single Windows machine. Each "seat" gets its own isolated Windows user account, virtual display, virtual audio cable, and Apollo (Sunshine) streaming instance — all managed from a single web dashboard.

---

## How It Works

1. You create or link a Windows local account for each streaming seat.
2. MultiSeat provisions a seat: launches a dedicated Apollo process in the account's RDP session, attaches a virtual display (SudoVDA), and routes a virtual audio cable (VB-CABLE) to it.
3. The Moonlight client connects to the seat's Apollo instance using the host's IP and the seat's assigned port.
4. Each seat streams independently with isolated input, audio, and display.

```
Host Machine
├── Seat 0 (MultiSeat01)  →  Apollo :47984  →  Moonlight Client A
├── Seat 1 (MultiSeat02)  →  Apollo :47994  →  Moonlight Client B
└── Seat 2 (MultiSeat03)  →  Apollo :48004  →  Moonlight Client C
```

---

## Requirements

See [REQUIREMENTS.md](REQUIREMENTS.md) for the full hardware and software requirements.

**Quick summary:**
- Windows 11 (build 26100+ recommended) or Windows 10 (build 19041+)
- x64 CPU with 2+ cores per seat; 4+ GB RAM per seat
- NVIDIA GTX 1060+ or AMD RX 580+ GPU with hardware encoding (NVENC/AMF)
- .NET 9 Runtime
- Apollo (Sunshine fork with multi-instance support)
- SudoVDA virtual display driver (one virtual display per seat)
- VB-CABLE virtual audio (one per seat)
- HidHide (controller isolation)
- ViGEmBus (virtual controller driver)
- RDPWrap (multi-session RDP on Windows Home/Pro)

---

## Installation

> **All commands must be run as Administrator in PowerShell.**

### Step 1 — Clone the repository

```powershell
git clone https://github.com/vibesoftwarecoder/MultiSeat.git
cd MultiSeat
```

### Step 2 — Install prerequisites

```powershell
.\prerequisites\install-prerequisites.ps1
```

This script automatically downloads and installs everything:

| Software | Purpose |
|----------|---------|
| ViGEmBus | Virtual Xbox controller driver |
| HidHide | Hides physical controllers from the host |
| VB-CABLE (basic) | Virtual audio device for seat 0 (free, auto-downloaded) |
| VoiceMeeter Potato | 3 additional virtual audio devices for seats 1–3 (free, auto-downloaded) |
| RDPWrap + rdpwrap.ini | Enables concurrent RDP sessions on Windows Home/Pro |
| Apollo | Sunshine fork with multi-instance streaming support |
| SudoVDA | Virtual display driver (one display per seat) |
| .NET 9 SDK | Required to build and run MultiSeat.Service |
| Node.js LTS | Required to build the dashboard |

It also enables Remote Desktop and opens the required firewall ports automatically.

> **Reboot** when prompted — HidHide and RDPWrap require it before the service will work.

### Step 3 — Install the MultiSeat service

```powershell
.\scripts\install-service.ps1
```

This script:
- Installs dashboard npm packages if needed
- Builds and publishes `MultiSeat.Service`
- Builds the web dashboard
- Registers `MultiSeatService` as a Windows auto-start service running as SYSTEM
- Starts the service immediately

### Step 4 — Open the dashboard

Open a browser and navigate to:

```
http://localhost:9550
```

From any other device on the same LAN:

```
http://<host-ip>:9550
```

### Step 5 — Create accounts and provision seats

1. Go to the **Accounts** tab — create a Windows local account for each seat (e.g., `MultiSeat01`, `MultiSeat02`).
2. Go to the **Seats** tab — click **+ New Seat**, select an account, and choose resolution and FPS.
3. Wait ~15 seconds for the seat to reach **Ready** status.

### Step 6 — Connect with Moonlight

Add the host to Moonlight using its IP address and the seat's assigned port:

```
<host-ip>:<seat-port>
```

The port for each seat is shown in the dashboard. Default ports:

| Seat | Port |
|------|------|
| Seat 0 | 47984 |
| Seat 1 | 47994 |
| Seat 2 | 48004 |

---

## Configuration

Edit `appsettings.json` in `C:\Program Files\MultiSeat\` (restart the service after changes):

| Key | Default | Description |
|-----|---------|-------------|
| `MaxSeats` | `4` | Maximum concurrent seats |
| `PortBase` | `47984` | First Apollo HTTPS port |
| `ApolloExePath` | `C:\Program Files\Apollo\sunshine.exe` | Path to Apollo executable |
| `ApolloConfigDir` | `C:\ProgramData\MultiSeat\apollo` | Per-seat config directory |
| `ApiPort` | `9550` | Dashboard port |
| `ApiKey` | *(empty)* | Optional API key for dashboard access |
| `VacCableCount` | `4` | Number of installed VB-CABLE devices |
| `EnableKeyboardMouseIsolation` | `true` | Route keyboard/mouse to the active seat |

---

## Uninstall

```powershell
.\scripts\install-service.ps1 -Uninstall
```

Then delete the data directories if desired:

```powershell
Remove-Item "C:\Program Files\MultiSeat" -Recurse -Force
Remove-Item "C:\ProgramData\MultiSeat"   -Recurse -Force
```

---

## Building from Source

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/)
- [CMake 3.20+](https://cmake.org/) and MSVC (only needed for the InputHook DLL)

> If you ran `prerequisites\install-prerequisites.ps1`, .NET SDK and Node.js are already installed.

### Build and deploy

```powershell
# Builds the service, installs npm deps, builds the dashboard,
# registers the Windows service, and starts it.
.\scripts\install-service.ps1
```

### Individual build steps

```powershell
# Restore .NET packages
dotnet restore src\MultiSeat.slnx

# Build the service
dotnet build src\MultiSeat.slnx

# Install and build the dashboard
cd src\MultiSeat.Dashboard
node install.cjs   # installs npm packages
node build.cjs     # compiles TypeScript + bundles with Vite
cd ..\..

# (Optional) Build the InputHook DLL — required for keyboard/mouse isolation
cd src\MultiSeat.InputHook
cmake -B build -A x64
cmake --build build --config Release
cd ..\..
```

### Run tests

```powershell
dotnet test src\MultiSeat.Tests\MultiSeat.Tests.csproj
```

---

## Architecture

```
MultiSeatService (Windows Service, SYSTEM)
├── SeatManager           — seat lifecycle (provision/teardown)
├── SessionLauncher       — RDP session + mstsc window management
├── ApolloManager         — per-seat Apollo process management
├── VirtualDisplayManager — SudoVDA display attach/detach
├── AudioRouter           — VB-CABLE assignment per seat
├── InputRouter           — XInput/ViGEm controller routing
├── HidHideConfigurator   — controller cloaking
├── InputHookManager      — keyboard/mouse session isolation
├── AccountManager        — Windows local account CRUD
├── ApiServer             — ASP.NET Core HTTP API + WebSocket
└── MultiSeat.Dashboard   — React/TypeScript web dashboard
```

The service runs as SYSTEM. Each seat's Apollo process runs inside its own RDP session, which is kept permanently Active via a managed `mstsc` connection so that the virtual display pipeline stays available to the streaming encoder.

---

## Port Layout

Each seat reserves a block of 10 ports starting at `PortBase + (seat_index × 10)`:

| Offset | Protocol | Use |
|--------|----------|-----|
| +0 | TCP | Apollo HTTPS (Moonlight pairing) |
| +1 | TCP | Apollo HTTP |
| +2 | TCP/UDP | RTP video |
| +3 | TCP/UDP | RTP audio |
| +4 | TCP/UDP | Control channel |

Default `PortBase` = 47984. Seat 0 = 47984, Seat 1 = 47994, Seat 2 = 48004, etc.

---

## Troubleshooting

**Moonlight shows "Failed to initialize video capture"**
The seat's RDP session became Disconnected. The health check will recover it automatically within ~5 seconds. If it persists, check the Apollo log under `C:\ProgramData\MultiSeat\apollo\`.

**Seat stuck at Provisioning**
Check the service log in `C:\ProgramData\MultiSeat\logs\`. Common causes: SudoVDA not installed, Apollo path wrong in `appsettings.json`, or insufficient virtual displays.

**Controller input not isolated between seats**
Ensure `EnableKeyboardMouseIsolation: true` in `appsettings.json` and that `MultiSeatInputHook.dll` is present in the install directory. HidHide must be installed and the service restarted after install.

**RDPWrap shows "Not supported" after a Windows update**
Re-run `prerequisites\install-prerequisites.ps1` — it will fetch the latest `rdpwrap.ini` automatically.

**Multiple VB-CABLE devices needed**
Each seat requires one VB-CABLE. After installing the first one via the prerequisites script, run `VBCABLE_Setup_x64.exe` manually for each additional seat (found in the extracted `VBCABLE_Driver_Pack45.zip`).

---

## A Note from the Author

MultiSeat started as a personal project — I built it because I wanted to run multiple game streaming sessions on one machine for myself and couldn't find anything that did exactly what I needed. I never expected others to find it useful, so I'm genuinely glad if it's working for you too.

Since I use this daily, it gets real-world testing every day. When something breaks I feel it immediately, so bugs tend to get fixed fast. If you run into an issue, open a GitHub issue and I'll take a look — no promises on timelines, but if it's something I can reproduce it'll get fixed.

Thanks for trying it out.

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

## Support

If you find this project useful, consider sending a tip:

**Bitcoin:** `12uGJ1YBFZGprhw9JrVSEEjEWkAHLaaaMU`
