# MultiSeat-Extended: Каталог технических решений (Technique Catalog)

## T-001: RDP Concurrent Session Patching

- **Problem solved**: Windows Home/Pro limits concurrent RDP sessions to 1
- **Projects using it**: TermWrap, rdpWrapper, Duo, neo_multiseat
- **Best implementation found**: TermWrap (Rust fork) — symbol-free dynamic patching
- **How it works**: DLL proxy replaces termsrv.dll, patches session limit checks at runtime using disassembly
- **Advantages**: No static .ini files, survives Windows updates better
- **Disadvantages**: Complex disassembly logic, may break with major Windows changes
- **Windows compatibility**: Windows 10/11 x64 (Rust fork also supports ARM64)
- **Security implications**: User-mode only, no kernel patches, but modifies termsrv behavior
- **License**: MIT
- **Potential MultiSeat integration**: ADAPT — MultiSeat currently uses RDPWrap; TermWrap's dynamic approach could replace static .ini dependency

---

## T-002: Automatic Termsrv Offset Discovery

- **Problem solved**: Static offset .ini files break after every Windows cumulative update
- **Projects using it**: TermWrap (Rust fork)
- **Best implementation found**: kernalix7/rdprrap — pelite + iced-x86 disassembler
- **How it works**: Scans .rdata strings and exception tables, traces xrefs, performs runtime struct-layout analysis
- **Advantages**: No PDB server needed, no .ini file maintenance
- **Disadvantages**: Complex implementation, may miss edge cases
- **Windows compatibility**: Windows 10/11 x64/ARM64
- **Security implications**: Same as T-001
- **License**: MIT
- **Potential MultiSeat integration**: REFERENCE ONLY — MultiSeat delegates to RDPWrap/TermWrap

---

## T-003: RDP Loopback Session Creation

- **Problem solved**: CreateProcessAsUser cannot create new interactive sessions from Session 0
- **Projects using it**: MultiSeat-Extended, Duo
- **Best implementation found**: MultiSeat-Extended SessionLauncher
- **How it works**: Launch mstsc.exe targeting 127.0.0.2 in console session → RDP triggers termsrv to create new session → poll WTS until account's session appears
- **Advantages**: Reliable, uses official Windows RDP stack
- **Disadvantages**: Requires RDP Wrapper for concurrent sessions
- **Windows compatibility**: Windows 10/11 with RDP Wrapper
- **Security implications**: Credentials stored temporarily in console user's credential store
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-004: Per-Seat Virtual Display (SudoVDA)

- **Problem solved**: Headless/multi-seat needs independent displays per seat
- **Projects using it**: MultiSeat-Extended, Vibepollo, Apollo, Duo
- **Best implementation found**: MultiSeat-Extended + Vibepollo (SudoVDA UUID tracking)
- **How it works**: SudoVDA IddCx driver creates virtual monitors; Vibepollo creates/destroys per client connect; MultiSeat tracks UUID via log parsing
- **Advantages**: No physical display needed, resolution matches client
- **Disadvantages**: Vibepollo creates display lazily (on client connect, not startup)
- **Windows compatibility**: Windows 10/11 x64
- **Security implications**: Virtual display driver runs in kernel mode
- **License**: SudoVDA license (separate)
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-005: Display Isolation (SudoVDA Primary + RDP Shrunk)

- **Problem solved**: TermService CPU high when encoding full RDP desktop
- **Projects using it**: MultiSeat-Extended (unique approach)
- **Best implementation found**: MultiSeat-Extended SeatManager.ApplyDisplayIsolationAsync
- **How it works**: Makes SudoVDA session primary, shrinks RDP display to 640×480 → TermService only encodes tiny secondary display
- **Advantages**: Reduces TermService CPU from ~70% to <5%
- **Disadvantages**: State doesn't survive session disconnect; must re-apply after sleep/reconnect
- **Windows compatibility**: Windows 10/11
- **Security implications**: None
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-006: Per-Session Audio (RDP Remote Audio)

- **Problem solved**: Seat audio isolation without VAC/VoiceMeeter
- **Projects using it**: MultiSeat-Extended (unique approach)
- **Best implementation found**: MultiSeat-Extended PerSession mode
- **How it works**: Each RDP session has its own "Remote Audio" endpoint; Vibepollo loopback-captures from inside session; mstsc muted on console side
- **Advantages**: No driver dependencies, true isolation, no host audio wedging
- **Disadvantages**: No microphone path
- **Windows compatibility**: Windows 10/11
- **Security implications**: None
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-007: HidHide Session Jail

- **Problem solved**: Gamepad isolation between seats
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended HidHideSessionJail
- **How it works**: Undocumented HidHide feature — append !<sessionId> to device instance path in blacklist → device visible only in that session
- **Advantages**: Kernel-level isolation, transparent to applications
- **Disadvantages**: Undocumented feature, HidHide CLI traps, console-side Vibepollo pad ambiguous
- **Windows compatibility**: HidHide >= 1.4.181.0
- **Security implications**: Kernel driver dependency
- **License**: HidHide is MIT, MultiSeat integration is MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-008: Multi-Instance Streaming (Provider per Seat)

- **Problem solved**: Multiple independent streaming servers on one host
- **Projects using it**: MultiSeat-Extended, Helios, Duo
- **Best implementation found**: Helios (cleanest architecture)
- **How it works**: Each seat gets independent sunshine.conf, port block, config directory, state file, credentials
- **Advantages**: Full isolation, independent pairing, independent settings
- **Disadvantages**: Port consumption (30 ports per seat), process overhead
- **Windows compatibility**: Windows 10/11
- **Security implications**: Each instance has own credentials
- **License**: GPLv3 (Helios), MIT (MultiSeat)
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-009: Provider Lifecycle Management

- **Problem solved**: Start/stop/restart streaming servers reliably
- **Projects using it**: MultiSeat-Extended, Helios, Duo
- **Best implementation found**: Helios (Named Pipe IPC to SYSTEM service)
- **How it works**: Helios uses separate SYSTEM service (Spawner) + Named Pipes; MultiSeat uses embedded service + ProcessInjector
- **Advantages**: Helios approach separates UI from privileged operations
- **Disadvantages**: Additional service complexity
- **Windows compatibility**: Windows 10/11
- **Security implications**: SYSTEM execution required for session token access
- **License**: GPLv3 (Helios), MIT (MultiSeat)
- **Potential MultiSeat integration**: ADAPT — consider Named Pipe pattern for future provider abstraction

---

## T-010: Crash Recovery with Restart Limit

- **Problem solved**: Prevent infinite restart loops when provider crashes
- **Projects using it**: MultiSeat-Extended, Helios
- **Best implementation found**: MultiSeat-Extended VibepolloManager
- **How it works**: RestartCount tracks attempts; max 3 before giving up; sleep reconnect resets count
- **Advantages**: Prevents resource exhaustion, distinguishes crash from sleep
- **Disadvantages**: Fixed limit may not suit all scenarios
- **Windows compatibility**: Windows 10/11
- **Security implications**: None
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-011: Launch-on-Connect App Launcher

- **Problem solved**: Game launchers scanning controllers before virtual pad exists
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended OnConnectAppLauncher
- **How it works**: Tails Vibepollo log for CLIENT CONNECTED; launches configured apps after delay; optional kill on disconnect
- **Advantages**: Guarantees virtual controller exists before launcher scans
- **Disadvantages**: Depends on log format; delay is configurable but fixed
- **Windows compatibility**: Windows 10/11
- **Security implications**: Apps launched with seat account privileges
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-012: Client Resolution Following

- **Problem solved**: Seat resolution should match Moonlight client
- **Projects using it**: MultiSeat-Extended, Vibepollo, Duo
- **Best implementation found**: Duo (automatic, seamless); MultiSeat (reconnect-based)
- **How it works**: Vibepollo logs client request → MultiSeat disconnects + reconnects session at new size → restarts Vibepollo
- **Advantages**: Accurate resolution matching
- **Disadvantages**: Brief stream interruption during reconnect
- **Windows compatibility**: Windows 10/11
- **Security implications**: None
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-013: Orphaned Process Cleanup

- **Problem solved**: Previous service run leaves Vibepollo processes holding ports
- **Projects using it**: MultiSeat-Extended, Vibepollo
- **Best implementation found**: MultiSeat-Extended MultiSeatWorker
- **How it works**: WMI query identifies MultiSeat-managed Vibepollo PIDs by exe path or config path; kills only managed instances
- **Advantages**: Safe for standalone Vibepollo coexistence
- **Disadvantages**: WMI failure → skip cleanup (fail-safe)
- **Windows compatibility**: Windows 10/11
- **Security implications**: Process killing requires SYSTEM
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-014: Application Compatibility Layer (Game Isolation)

- **Problem solved**: Games detecting remote sessions, conflicting instances, mutex issues
- **Projects using it**: Duo
- **Best implementation found**: Duo (proprietary)
- **How it works**: Native Windows Compatibility Database + custom library patches bypass session checks, Steam instance conflicts, file locks
- **Advantages**: Broad game compatibility
- **Disadvantages**: Closed-source, anti-cheat may flag it
- **Windows compatibility**: Windows 11 24H2+
- **Security implications**: Modifies application behavior at system level
- **License**: Proprietary
- **Potential MultiSeat integration**: REFERENCE ONLY — cannot reuse closed-source code

---

## T-015: Steam Multi-Instance

- **Problem solved**: Steam terminates when second instance starts
- **Projects using it**: Duo
- **Best implementation found**: Duo (proprietary)
- **How it works**: Process patching to bypass single-instance check
- **Advantages**: Allows multiple Steam instances per seat
- **Disadvantages**: Closed-source, may break with Steam updates
- **Windows compatibility**: Windows 11
- **Security implications**: Modifies Steam behavior
- **License**: Proprietary
- **Potential MultiSeat integration**: REFERENCE ONLY — consider shared library approach instead (already implemented)

---

## T-016: DPAPI System-Scope Credential Encryption

- **Problem solved**: Protect seat passwords from non-admin access
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended AccountManager
- **How it works**: ProtectedData.Protect with CurrentUser scope under SYSTEM → only SYSTEM can decrypt; ACL hardening on store file
- **Advantages**: Strong encryption, survives restarts
- **Disadvantages**: Service reconfiguration to different account breaks decryption
- **Windows compatibility**: Windows 10/11
- **Security implications**: Administrator can still become SYSTEM
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-017: WebSocket Real-Time Seat State

- **Problem solved**: Dashboard needs live seat status updates
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended WebSocketHub
- **How it works**: Broadcasts full SeatInfo on every state change via /ws/seats
- **Advantages**: Real-time updates, no polling
- **Disadvantages**: Broadcasts sensitive data (account names, PIDs, ports)
- **Windows compatibility**: Any
- **Security implications**: API key required for WebSocket connection
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-018: Named Pipe IPC (Service ↔ UI)

- **Problem solved**: Privileged operations from non-privileged UI
- **Projects using it**: Helios
- **Best implementation found**: Helios (Helios.Spawner ↔ Helios.App)
- **How it works**: SYSTEM service listens on Named Pipe; UI sends commands (start/stop); service executes with elevated privileges
- **Advantages**: Clean separation of privilege levels
- **Disadvantages**: Additional complexity, Named Pipe security
- **Windows compatibility**: Windows 10/11
- **Security implications**: Named Pipe ACL must be properly configured
- **License**: GPLv3
- **Potential MultiSeat integration**: REFERENCE ONLY — MultiSeat uses embedded API instead

---

## T-019: DWM Frame Interval for High FPS

- **Problem solved**: RDP sessions default to ~30fps DWM composition
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended MultiSeatWorker
- **How it works**: Sets DWMFRAMEINTERVAL=1 in Terminal Server WinStations registry key
- **Advantages**: Allows DWM to compose at display refresh rate
- **Disadvantages**: Requires service restart + new RDP sessions
- **Windows compatibility**: Windows 10/11
- **Security implications**: Registry modification requires SYSTEM
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE

---

## T-020: Late SudoVDA Display Detection

- **Problem solved**: Vibepollo creates display on client connect, not at startup
- **Projects using it**: MultiSeat-Extended
- **Best implementation found**: MultiSeat-Extended SeatManager.TryLateDisplayDetectionAsync
- **How it works**: Health check re-parses Vibepollo log for SudoVDA UUID; applies display isolation when found
- **Advantages**: Handles lazy display creation without restart
- **Disadvantages**: Delayed display isolation (until first client connects)
- **Windows compatibility**: Windows 10/11
- **Security implications**: None
- **License**: MIT
- **Potential MultiSeat integration**: ✅ Already implemented — REUSE
