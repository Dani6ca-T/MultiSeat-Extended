# MultiSeat-Extended: Итоговый отчёт исследования экосистемы

## 1. Исследованные проекты

### GROUP A — MultiSeat

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| MultiSeat-Extended | Dani6ca-T/MultiSeat-Extended | MIT | C# (.NET 9) | Active (our project) |
| MultiSeat upstream | vibesoftwarecoder/MultiSeat | MIT | C# (.NET 9) | Active (fork base) |

### GROUP B — Streaming

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| Vibepollo | Nonary/Vibepollo | GPLv3 | C++ | Active |
| Helios | MintCapybara924/Helios-Sunshine-Manager | GPLv3 | C# (.NET 8) | Active |
| Apollo | ClassicOldSong/Apollo | GPLv3 | C++ | Active |
| Apollo launcher | neo0oen619/apollo-multi-instance-launcher | MIT | Python | Active |

### GROUP C — DuoStream

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| Duo | DuoStream/Duo | Proprietary | C#/Proprietary | Active (paid features) |

### GROUP D — RDP / Windows Sessions

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| TermWrap | llccd/TermWrap | MIT | C++ | Active |
| TermWrap Rust | kernalix7/rdprrap | MIT | Rust | Active |
| rdpWrapper | redesk-io/rdpWrapper | Unknown | Unknown | Unknown |

### GROUP E — Other MultiSeat

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| neo_multiseat | neo0oen619/neo_multiseat | MIT | PowerShell | Active |
| MultiseatProject | Abdulhanan535/MultiseatProject | N/A | N/A | Not found |

### GROUP F — Tools

| Project | Repository | License | Language | Status |
|---------|-----------|---------|----------|--------|
| LuaTools | madoiscool/LuaTools | Unknown | C# (.NET 8) | Active (not relevant) |

---

## 2. Найденные дополнительные проекты

| Project | Repository | Purpose | Relevance |
|---------|-----------|---------|-----------|
| virtual-display-rs | MolotovCherry/virtual-display-rs | Rust IddCx virtual display driver | Medium — alternative to SudoVDA |

---

## 3. Что MultiSeat-Extended уже умеет

### ✅ Fully Implemented

1. Windows user account management (create, link, delete)
2. RDP loopback session creation
3. Session monitoring and reconnect
4. Per-seat Vibepollo streaming server
5. Virtual display (SudoVDA) with UUID tracking
6. Display isolation (SudoVDA primary + RDP shrunk)
7. Per-session audio isolation (no VAC needed)
8. Port allocation (30-port blocks, bitmap)
9. Windows Firewall management
10. ViGEm virtual controllers (optional)
11. HidHide session jail (optional)
12. XInput controller routing
13. Launch-on-connect apps
14. Client resolution following
15. Shared game library
16. Emulator netplay (RetroArch)
17. Health check loop (5s)
18. WebSocket real-time updates
19. API key authentication
20. DPAPI credential encryption
21. ACL hardening
22. Playnite, RTSS, Lossless Scaling integration
23. HDR probe (diagnostics)
24. NVENC quality presets
25. Auto-provisioning from presets
26. Coexistence with standalone Vibepollo
27. 22 test files (unit + integration)

### ⚠️ Partially Implemented

1. Display isolation (works but doesn't survive disconnect)
2. Process tracking (Vibepollo PID only, no game PID)
3. Network isolation (loopback option only)

---

## 4. Что есть у других проектов

### Duo (Proprietary)

1. HDR streaming (paid)
2. Game mutex isolation (compatibility layer)
3. Steam multi-instance (process patching)
4. Seamless display adjustment
5. Custom WDDM display driver
6. UMDF input driver
7. Application Compatibility Layer
8. KB/M session isolation
9. 500Hz support (paid)
10. Frame generation support

### Vibepollo (GPLv3)

1. Multi-codec encoding (H.264, HEVC, AV1)
2. NVENC + AMF support
3. Encoder auto-detection
4. RTSS integration
5. Lossless Scaling integration
6. NVIDIA Smooth Motion
7. Display layout restoration
8. WGC service capture
9. Microphone passthrough
10. HDR metadata handling

### Helios (GPLv3)

1. Multi-instance management (core feature)
2. Named Pipe IPC (UI ↔ SYSTEM service)
3. Per-instance config isolation
4. Per-instance audio routing
5. Batch operations (Start All/Stop All)
6. Per-instance port allocation
7. SYSTEM execution via Spawner service

### TermWrap (MIT)

1. Dynamic termsrv patching
2. Symbol-free offset discovery (Rust fork)
3. Camera/USB redirection (UmWrap)
4. Audio recording redirection (EndpWrap)
5. ARM64 support (Rust fork)

### neo_multiseat (MIT)

1. Automated RDPWrap recovery
2. Live session monitoring
3. Tailscale integration
4. NLA/TLS hardening templates
5. CSV export of session logs

---

## 5. Лучшие найденные технические решения

| Technique | Best Implementation | Why |
|-----------|-------------------|-----|
| RDP session patching | TermWrap Rust | Symbol-free, survives updates |
| Virtual display | SudoVDA (in MultiSeat) | Proven, UUID-based tracking |
| Display isolation | MultiSeat-Extended | Unique, reduces CPU |
| Per-session audio | MultiSeat-Extended | No VAC needed, true isolation |
| Provider lifecycle | Helios | Clean Named Pipe IPC pattern |
| Multi-instance management | Helios | Automated, per-instance isolation |
| Gamepad isolation | MultiSeat-Extended (HidHide) | Undocumented but proven |
| Session monitoring | MultiSeat-Extended | Comprehensive health checks |
| Credential encryption | MultiSeat-Extended | DPAPI SYSTEM scope |
| Crash recovery | MultiSeat-Extended | Auto-restart with limits |

---

## 6. Что можно переиспользовать

| Component | Source | License | Can Reuse |
|-----------|--------|---------|-----------|
| RDP loopback session creation | MultiSeat-Extended | MIT | ✅ Same project |
| Display isolation | MultiSeat-Extended | MIT | ✅ Same project |
| Per-session audio | MultiSeat-Extended | MIT | ✅ Same project |
| Port allocation | MultiSeat-Extended | MIT | ✅ Same project |
| HidHide session jail | MultiSeat-Extended | MIT | ✅ Same project |
| Health check loop | MultiSeat-Extended | MIT | ✅ Same project |
| DPAPI credentials | MultiSeat-Extended | MIT | ✅ Same project |
| ACL hardening | MultiSeat-Extended | MIT | ✅ Same project |
| TermWrap approach | TermWrap | MIT | ⚠️ Adapt |
| Helios IPC pattern | Helios | GPLv3 | ⚠️ Reference only |
| neo_multiseat monitoring | neo_multiseat | MIT | ⚠️ Adapt |

---

## 7. Что нельзя/не стоит переиспользовать

| Component | Source | License | Cannot Reuse | Reason |
|-----------|--------|---------|--------------|--------|
| Duo code | DuoStream/Duo | Proprietary | ❌ | Closed-source |
| Duo drivers | DuoStream/Duo | Proprietary | ❌ | Closed-source |
| Vibepollo code (embedded) | Nonary/Vibepollo | GPLv3 | ⚠️ | Copyleft if linked |
| Helios code (embedded) | Helios | GPLv3 | ⚠️ | Copyleft if linked |
| Apollo code (embedded) | Apollo | GPLv3 | ⚠️ | Copyleft if linked |
| LuaTools | madoiscool/LuaTools | Unknown | ❌ | DRM bypass tools |

---

## 8. Главные недостатки MultiSeat-Extended

1. **No streaming provider abstraction** — Vibepollo tightly coupled
2. **No HDR support** — EnableHdr is no-op
3. **No KB/M session isolation** — InputHookManager is no-op
4. **No game mutex isolation** — games may conflict
5. **No Steam multi-instance** — Steam terminates second instance
6. **No HTTPS for API** — plaintext HTTP
7. **No microphone path** — PerSession trade-off
8. **No frame generation** — missing NVIDIA Smooth Motion
9. **No dynamic port allocation** — MaxSeats ceiling (8)
10. **No metrics export** — no Prometheus/Grafana integration

---

## 9. Главные преимущества MultiSeat-Extended

1. **Open source** (MIT) — fully transparent
2. **Display isolation** — unique SudoVDA primary + RDP shrunk approach
3. **Per-session audio** — no VAC/VoiceMeeter needed
4. **HidHide session jail** — proven gamepad isolation
5. **Emulator netplay** — RetroArch per-seat ports
6. **Shared game library** — icacls-based provisioner
7. **Late display detection** — handles Vibepollo lazy creation
8. **Orphan cleanup** — WMI-based, safe for standalone Vibepollo
9. **Detailed diagnostics** — HidHideInspector, LogFilterInspector
10. **Well-documented security** — CLAUDE.md, security-posture.md

---

## 10. Что делает Duo лучше

1. **HDR streaming** — working implementation
2. **Game mutex isolation** — compatibility layer
3. **Steam multi-instance** — process patching
4. **Seamless display adjustment** — no reconnect needed
5. **Custom drivers** — WDDM + UMDF for better integration
6. **KB/M isolation** — session ID filtering works
7. **Higher refresh rates** — up to 500Hz
8. **Frame generation** — NVIDIA Smooth Motion
9. **All-in-one** — no external dependencies

---

## 11. Что Vibepollo уже решает

1. **All encoding/streaming** — H.264, HEVC, AV1, NVENC, AMF
2. **Virtual display creation** — SudoVDA integration
3. **Display resolution matching** — client-driven
4. **Audio capture** — WASAPI loopback
5. **Input forwarding** — gamepad, KB/M, touch
6. **Pairing/authentication** — PIN, certificates
7. **Web UI** — configuration dashboard
8. **Headless mode** — auto-create virtual display
9. **Recovery** — crash detection, auto-restart
10. **Advanced integration** — Playnite, RTSS, Lossless Scaling, NVIDIA Smooth Motion

---

## 12. Что должен делать MultiSeat-Extended

### Orchestration Layer (core value)

1. **Session lifecycle** — create, monitor, reconnect, destroy
2. **User management** — create, delete, credentials
3. **Port allocation** — per-seat blocks
4. **Display isolation** — SudoVDA primary + RDP shrunk
5. **Audio routing** — per-session Remote Audio
6. **Health monitoring** — session, process, display
7. **Crash recovery** — auto-restart with limits
8. **Provider management** — start/stop/restart Vibepollo
9. **Security** — credentials, ACLs, authentication
10. **API + Dashboard** — management interface

### Game/Process Features (consider adding)

11. **Game mutex isolation** — study Duo's approach
12. **Steam multi-instance** — via shared library (current) or patching (future)
13. **KB/M isolation** — re-architect InputHookManager

---

## 13. Что НЕ должен делать MultiSeat-Extended

1. **Encoding/streaming** — delegate to Vibepollo
2. **Virtual display creation** — delegate to SudoVDA/Vibepollo
3. **Audio capture** — delegate to Vibepollo
4. **Input forwarding** — delegate to Vibepollo
5. **Pairing/authentication** — delegate to Vibepollo
6. **Encoder probing** — delegate to Vibepollo
7. **Display resolution matching** — delegate to Vibepollo
8. **Frame generation** — delegate to Vibepollo

---

## 14. Windows limitations

See `WINDOWS-LIMITATIONS.md` for full details.

Key limitations:
- Concurrent RDP sessions require patching
- CreateProcessAsUser cannot create sessions from Session 0
- Session resolution fixed at connect time
- DXGI ACCESS_DENIED for disconnected sessions
- Single machine-wide default audio device
- NLA must be disabled for loopback
- RDPWrap breaks after Windows updates

---

## 15. Game limitations

- Game mutex / single instance
- Anti-cheat in virtual sessions
- Steam single instance
- DRM in virtual sessions
- Hardware-based DRM

---

## 16. Security concerns

- API key in plaintext HTTP
- No HTTPS support
- Administrator can read all credentials
- RDP NLA disabled machine-wide
- RDP certificate warnings suppressed
- Seat accounts could be elevated (if GrantSeatAdministrator)

---

## 17. License concerns

- MultiSeat-Extended: MIT ✅
- Vibepollo/Apollo/Helios: GPLv3 ⚠️ (safe as external process)
- Duo: Proprietary ❌ (cannot reuse)
- TermWrap: MIT ✅
- SudoVDA: Unknown ⚠️ (verify separately)

---

## 18. Recommended architecture direction

```
MultiSeat-Extended (MIT)
    ├── Session Layer (RDP loopback, TermWrap)
    ├── Provider Layer (IStreamingProvider → VibepolloProvider)
    ├── Display Layer (SudoVDA, display isolation)
    ├── Audio Layer (PerSession, no VAC)
    ├── Input Layer (HidHide, ViGEm, InputRouter)
    ├── Security Layer (DPAPI, ACLs, API key)
    └── Management Layer (API, Dashboard, WebSocket)
```

**Key principle**: MultiSeat-Extended is an **orchestration layer**, not a streaming server.

---

## 19. Recommended development order

### Phase 1: Stabilize (current)

- Fix existing bugs
- Improve test coverage
- Document architecture

### Phase 2: Provider Abstraction

- Extract IStreamingProvider interface
- Implement VibepolloProvider
- Test with Apollo as second provider

### Phase 3: Missing Features

- HTTPS for API
- Metrics export (Prometheus)
- Dynamic port allocation (remove MaxSeats ceiling)
- Game process tracking

### Phase 4: Advanced Features

- HDR support (when Vibepollo enables it)
- KB/M session isolation (re-architecture)
- Frame generation integration
- Microphone passthrough (when WebRTC mic available)

### Phase 5: Ecosystem

- TermWrap integration (replace RDPWrap dependency)
- Helios-inspired Named Pipe IPC
- Multi-GPU support
- Docker/container support

---

## 20. Неизвестные вопросы

1. **SudoVDA license** — what are the exact terms?
2. **Duo's exact architecture** — closed-source limits analysis
3. **Vibepollo roadmap** — what features are planned?
4. **TermWrap stability** — how reliable is dynamic patching?
5. **WebRTC mic support** — when will Vibepollo support it?
6. **Multi-GPU assignment** — how to map seats to GPUs?
7. **Anti-cheat compatibility** — which games work, which don't?
8. **Performance limits** — how many seats on given hardware?
9. **Network isolation** — how to isolate seats on network level?
10. **Update mechanism** — how to update MultiSeat without losing config?

---

## HYPOTHESIS VALIDATION

### Hypothesis

```
MultiSeat-Extended should be an orchestration layer, not a streaming server.
```

### VALIDATED ✅

**Reasons**:

1. Vibepollo/Apollo/Sunshine already handle streaming excellently
2. MultiSeat's unique value is session/user/port/display/audio orchestration
3. Helios proves the orchestration layer pattern works
4. Duo's success shows the market needs orchestration, not another streaming server
5. The boundary between streaming and orchestration is clean and well-defined

**Conclusion**: MultiSeat-Extended should focus on being the best open-source multiseat orchestrator, leveraging existing streaming providers rather than reimplementing streaming functionality.
