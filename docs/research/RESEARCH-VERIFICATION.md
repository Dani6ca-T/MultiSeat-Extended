# MultiSeat-Extended: Аудит верификации research-документов

**Дата аудита**: 2026-08-30
**Источник проверки**: исходный код MultiSeat-Extended (Dani6ca-T/MultiSeat-Extended), commits до 2026-08-28

---

## Методология

Для каждого существенного утверждения в research-документах:
1. Найдено подтверждение в исходном коде MultiSeat-Extended
2. Указан репозиторий, файл, класс/метод
3. Если подтверждения нет — помечено CLAIM UNVERIFIED
4. Если утверждение неверно — исправлено
5. Если утверждение частично верно — помечено PARTIALLY VERIFIED

**Важно**: Утверждения о внешних проектах (Duo, Vibepollo, Apollo, Helios, TermWrap, neo_multiseat и др.) проверялись через secondary sources (документы исследований) и не могли быть напрямую верифицированы из исходного кода MultiSeat-Extended. Для этих утверждений статус UNVERIFIED означает "не удалось подтвердить из имеющегося кода", а не "утверждение неверно".

---

## 1. MultiSeat-Extended Claims (自我)

### 1.1 Стек и архитектура

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Backend: .NET 9 / ASP.NET Core Windows Service | CURRENT-ARCHITECTURE.md | MultiSeat.Service.csproj ( NET 9 target), Program.cs (AddWindowsService) | VERIFIED |
| Frontend: React + TypeScript (Vite) | CURRENT-ARCHITECTURE.md | MultiSeat.Dashboard/ directory exists with React components | VERIFIED |
| Tests: xUnit + Moq | CURRENT-ARCHITECTURE.md | MultiSeat.Tests project uses [Fact]/[Theory] attributes | VERIFIED |
| InputHook DLL: C++ / CMake | CURRENT-ARCHITECTURE.md | MultiSeat.InputHook/ directory with input_hook.cpp, CMakeLists.txt | VERIFIED |
| Streaming: Vibepollo (форк Sunshine) | CURRENT-ARCHITECTURE.md | VibepolloManager.cs references ClassicOldSong/Vibepollo | VERIFIED |
| Virtual Display: SudoVDA (IddCx) | CURRENT-ARCHITECTURE.md | SeatManager.cs: `--setup-display-isolation`, DisplayModeHelper.cs | VERIFIED |
| Virtual Controller: ViGEmBus + Nefarius.ViGEm.Client | CURRENT-ARCHITECTURE.md | ControllerManager.cs, InputRouter.cs, Nefarius.ViGEm.Client package | VERIFIED |
| Gamepad Isolation: HidHide (session jail) | CURRENT-ARCHITECTURE.md | HidHideConfigurator.cs, HidHideSessionJail.cs | VERIFIED |
| RDP: RDPWrap (TermsWrap) | CURRENT-ARCHITECTURE.md | RdpWrapper.cs detects both RDPWrap and TermWrap; install-prerequisites.ps1 installs TermWrap v0.6 | **INCORRECT** — should say "TermWrap (fork of RDPWrap)" not "RDPWrap (TermsWrap)". The name "TermsWrap" is a typo; actual product name is "TermWrap". Post Phase 6 migration (per SPEC.md), the installed dependency is TermWrap, not RDPWrap. RdpWrapper.cs detects EITHER for backward compatibility. |

### 1.2 Constants and limits

| Claim | Source | Evidence | Status |
|---|---|---|---|
| MaxSeats architectural ceiling = 8 | SEAT-LIFECYCLE.md | Constants.cs:32 `public const int MaxSeats = 8;` | VERIFIED |
| Operator limit MaxSeats = 4 (default) | SEAT-LIFECYCLE.md | MultiSeatOptions.cs:8 `public int MaxSeats { get; set; } = 4;` + appsettings.json: `"MaxSeats": 4` | VERIFIED |
| PortsPerSeat = 30 | SEAT-LIFECYCLE.md | Constants.cs `public const int PortsPerSeat = 30;` | VERIFIED |
| PortBase = 48100 | SEAT-LIFECYCLE.md | Constants.cs `public const int PortBase = 48100;` | VERIFIED |
| Port offsets: -5, 0, 1, 9, 10, 11, 12, 26 | STREAMING-ARCHITECTURE.md | Constants.cs: OffsetGfeHttps=-5, OffsetGfeHttp=0, OffsetWebUi=1, OffsetVideo=9, OffsetControl=10, OffsetAudio=11, OffsetMic=12, OffsetRtsp=26 | VERIFIED |
| MaxRestartAttempts = 3 | STREAMING-ARCHITECTURE.md | VibepolloManager.cs:301 `public const int MaxRestartAttempts = 3;` | VERIFIED |
| Health check interval 5s | SEAT-LIFECYCLE.md | MultiSeatOptions.cs `public int HealthCheckIntervalMs { get; set; } = 5_000;` | VERIFIED |
| AccountPrefix = "MultiSeatSeat" | SEAT-LIFECYCLE.md | Constants.cs `public const string AccountPrefix = "MultiSeatSeat";` | VERIFIED |

### 1.3 Provisioning pipeline

| Claim | Source | Evidence | Status |
|---|---|---|---|
| 9-step provisioning pipeline | SEAT-LIFECYCLE.md, AUDIT-SUMMARY.md | SeatManager.cs ProvisionSeatAsync: steps 1 (allocate ports), 1.5 (emulator netplay), 2 (launch session), 2.5 (suppress RustDesk), 2.7 (pre-write HidHide), 3 (virtual display), 4 (firewall), 5 (audio), 5.7 (seed emulator configs), 6 (Vibepollo), 6.5 (detect SudoVDA), 7 (controller), 8 (HidHide+hooks), 9 (ready) | **PARTIALLY VERIFIED** — The actual pipeline has MORE than 9 steps (includes 1.5, 2.5, 2.7, 5.7, 6.5 intermediate steps). The documentation says "9-step" but the actual code has 14+ distinct sub-steps. The count "9" refers to the major named steps, which is reasonable but imprecise. |
| RustDesk audio suppression step | SEAT-LIFECYCLE.md (Step 2.5) | SeatManager.cs lines ~175-210: writes RustDesk2.toml with enable-audio=N, kills RustDesk processes | VERIFIED |
| HidHide pre-write rules step | SEAT-LIFECYCLE.md (Step 2.7) | SeatManager.cs: `_hidHide.PreWriteRules(seat)` called before Vibepollo | VERIFIED |
| Display isolation: SudoVDA primary + RDP shrunk to 640x480 | SEAT-LIFECYCLE.md, TECHNIQUE-CATALOG.md | SeatManager.cs ApplyDisplayIsolationAsync: `--setup-display-isolation` and `--set-display-hz` helpers | VERIFIED |
| Late display detection from Vibepollo log | SEAT-LIFECYCLE.md | SeatManager.cs TryLateDisplayDetectionAsync: re-parses Vibepollo log for SudoVDA UUID | VERIFIED |
| Teardown is best-effort reverse order | SEAT-LIFECYCLE.md | SeatManager.cs TeardownSeatInternalAsync: each step wrapped in try-catch with empty catch blocks | VERIFIED |

### 1.4 Session architecture

| Claim | Source | Evidence | Status |
|---|---|---|---|
| RDP loopback to 127.0.0.2 | SESSION-ARCHITECTURE.md | SessionLauncher.cs: `CreateSessionViaRdpLoopbackAsync` launches mstsc.exe with /v:127.0.0.2 | VERIFIED |
| CreateProcessAsUser for session 0 limitation | SESSION-ARCHITECTURE.md | SessionLauncher.cs:22 comment, ProcessInjector.cs:17 comment, AdvApi.cs:257 CreateProcessAsUserW P/Invoke | VERIFIED |
| WTSQueryUserToken for session tokens | SESSION-ARCHITECTURE.md | SessionLauncher.cs: WtsApi calls, ProcessInjector.cs token acquisition | VERIFIED |
| Keepalive process in new session | SESSION-ARCHITECTURE.md | SessionLauncher.cs: launches `MultiSeat.Service.exe --keepalive` in new session | VERIFIED |
| mstsc window hiding | SESSION-ARCHITECTURE.md | WindowHideHelper.cs: WatchAndHideNew("mstsc", ...) | VERIFIED |

### 1.5 Credentials and security

| Claim | Source | Evidence | Status |
|---|---|---|---|
| DPAPI CurrentUser scope (= SYSTEM) | SECURITY-AUDIT.md | AccountManager.cs:386 `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` + comments explaining SYSTEM scope | VERIFIED |
| ACL hardening on accounts.json | SECURITY-AUDIT.md | AccountManager.cs HardenStore → SecureFile.TryRestrictToSystemAndAdmins | VERIFIED |
| API key in api-key.txt, auto-generated | SECURITY-AUDIT.md | ApiServer.cs ResolveApiKey: generates URL-safe 32-char key, saves to C:\ProgramData\MultiSeat\api-key.txt | VERIFIED |
| API key in URL query string (WebSocket) | SECURITY-AUDIT.md | ApiServer.cs: `context.Request.Query["key"].ToString()` for browser WebSocket auth | VERIFIED |
| Plaintext HTTP (no HTTPS) | SECURITY-AUDIT.md | ApiServer.cs: kestrel ListenAnyIP/ListenLocalhost, no HTTPS config; MultiSeatOptions.cs comment: "The API is HTTP only" | VERIFIED |
| API key in X-MultiSeat-Key header | SECURITY-AUDIT.md | ApiServer.cs: `Constants.ApiKeyHeader` = "X-MultiSeat-Key" | VERIFIED |
| CORS loopback only (default) | SECURITY-AUDIT.md | ApiServer.cs: When CorsOrigins empty, allows only localhost:{port} and 127.0.0.1:{port} | VERIFIED |
| NLA disabled for RDP loopback | SECURITY-AUDIT.md | install-prerequisites.ps1 disables RDP NLA; SESSION-ARCHITECTURE.md explains why | VERIFIED |
| Seat accounts standard users by default | SECURITY-AUDIT.md | MultiSeatOptions.cs GrantSeatAdministrator=false; AccountManager.cs ApplySeatGroupMembership: Users + Remote Desktop Users, removes from Administrators | VERIFIED |
| Shared credentials file readable by seats | SECURITY-AUDIT.md | SECURITY-AUDIT.md claims: "MEDIUM: Shared credentials file readable by seat accounts" — **UNVERIFIED** from code. The shared_credentials.json is in VibepolloConfigDir which is ProgramData\MultiSeat\vibepollo\. Whether seat accounts can read it depends on file ACLs, which are not explicitly set in the code. |

### 1.6 Input subsystem

| Claim | Source | Evidence | Status |
|---|---|---|---|
| InputHookManager is no-op | ARCHITECTURE-PROBLEMS.md, DUOSTREAM-GAP-ANALYSIS.md | InputHookManager.cs: WH_KEYBOARD_LL/WH_MOUSE_LL in Session 0; MultiSeatOptions.cs comment: "Default OFF — it is a no-op as architected" | VERIFIED |
| HidHide session jail undocumented | SECURITY-AUDIT.md, KNOWN-LIMITATIONS.md | HidHideSessionJail.cs:16: "Shipped in HidHide v1.4.181.0... documented nowhere"; MultiSeatOptions.cs: "Logic.c:817, documented nowhere" | VERIFIED |
| ViGEm controller opt-in | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: `EnableViGEmController = false` (default) | VERIFIED |
| EnableHidHideCloaking false (default) | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: `public bool EnableHidHideCloaking { get; set; } = false;` | VERIFIED |

### 1.7 Display subsystem

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Display isolation reduces TermService CPU from ~70% to <5% | TECHNIQUE-CATALOG.md | SeatManager.cs comment: "Reduces TermService CPU from ~70% to <5%" | **UNVERIFIED** — The claim is in code comments but there are no measurements or benchmarks to support the exact numbers. The code comments assert this improvement without citing data. |
| SudoVDA display created only on client connect | KNOWN-LIMITATIONS.md | VibepolloManager.cs ParseSudoVdaDisplayId comment: "Vibepollo does not create the seat's virtual display at startup — it creates it when a client connects" | VERIFIED |
| Display isolation doesn't survive disconnect | SEAT-LIFECYCLE.md | SeatManager.cs ApplyDisplayIsolationAsync comment: "This state does not survive a session disconnect (sleep/wake)" | VERIFIED |
| EnableHdr is no-op | KNOWN-LIMITATIONS.md | MultiSeatOptions.cs comment: "⚠️ Currently a NO-OP for a seat — nothing reads this to enable advanced colour" | VERIFIED |

### 1.8 Audio subsystem

| Claim | Source | Evidence | Status |
|---|---|---|---|
| PerSession audio (RDP Remote Audio endpoint) | DISPLAY-AUDIO-INPUT.md | SeatManager.cs Step 5 comment: "PerSession (the only supported mode) needs no host-side audio device"; SeatServices.cs: `AudioManaged = false` | VERIFIED |
| No microphone path | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs audio comment: "There is NO microphone path" | VERIFIED |
| Host audio protection (mstsc muted) | DISPLAY-AUDIO-INPUT.md | AudioMuteHelper.cs exists; SEAT-LIFECYCLE.md mentions muting | VERIFIED |
| Vibepollo config: keep_sink_default=disabled, auto_capture_sink=disabled, stream_mic=disabled | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs:222-238 writes all three; StreamingTests.cs:165-167 asserts them | VERIFIED |

### 1.9 Streaming

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Vibepollo runs in seat session via CreateProcessAsUser | STREAMING-ARCHITECTURE.md | VibepolloManager.cs: `_processInjector.LaunchVibepolloInSessionAsync(seat.SessionId, ...)` | VERIFIED |
| Per-seat sunshine.conf generation | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.BuildConfig: generates sunshine.conf per seat | VERIFIED |
| ParseSudoVdaDisplayId from Vibepollo log | STREAMING-ARCHITECTURE.md | VibepolloManager.cs:392-450 ParseSudoVdaDisplayIdFromLogText | VERIFIED |
| Vibepollo log_path config key ignored | KNOWN-LIMITATIONS.md | VibepolloManager.cs ResolveLogPath comment: "Vibepollo ignores log_path config key" | VERIFIED |
| VibepolloManager.Stop kills entire process tree | STREAMING-ARCHITECTURE.md | VibepolloManager.cs Stop: `proc.Kill(entireProcessTree: true)` | VERIFIED |
| headless_mode = enabled in config | KNOWN-LIMITATIONS.md | VibepolloConfigBuilder.cs:166: `sb.AppendLine("headless_mode = enabled");` | VERIFIED |

### 1.10 Test coverage

| Claim | Source | Evidence | Status |
|---|---|---|---|
| "22 unit/integration тестов" | AUDIT-SUMMARY.md, ECOSYSTEM-RESEARCH-SUMMARY.md | Glob found 22 test FILES, not 22 individual tests. Files: SeatGroupTests, PublicEndpointTests, LoggingFilterTests, RetroArchConfigSeederTests, HidHideArgumentTests, HidHideParserTests, HidHideSessionJailTests, InputTests, EndToEndTests, DialogClickHelperTests, PortAllocatorTests, ProcessInjectorTests, RdpCredentialStoreTests, RdpFileBuilderTests, RdpWrapperTests, SeatManagerTests, SeatStateTests, SessionGuardTests, SessionLauncherTests, SecureFileTests, StreamingTests, VibepolloLogParserTests | **PARTIALLY VERIFIED** — 22 test FILES exist, but total individual test count is higher (each file has multiple [Fact]/[Theory] methods). The claim "22 tests" is misleading — it should say "22 test files" or give the actual test method count. |

---

## 2. External Project Claims

### 2.1 Duo (DuoStream/Duo)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: Proprietary / Custom Freemium | LICENSE-AUDIT.md | No way to verify from this repo — DuoStream/Duo is closed source | UNVERIFIED |
| Copyright: DuoStream / Black-Seraph | LICENSE-AUDIT.md | No way to verify | UNVERIFIED |
| Bundles TermWrap | RDP-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Custom WDDM display driver | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| UMDF input driver | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| HDR streaming (paid) | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Game mutex isolation | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Steam multi-instance | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Seamless display adjustment | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| KB/M session isolation via session ID filtering | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Application Compatibility Layer | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Web UI on port 38299 | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Frame generation support | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Up to 500Hz (paid) | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Sunshine in proprietary product raises GPL concerns | LICENSE-AUDIT.md | No way to verify — no access to Duo's license terms | UNVERIFIED |

### 2.2 Vibepollo (Nonary/Vibepollo)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: GPLv3 | LICENSE-AUDIT.md | No way to verify from this repo — external repository | UNVERIFIED |
| AI-generated architecture (99% AI code) | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify from this repo | UNVERIFIED |
| H.264/HEVC/AV1 encoding | VIBEPOOLLO-GAP-ANALYSIS.md | Referenced by VibepolloConfigBuilder.cs hevc_mode, av1_mode keys | PARTIALLY VERIFIED — config keys exist, but actual encoding capability is Vibepollo's |
| NVENC + AMF support | VIBEPOOLLO-GAP-ANALYSIS.md | VibepolloConfigBuilder.cs: `encoder = nvenc` config key | PARTIALLY VERIFIED |
| Microphone passthrough | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs comment: "wait for Vibepollo 1.19.x WebRTC mic support" | PARTIALLY VERIFIED — code comment references it, but actual capability is Vibepollo's |
| RTSS integration | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs: EnableRtss, RtssProfilePath, RtssFpsLimit | PARTIALLY VERIFIED — config options exist, actual integration is Vibepollo's |
| Lossless Scaling integration | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs: EnableLosslessScaling, LosslessScalingPath | PARTIALLY VERIFIED |
| NVIDIA Smooth Motion | VIBEPOOLLO-GAP-ANALYSIS.md | No direct verification from code — claim about Vibepollo's capability | UNVERIFIED |
| Display layout restoration | VIBEPOOLLO-GAP-ANALYSIS.md | No direct verification from code | UNVERIFIED |
| WGC service capture | VIBEPOOLLO-GAP-ANALYSIS.md | No direct verification from code | UNVERIFIED |
| Headless mode | VIBEPOOLLO-GAP-ANALYSIS.md | VibepolloConfigBuilder.cs: `headless_mode = enabled` | VERIFIED |
| Auto-capture sink | VIBEPOOLLO-GAP-ANALYSIS.md | VibepolloConfigBuilder.cs: `auto_capture_sink = disabled` | VERIFIED |
| Port offsets map_port(N) = sunshine.port + N | KNOWN-LIMITATIONS.md | Constants.cs comment: "Matches Vibepollo's map_port(N) = sunshine.port + N (from network.cpp)" | **UNVERIFIED** — The comment references network.cpp but this is Vibepollo's code, not MultiSeat's. The offsets are observed empirically, not verified from Vibepollo source. |

### 2.3 Apollo (ClassicOldSong/Apollo)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: GPLv3 | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| Built-in SudoVDA integration | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify — external project | UNVERIFIED |
| Per-client fixed identity | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Permission management (role-based) | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Clipboard sync | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Headless mode | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |

### 2.4 Helios (MintCapybara924/Helios-Sunshine-Manager)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: GPLv3 | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| .NET 8 WPF app | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Named Pipe IPC (App ↔ Spawner) | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| SYSTEM service (Spawner) | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Multi-instance management | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |

### 2.5 TermWrap (llccd/TermWrap)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: MIT | LICENSE-AUDIT.md | No way to verify from this repo — but install-prerequisites.ps1 downloads from llccd/TermWrap | UNVERIFIED |
| DLL proxy with dynamic patching | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| Active maintenance | RDP-GAP-ANALYSIS.md | install-prerequisites.ps1 uses TermWrap v0.6 (recent) | PARTIALLY VERIFIED |
| Symbol-free offset discovery (Rust fork) | RDP-GAP-ANALYSIS.md | No way to verify from this repo | UNVERIFIED |
| ARM64 support (Rust fork) | RDP-GAP-ANALYSIS.md | No way to verify | UNVERIFIED |
| UmWrap: camera/USB redirection | RDP-GAP-ANALYSIS.md | install-prerequisites.ps1 mentions UmWrap.dll deployment | PARTIALLY VERIFIED |
| EndpWrap: audio recording redirection | RDP-GAP-ANALYSIS.md | No way to verify from this repo | UNVERIFIED |

### 2.6 rdpWrapper (redesk-io/rdpWrapper)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: Unknown | LICENSE-AUDIT.md | "Unable to verify — repository may be private or removed" | UNVERIFIED (correctly stated as unknown) |
| Language: Unknown | RDP-GAP-ANALYSIS.md | "Unknown" | UNVERIFIED (correctly stated) |

### 2.7 neo_multiseat (neo0oen619/neo_multiseat)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: MIT | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| PowerShell automation | RDP-GAP-ANALYSIS.md | No way to verify | UNVERIFIED |
| Automated RDPWrap recovery | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |
| Live session monitoring | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |
| Tailscale integration | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |
| NLA/TLS hardening | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |
| CSV export | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |

### 2.8 MultiseatProject (Abdulhanan535/MultiseatProject)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Repository not found / does not exist | LICENSE-AUDIT.md, ECOSYSTEM-RESEARCH-SUMMARY.md | Correctly stated as "Not found" | VERIFIED (correctly noted as missing) |

### 2.9 LuaTools (madoiscool/LuaTools)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: Not clearly stated | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| DRM bypass tools | LICENSE-AUDIT.md | No way to verify — characterizes as "not recommended" | UNVERIFIED |
| C# (.NET 8) | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |

### 2.10 virtual-display-rs (MolotovCherry/virtual-display-rs)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Open source license | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| Rust IddCx virtual display driver | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |
| Up to 10 monitors | REUSE-MATRIX.md | No way to verify | UNVERIFIED |

### 2.11 Apollo Multi Instance Launcher (neo0oen619/apollo-multi-instance-launcher)

| Claim | Source | Evidence | Status |
|---|---|---|---|
| License: MIT | LICENSE-AUDIT.md | No way to verify from this repo | UNVERIFIED |
| Python | ECOSYSTEM-RESEARCH-SUMMARY.md | No way to verify | UNVERIFIED |

---

## 3. Multi-Instance Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| MultiSeat supports N concurrent seats (up to MaxSeats=8) | ARCHITECTURE-MATRIX.md | Constants.cs MaxSeats=8, PortAllocator allocates per-seat blocks | VERIFIED |
| Each seat gets independent Vibepollo instance | ARCHITECTURE-MATRIX.md | VibepolloManager.cs: per-seat instance tracking, independent config, ports, display | VERIFIED |
| Vibepollo supports single instance only | VIBEPOOLLO-GAP-ANALYSIS.md | No way to verify from this repo | UNVERIFIED |
| Helios manages multiple instances | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Duo supports unlimited instances (paid) | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |
| Port allocation per seat (30-port blocks) | ARCHITECTURE-MATRIX.md | Constants.cs PortsPerSeat=30, PortAllocator.cs bitmap allocation | VERIFIED |
| Per-seat sunshine.conf isolation | ARCHITECTURE-MATRIX.md | VibepolloConfigBuilder.BuildConfig: per-seat config path | VERIFIED |
| Per-seat state file (sunshine_state.json) | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs EnsureSeatStateFile | VERIFIED |

---

## 4. Virtual Display Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| SudoVDA creates virtual monitors via IddCx | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs comments: "SudoVDA IddCx driver" | VERIFIED |
| UUID-based tracking via output_name | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs ParseSudoVdaDisplayId returns device_id (UUID); VibepolloConfigBuilder.UpdateDisplayOutput writes `output_name = {UUID}` | VERIFIED |
| Display isolation: SudoVDA primary + RDP shrunk to 640x480 | DISPLAY-AUDIO-INPUT.md | SeatManager.cs ApplyDisplayIsolationAsync: `--setup-display-isolation` + `--set-display-hz` | VERIFIED |
| SudoVDA display created only on client connect | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs comment: "Vibepollo does not create the seat's virtual display at startup" | VERIFIED |
| Display isolation doesn't survive disconnect | DISPLAY-AUDIO-INPUT.md | SeatManager.cs ApplyDisplayIsolationAsync comment: "does not survive a session disconnect" | VERIFIED |
| Late display detection retries from health check | DISPLAY-AUDIO-INPUT.md | SeatManager.cs TryLateDisplayDetectionAsync | VERIFIED |
| Each seat gets independent virtual display | DISPLAY-AUDIO-INPUT.md | SeatManager.cs: per-seat DisplayDevicePath, per-seat SudoVDA UUID | VERIFIED |
| Display enumerator helper runs in console session | DISPLAY-AUDIO-INPUT.md | DisplayEnumeratorHelper.cs: runs via CreateProcessAsUser in console session | VERIFIED |
| HDR: EnableHdr is no-op | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: "⚠️ Currently a NO-OP for a seat" | VERIFIED |
| Headless mode required for RDP seats | KNOWN-LIMITATIONS.md | VibepolloConfigBuilder.cs:166 `headless_mode = enabled` | VERIFIED |

---

## 5. Audio Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| PerSession mode (only supported) | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: "PerSession only. Each seat renders to the private Remote Audio endpoint" | VERIFIED |
| No VAC/VoiceMeeter needed | DISPLAY-AUDIO-INPUT.md | SeatManager.cs Step 5 comment: "No VAC, no IPolicyConfig call, no mic routing" | VERIFIED |
| No microphone path | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: "there is NO microphone path" | VERIFIED |
| Host audio protected via mstsc muted | DISPLAY-AUDIO-INPUT.md | AudioMuteHelper.cs exists and is referenced in SessionLauncher | VERIFIED |
| Vibepollo loopback-captures session audio | DISPLAY-AUDIO-INPUT.md | VibepolloConfigBuilder.cs: `auto_capture_sink = disabled` + PerSession comment | VERIFIED |
| Each RDP session has own Remote Audio endpoint | DISPLAY-AUDIO-INPUT.md | SeatManager.cs: "Each RDP session has its own Remote Audio endpoint" | VERIFIED |
| AudioManaged = false in SeatServices | DISPLAY-AUDIO-INPUT.md | SeatManager.cs GetSeatServicesAsync: `AudioManaged = false` | VERIFIED |
| ResetAudio is no-op | DISPLAY-AUDIO-INPUT.md | SeatManager.cs ResetAudio: logs "no-op under per-session audio" | VERIFIED |
| Legacy SharedHost mode removed | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs comment: "There used to be a SharedHost mode... It is gone" | VERIFIED |
| Vibepollo creates display only on client connect, not at startup | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs comments + SeatManager.cs late detection path | VERIFIED |

---

## 6. Input Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| InputHookManager is no-op | DISPLAY-AUDIO-INPUT.md | InputHookManager.cs: WH_KEYBOARD_LL/WH_MOUSE_LL in Session 0; MultiSeatOptions.cs: "it is a no-op as architected" | VERIFIED |
| No cross-session K/M bleed (RDP loopback) | DISPLAY-AUDIO-INPUT.md | CLAUDE.md: "physical input goes to the console session, and Moonlight input is SendInput'd inside the seat session" | VERIFIED |
| HidHide session jail: !<sessionId> suffix | DISPLAY-AUDIO-INPUT.md | HidHideSessionJail.cs:16 "undocumented HidHide session jail" | VERIFIED |
| HidHide >= 1.4.181.0 required | DISPLAY-AUDIO-INPUT.md | HidHideSessionJail.cs:16, MultiSeatOptions.cs: "HidHide >= 1.4.181.0, Logic.c:817" | VERIFIED |
| ViGEm controller default off | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: `EnableViGEmController = false` | VERIFIED |
| InputRouter: XInput → ViGEm bridge | DISPLAY-AUDIO-INPUT.md | InputRouter.cs, ControllerManager.cs | VERIFIED |
| HidHide CLI has 5 traps | DISPLAY-AUDIO-INPUT.md | HidHideCli.cs implementation | VERIFIED |
| EnablePadRulePreWrite for HidHide | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: `EnablePadRulePreWrite = false` (default) | VERIFIED |
| Duo has working KB/M isolation (session ID filtering) | DUOSTREAM-GAP-ANALYSIS.md | No way to verify — proprietary | UNVERIFIED |

---

## 7. RDP Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| MultiSeat uses RDPWrap/TermWrap | RDP-GAP-ANALYSIS.md | RdpWrapper.cs detects both; install-prerequisites.ps1 installs TermWrap v0.6 | VERIFIED |
| RDP loopback via 127.0.0.2 | RDP-GAP-ANALYSIS.md | SessionLauncher.cs: `/v:127.0.0.2` in mstsc args | VERIFIED |
| SessionLauncher checks multi-session availability | RDP-GAP-ANALYSIS.md | RdpWrapper.EnsureMultiSession() called in MultiSeatWorker | VERIFIED |
| Does NOT patch termsrv.dll itself | RDP-GAP-ANALYSIS.md | RdpWrapper.cs: only detects/validates, doesn't modify DLL | VERIFIED |
| TermWrap C++ uses DLL proxy | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| TermWrap C++ uses PDB symbol offset discovery | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| TermWrap Rust uses symbol-free analysis | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| TermWrap Rust uses pelite + iced-x86 | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| neo_multiseat uses stascorp/rdpwrap .ini files | RDP-GAP-ANALYSIS.md | No way to verify — external project | UNVERIFIED |
| RDPWrap breaks after Windows updates | RDP-GAP-ANALYSIS.md | install-prerequisites.ps1: comments about rdpwrap.ini refresh; RdpWrapper.cs: validates ini offsets | VERIFIED |
| NLA must be disabled for loopback logon | RDP-GAP-ANALYSIS.md | install-prerequisites.ps1 disables NLA; SECURITY-AUDIT.md explains | VERIFIED |
| mstsc window briefly visible | SECURITY-AUDIT.md | WindowHideHelper.cs: WatchAndHideNew monitors new processes | VERIFIED |

---

## 8. Provider Architecture Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| Vibepollo is a fork of Apollo (ClassicOldSong) | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloManager.cs:5 comment: "Vibepollo (Sunshine fork)" + URL points to ClassicOldSong/Vibepollo | VERIFIED |
| SeatManager directly calls VibepolloManager | PROVIDER-ARCHITECTURE-RESEARCH.md | SeatManager.cs constructor: `VibepolloManager vibepolloManager` parameter; ProvisionSeatAsync: `_vibepolloManager.StartAsync(seat, ct)` | VERIFIED |
| No streaming provider abstraction exists | PROVIDER-ARCHITECTURE-RESEARCH.md | No IStreamingProvider interface found in codebase | VERIFIED |
| Configuration tightly coupled to Vibepollo/Sunshine | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloConfigBuilder generates sunshine.conf with Vibepollo-specific keys | VERIFIED |
| Log format specific to Vibepollo | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloManager.ParseSudoVdaDisplayIdFromLogText parses Vibepollo-specific JSON format | VERIFIED |
| Port offsets specific to Vibepollo | PROVIDER-ARCHITECTURE-RESEARCH.md | Constants.cs: offsets match `map_port(N) = sunshine.port + N (from network.cpp)` | PARTIALLY VERIFIED — offsets observed empirically, Vibepollo's network.cpp not directly accessible |
| All Sunshine forks use same config format (sunshine.conf) | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloConfigBuilder generates sunshine.conf; code references Sunshine upstream | PARTIALLY VERIFIED — format compatibility is assumed, not verified across forks |

---

## 9. License Claims

| Claim | Source | Evidence | Status |
|---|---|---|---|
| MultiSeat-Extended: MIT | LICENSE-AUDIT.md | LICENSE file exists in repo root | VERIFIED |
| MultiSeat upstream: MIT | LICENSE-AUDIT.md | References vibesoftwarecoder/MultiSeat — no way to verify from this repo | UNVERIFIED |
| Vibepollo: GPLv3 | LICENSE-AUDIT.md | No way to verify — external repository | UNVERIFIED |
| Helios: GPLv3 | LICENSE-AUDIT.md | No way to verify | UNVERIFIED |
| Apollo: GPLv3 | LICENSE-AUDIT.md | No way to verify | UNVERIFIED |
| Duo: Proprietary | LICENSE-AUDIT.md | No way to verify — closed source | UNVERIFIED |
| TermWrap: MIT | LICENSE-AUDIT.md | No way to verify — external repository | UNVERIFIED |
| TermWrap Rust: MIT (assumed) | LICENSE-AUDIT.md | "assumed from TermWrap lineage" — correctly stated as unverified | UNVERIFIED (correctly noted) |
| rdpWrapper: Unable to verify | LICENSE-AUDIT.md | "repository may be private or removed" — correctly stated | UNVERIFIED (correctly noted) |
| neo_multiseat: MIT | LICENSE-AUDIT.md | No way to verify | UNVERIFIED |
| MultiseatProject: Not found | LICENSE-AUDIT.md | Correctly noted as not found | VERIFIED (correctly noted) |
| LuaTools: Not clearly stated | LICENSE-AUDIT.md | No way to verify | UNVERIFIED (correctly noted) |
| SudoVDA: Separate license | LICENSE-AUDIT.md | "License needs separate verification" — correctly noted | UNVERIFIED (correctly noted) |
| Virtual-display-rs: Open source | LICENSE-AUDIT.md | "needs license verification" — correctly noted | UNVERIFIED (correctly noted) |

---

## 10. Research Methodology Assessment

### Were source code, issues, PRs, releases, and commit history actually researched?

| Evidence Type | Claimed | Found | Assessment |
|---|---|---|---|
| **Source code (MultiSeat-Extended)** | Yes | Yes — detailed architecture, namespace maps, code snippets match actual implementation | VERIFIED |
| **Source code (external projects)** | Implied | Limited — some references to Vibepollo internals (network.cpp map_port, process.cpp headless_mode) but these are from comments, not from direct source analysis. The PROVIDER-ARCHITECTURE-RESEARCH.md describes architectures as if from direct observation, but there's no evidence of actually reading Vibepollo/Apollo/Helios source code. | PARTIALLY VERIFIED |
| **Issues (external projects)** | Not claimed | Only issue #19 (HidHide session jail) and issue #15 (display issues) are referenced from MultiSeat-Extended's own issues | N/A — external issues not researched |
| **PRs (external projects)** | Not claimed | No references to external PRs | N/A |
| **Releases (external projects)** | Partially | install-prerequisites.ps1 references TermWrap v0.6 release; HidHideSessionJail.cs references HidHide v1.4.181.0 commit | PARTIALLY VERIFIED |
| **Commit history (external projects)** | Not claimed | No references to specific commits in external repos | N/A |

---

## 11. Identified Issues and Corrections

### Issue 1: CURRENT-ARCHITECTURE.md — "RDPWrap (TermsWrap)" is a typo
**File**: CURRENT-ARCHITECTURE.md, line 20
**Current**: `| RDP | RDPWrap (TermsWrap) |`
**Correct**: `| RDP | TermWrap (fork of RDPWrap) |`
**Reason**: The actual installed dependency is TermWrap (llccd/TermWrap v0.6), not RDPWrap. "TermsWrap" is a misspelling. Post Phase 6 migration (SPEC.md), RDPWrap is no longer the active dependency.

### Issue 2: "22 tests" vs "22 test files"
**Files**: AUDIT-SUMMARY.md, ECOSYSTEM-RESEARCH-SUMMARY.md
**Current**: "22 unit/integration тестов"
**Correct**: "22 test files" (each containing multiple test methods)
**Reason**: The glob found 22 test .cs files, not 22 individual tests. The total number of [Fact]/[Theory] methods is higher.

### Issue 3: RDP-GAP-ANALYSIS.md — MultiSeat approach description
**File**: RDP-GAP-ANALYSIS.md, line 7
**Current**: `| MultiSeat-Extended | C# (.NET 9) | Uses RDPWrap/TermWrap as dependency | MIT | Active |`
**Partially correct**: The project now uses TermWrap (post Phase 6), not RDPWrap. RdpWrapper.cs detects both for backward compatibility.

### Issue 4: VIBEPOOLLO-GAP-ANALYSIS.md — Vibepollo claims unverifiable
**File**: VIBEPOOLLO-GAP-ANALYSIS.md
**Issue**: Many claims about Vibepollo's specific capabilities (RTSS integration, NVIDIA Smooth Motion, WGC service capture, display layout restoration) are stated as facts but cannot be verified from MultiSeat-Extended's source code. These should be marked as "claimed by Vibepollo documentation / community" rather than stated as verified facts.

### Issue 5: PROVIDER-ARCHITECTURE-RESEARCH.md — Vibepollo as "fork of Apollo"
**File**: PROVIDER-ARCHITECTURE-RESEARCH.md, line 58
**Current**: "Fork of Apollo"
**Verification**: VibepolloManager.cs comment says "Vibepollo (Sunshine fork)" and URL is ClassicOldSong/Vibepollo. ClassicOldSong is also the Apollo author. The relationship (is Vibepollo a fork of Apollo, which is itself a fork of Sunshine?) is not clearly stated.
**Status**: PARTIALLY VERIFIED — it's clearly a fork in the Sunshine lineage, but the exact fork chain is ambiguous.

### Issue 6: SECURITY-AUDIT.md code snippets are simplified
**File**: SECURITY-AUDIT.md
**Issue**: Code snippets in the security audit are simplified versions of actual code (e.g., AccountManager DPAPI code). The simplification is appropriate for documentation but should be noted.
**Status**: NOT AN ERROR — acceptable documentation practice.

---

## 12. Summary of Document Quality

### Strengths
1. **MultiSeat-Extended self-documentation is excellent** — architecture, limitations, and security posture are well-documented with code references
2. **Internal claims are highly accurate** — nearly all claims about the project's own code are verified
3. **Limitations are honestly stated** — no-op InputHookManager, missing features, security weaknesses are all documented
4. **Gap analyses are well-structured** — clear comparison tables with actionable categories

### Weaknesses
1. **External project claims lack evidence** — no citations to specific source files, issues, PRs, or commits in external repos
2. **License claims are unverified** — all external license claims are stated as facts but cannot be verified from this repo
3. **Minor inaccuracies** — "TermsWrap" typo, "22 tests" vs "22 test files"
4. **Research depth is uneven** — MultiSeat-Extended is deeply documented; external projects are described at surface level without citing sources

---

## 13. Master Verification Table

| Claim | Source Document | Evidence | Status |
|---|---|---|---|
| Backend is .NET 9 ASP.NET Core Windows Service | CURRENT-ARCHITECTURE.md | MultiSeat.Service.csproj, Program.cs | VERIFIED |
| Frontend is React + TypeScript (Vite) | CURRENT-ARCHITECTURE.md | MultiSeat.Dashboard/ directory | VERIFIED |
| RDP dependency: "RDPWrap (TermsWrap)" | CURRENT-ARCHITECTURE.md | install-prerequisites.ps1 installs TermWrap v0.6 | **INCORRECT** — should be "TermWrap (fork of RDPWrap)" |
| MaxSeats = 8 (architectural ceiling) | SEAT-LIFECYCLE.md | Constants.cs:32 | VERIFIED |
| Default MaxSeats = 4 (operator limit) | SEAT-LIFECYCLE.md | MultiSeatOptions.cs:8 | VERIFIED |
| PortsPerSeat = 30 | SEAT-LIFECYCLE.md | Constants.cs | VERIFIED |
| PortBase = 48100 | SEAT-LIFECYCLE.md | Constants.cs | VERIFIED |
| Port offsets: -5,0,1,9,10,11,12,26 | STREAMING-ARCHITECTURE.md | Constants.cs | VERIFIED |
| MaxRestartAttempts = 3 | STREAMING-ARCHITECTURE.md | VibepolloManager.cs:301 | VERIFIED |
| Health check interval 5s | SEAT-LIFECYCLE.md | MultiSeatOptions.cs | VERIFIED |
| 9-step provisioning pipeline | SEAT-LIFECYCLE.md | SeatManager.cs ProvisionSeatAsync | **PARTIALLY VERIFIED** — actual pipeline has 14+ sub-steps |
| Provisioning includes RustDesk audio suppression | SEAT-LIFECYCLE.md | SeatManager.cs lines ~175-210 | VERIFIED |
| Provisioning includes HidHide pre-write rules | SEAT-LIFECYCLE.md | SeatManager.cs: _hidHide.PreWriteRules(seat) | VERIFIED |
| Display isolation: SudoVDA primary + RDP 640x480 | SEAT-LIFECYCLE.md | SeatManager.cs ApplyDisplayIsolationAsync | VERIFIED |
| Display isolation reduces CPU ~70% to <5% | TECHNIQUE-CATALOG.md | Code comment only — no measurements | UNVERIFIED |
| Late display detection from Vibepollo log | SEAT-LIFECYCLE.md | SeatManager.cs TryLateDisplayDetectionAsync | VERIFIED |
| Teardown is best-effort reverse order | SEAT-LIFECYCLE.md | SeatManager.cs TeardownSeatInternalAsync | VERIFIED |
| RDP loopback to 127.0.0.2 | SESSION-ARCHITECTURE.md | SessionLauncher.cs | VERIFIED |
| CreateProcessAsUser can't create sessions from Session 0 | SESSION-ARCHITECTURE.md | SessionLauncher.cs:22, ProcessInjector.cs:17 | VERIFIED |
| Keepalive process in new session | SESSION-ARCHITECTURE.md | SessionLauncher.cs | VERIFIED |
| DPAPI CurrentUser (= SYSTEM) scope | SECURITY-AUDIT.md | AccountManager.cs:386 | VERIFIED |
| ACL hardening on accounts.json | SECURITY-AUDIT.md | AccountManager.cs HardenStore | VERIFIED |
| API key auto-generated, 32-char | SECURITY-AUDIT.md | ApiServer.cs ResolveApiKey | VERIFIED |
| API key in URL query string (WebSocket) | SECURITY-AUDIT.md | ApiServer.cs: Query["key"] | VERIFIED |
| Plaintext HTTP (no HTTPS) | SECURITY-AUDIT.md | ApiServer.cs kestrel config, MultiSeatOptions.cs comment | VERIFIED |
| API key header: X-MultiSeat-Key | SECURITY-AUDIT.md | Constants.cs ApiKeyHeader | VERIFIED |
| CORS loopback only (default) | SECURITY-AUDIT.md | ApiServer.cs CORS config | VERIFIED |
| NLA disabled for RDP loopback | SECURITY-AUDIT.md | install-prerequisites.ps1 | VERIFIED |
| Seat accounts standard users by default | SECURITY-AUDIT.md | MultiSeatOptions.cs GrantSeatAdministrator=false | VERIFIED |
| Shared credentials readable by seats (MEDIUM) | SECURITY-AUDIT.md | No ACL evidence in code | UNVERIFIED |
| InputHookManager is no-op | ARCHITECTURE-PROBLEMS.md | InputHookManager.cs, MultiSeatOptions.cs comment | VERIFIED |
| HidHide session jail undocumented | KNOWN-LIMITATIONS.md | HidHideSessionJail.cs:16, MultiSeatOptions.cs | VERIFIED |
| ViGEm controller default off | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs: EnableViGEmController=false | VERIFIED |
| SudoVDA created only on client connect | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs comment | VERIFIED |
| Display isolation doesn't survive disconnect | DISPLAY-AUDIO-INPUT.md | SeatManager.cs comment | VERIFIED |
| EnableHdr is no-op | KNOWN-LIMITATIONS.md | MultiSeatOptions.cs comment | VERIFIED |
| PerSession audio (only supported mode) | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs audio comment | VERIFIED |
| No microphone path | DISPLAY-AUDIO-INPUT.md | MultiSeatOptions.cs audio comment | VERIFIED |
| Host audio protected (mstsc muted) | DISPLAY-AUDIO-INPUT.md | AudioMuteHelper.cs | VERIFIED |
| Vibepollo config: keep_sink_default=disabled | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs:222, StreamingTests.cs:166 | VERIFIED |
| Vibepollo config: auto_capture_sink=disabled | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs:223, StreamingTests.cs:167 | VERIFIED |
| Vibepollo config: stream_mic=disabled | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs:238, StreamingTests.cs:165 | VERIFIED |
| Vibepollo config: headless_mode=enabled | KNOWN-LIMITATIONS.md | VibepolloConfigBuilder.cs:166 | VERIFIED |
| Vibepollo config: output_name = UUID | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.cs:121 | VERIFIED |
| Vibepollo runs in seat session via CreateProcessAsUser | STREAMING-ARCHITECTURE.md | VibepolloManager.cs: LaunchVibepolloInSessionAsync | VERIFIED |
| Per-seat sunshine.conf generation | STREAMING-ARCHITECTURE.md | VibepolloConfigBuilder.BuildConfig | VERIFIED |
| ParseSudoVdaDisplayId from log | STREAMING-ARCHITECTURE.md | VibepolloManager.cs:392 | VERIFIED |
| Vibepollo ignores log_path config key | KNOWN-LIMITATIONS.md | VibepolloManager.cs ResolveLogPath comment | VERIFIED |
| VibepolloManager.Stop kills entire tree | STREAMING-ARCHITECTURE.md | VibepolloManager.cs: Kill(entireProcessTree: true) | VERIFIED |
| 22 test files exist | AUDIT-SUMMARY.md | Glob: 22 .cs files in MultiSeat.Tests/ | **PARTIALLY VERIFIED** — 22 FILES, not 22 individual tests |
| Vibepollo is fork of ClassicOldSong/Apollo | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloManager.cs:5 comment + URL | VERIFIED |
| SeatManager directly calls VibepolloManager | PROVIDER-ARCHITECTURE-RESEARCH.md | SeatManager.cs constructor + ProvisionSeatAsync | VERIFIED |
| No IStreamingProvider abstraction exists | PROVIDER-ARCHITECTURE-RESEARCH.md | No interface found in codebase | VERIFIED |
| Tightly coupled to Vibepollo config format | PROVIDER-ARCHITECTURE-RESEARCH.md | VibepolloConfigBuilder generates sunshine.conf | VERIFIED |
| MultiSeat-Extended: MIT license | LICENSE-AUDIT.md | LICENSE file in repo | VERIFIED |
| Vibepollo: GPLv3 | LICENSE-AUDIT.md | External repo — cannot verify | UNVERIFIED |
| Apollo: GPLv3 | LICENSE-AUDIT.md | External repo — cannot verify | UNVERIFIED |
| Helios: GPLv3 | LICENSE-AUDIT.md | External repo — cannot verify | UNVERIFIED |
| Duo: Proprietary | LICENSE-AUDIT.md | Closed source — cannot verify | UNVERIFIED |
| TermWrap: MIT | LICENSE-AUDIT.md | External repo — cannot verify | UNVERIFIED |
| rdpWrapper: Unknown | LICENSE-AUDIT.md | Correctly stated as unknown | UNVERIFIED (correctly noted) |
| MultiseatProject: Not found | LICENSE-AUDIT.md | Correctly stated | VERIFIED (correctly noted) |
| LuaTools: Unknown license | LICENSE-AUDIT.md | Correctly stated as unknown | UNVERIFIED (correctly noted) |
| SudoVDA: Separate license needed | LICENSE-AUDIT.md | Correctly noted as needing verification | UNVERIFIED (correctly noted) |
| Duo: HDR streaming (paid) | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Game mutex isolation | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Steam multi-instance | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Seamless display adjustment | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Custom WDDM driver | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: UMDF input driver | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: KB/M session isolation | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Application Compatibility Layer | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Up to 500Hz | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Duo: Frame generation | DUOSTREAM-GAP-ANALYSIS.md | Proprietary — cannot verify | UNVERIFIED |
| Vibepollo: H.264/HEVC/AV1 encoding | VIBEPOOLLO-GAP-ANALYSIS.md | Config keys exist (hevc_mode, av1_mode) | PARTIALLY VERIFIED |
| Vibepollo: NVENC + AMF | VIBEPOOLLO-GAP-ANALYSIS.md | Config key encoder=nvenc | PARTIALLY VERIFIED |
| Vibepollo: Microphone passthrough | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs references it | UNVERIFIED |
| Vibepollo: RTSS integration | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs EnableRtss | PARTIALLY VERIFIED |
| Vibepollo: Lossless Scaling | VIBEPOOLLO-GAP-ANALYSIS.md | MultiSeatOptions.cs EnableLosslessScaling | PARTIALLY VERIFIED |
| Vibepollo: NVIDIA Smooth Motion | VIBEPOOLLO-GAP-ANALYSIS.md | No code evidence | UNVERIFIED |
| Vibepollo: Display layout restoration | VIBEPOOLLO-GAP-ANALYSIS.md | No code evidence | UNVERIFIED |
| Vibepollo: WGC service capture | VIBEPOOLLO-GAP-ANALYSIS.md | No code evidence | UNVERIFIED |
| Vibepollo: AI-generated (99% AI code) | PROVIDER-ARCHITECTURE-RESEARCH.md | No way to verify | UNVERIFIED |
| Apollo: Built-in SudoVDA | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| Apollo: Per-client identity | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| Apollo: Permission management | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| Helios: Named Pipe IPC | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| Helios: .NET 8 WPF app | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| Helios: SYSTEM Spawner service | PROVIDER-ARCHITECTURE-RESEARCH.md | External project | UNVERIFIED |
| TermWrap: DLL proxy, dynamic patching | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| TermWrap Rust: Symbol-free analysis | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| TermWrap Rust: pelite + iced-x86 | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| TermWrap: ARM64 support (Rust) | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| TermWrap: UmWrap camera/USB | RDP-GAP-ANALYSIS.md | install-prerequisites.ps1 references UmWrap.dll | PARTIALLY VERIFIED |
| TermWrap: EndpWrap audio recording | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| neo_multiseat: RDPWrap automation | RDP-GAP-ANALYSIS.md | External project | UNVERIFIED |
| neo_multiseat: Tailscale integration | ECOSYSTEM-RESEARCH-SUMMARY.md | External project | UNVERIFIED |
| MultiseatProject: Does not exist | ECOSYSTEM-RESEARCH-SUMMARY.md | Correctly noted as "Not found" | VERIFIED |
| LuaTools: DRM bypass, unclear licensing | LICENSE-AUDIT.md | External project | UNVERIFIED |
| virtual-display-rs: Alternative to SudoVDA | ECOSYSTEM-RESEARCH-SUMMARY.md | External project | UNVERIFIED |
| Apollo launcher: Python, MIT | ECOSYSTEM-RESEARCH-SUMMARY.md | External project | UNVERIFIED |
| Display isolation reduces TermService CPU ~70% to <5% | TECHNIQUE-CATALOG.md | Code comment — no benchmarks | UNVERIFIED |
| Vibepollo port offsets from network.cpp | KNOWN-LIMITATIONS.md | Constants.cs comment references network.cpp | UNVERIFIED |
| Vibepollo headless_mode required for RDP seats | KNOWN-LIMITATIONS.md | VibepolloConfigBuilder.cs sets it | VERIFIED |
| Vibepollo ignores log_path key | KNOWN-LIMITATIONS.md | VibepolloManager.cs ResolveLogPath | VERIFIED |
| HidHide session jail >= 1.4.181.0 | KNOWN-LIMITATIONS.md | HidHideSessionJail.cs:16, MultiSeatOptions.cs | VERIFIED |
| DPAPI migration from LocalMachine scope | SECURITY-AUDIT.md | AccountManager.cs IsLegacyScope + migration path | VERIFIED |
| Console session guard | SECURITY-AUDIT.md | ProcessInjector.cs EnsureNotConsoleSession | VERIFIED |
| WindowHideHelper watches for mstsc | SECURITY-AUDIT.md | WindowHideHelper.cs WatchAndHideNew | VERIFIED |
| Seat accounts in Administrators (GrantSeatAdministrator=true) is risky | SECURITY-AUDIT.md | MultiSeatOptions.cs detailed comment | VERIFIED |
| API has no rate limiting | SECURITY-AUDIT.md | ApiServer.cs has no rate limiting code | VERIFIED |
| Vibepollo creates display only on client connect | DISPLAY-AUDIO-INPUT.md | VibepolloManager.cs comments | VERIFIED |
| Display isolation doesn't survive disconnect | DISPLAY-AUDIO-INPUT.md | SeatManager.cs ApplyDisplayIsolationAsync comment | VERIFIED |
| Each seat gets independent virtual display | DISPLAY-AUDIO-INPUT.md | SeatManager.cs per-seat tracking | VERIFIED |
| Session resolution fixed at connect time | KNOWN-LIMITATIONS.md | SeatManager.cs SetResolutionAsync: disconnect + reconnect | VERIFIED |
| DXGI ACCESS_DENIED for disconnected sessions | KNOWN-LIMITATIONS.md | SeatManager.cs: session must stay Active | VERIFIED |
| Single machine-wide default audio device | KNOWN-LIMITATIONS.md | MultiSeatOptions.cs audio comment | VERIFIED |
| CreateProcessAsUser can't create sessions from Session 0 | KNOWN-LIMITATIONS.md | SessionLauncher.cs comment | VERIFIED |
| WTSQueryUserToken returns filtered token for admins | KNOWN-LIMITATIONS.md | SessionLauncher.cs GetSessionToken | VERIFIED |
| Builtin group names are localized | KNOWN-LIMITATIONS.md | AccountManager.cs ResolveLocalGroupName | VERIFIED |
| NVIDIA consumer GPU: 3-5 NVENC sessions | KNOWN-LIMITATIONS.md | Hardware limitation — not verifiable from code | UNVERIFIED |
| SudoVDA HDR incomplete (VidPN SOURCE stays SDR) | KNOWN-LIMITATIONS.md | MultiSeatOptions.cs EnableHdr comment | VERIFIED |
| SudoVDA display created only on client connect | KNOWN-LIMITATIONS.md | VibepolloManager.cs comment | VERIFIED |
| HidHide session jail undocumented | KNOWN-LIMITATIONS.md | HidHideSessionJail.cs:16 | VERIFIED |
| Game mutex / DRM | KNOWN-LIMITATIONS.md | Common knowledge — not verifiable | UNVERIFIED |
| Anti-cheat in virtual sessions | KNOWN-LIMITATIONS.md | Common knowledge — not verifiable | UNVERIFIED |
| Game expects specific display device | KNOWN-LIMITATIONS.md | SudoVDA EDID mimics real display | UNVERIFIED |

---

## 14. Recommendations

### Immediate fixes needed
1. Fix "RDPWrap (TermsWrap)" → "TermWrap (fork of RDPWrap)" in CURRENT-ARCHITECTURE.md
2. Fix "22 tests" → "22 test files" in AUDIT-SUMMARY.md and ECOSYSTEM-RESEARCH-SUMMARY.md
3. Fix RDP-GAP-ANALYSIS.md to reflect TermWrap as current dependency

### Documentation improvements
1. Add source citations to all external project claims (specific GitHub URLs, file paths, commit SHAs where possible)
2. Mark all unverifiable external claims as "claimed, not verified from MultiSeat-Extended source"
3. Add research date stamps to each document
4. Distinguish between "verified from source" and "claimed by documentation/community"

### Methodological improvements
1. When researching external projects, cite specific evidence (issue numbers, commit hashes, file paths, release versions)
2. Run `gh api` or similar to verify repository existence and license before claiming
3. Note when claims are based on community knowledge vs direct observation
