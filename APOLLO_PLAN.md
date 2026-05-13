# ApolloVibe Optimization Plan

Last updated: 2026-05-02 (Phase 3 done; Phase 6 roadmap added)  
Repos: https://github.com/vibesoftwarecoder/Apollo | https://github.com/vibesoftwarecoder/moonlight-common-c  
Local clone: `C:\dev\Apollo-Development`  
Working branch: `stream-mic-rebase`

---

## Background

MultiSeat currently runs **logabell/Apollo fork v2026.3.18-mic.1** — a build of
ClassicOldSong/Apollo v0.4.6 (July 2024) with 6 cherry-picked commits that add
`stream_mic = enabled` for Moonlight mic passthrough via Steam Streaming Microphone.

**Problems with the current setup:**
- Apollo v0.4.6 is ~14 months behind Sunshine v2025.924 (September 2025), missing
  async NVENC encode, better frame timing, AV1 improvements, and color matrix fixes.
- The logabell fork is unmaintained (author on paternity leave as of April 2026).
  PR #1428 is open but unreviewed. Upstream Sunshine mic PR #4078 was closed/rejected.
- `stream_mic` is a fork-only feature — it will never land upstream.
- The binary version string is `0.0.0.0affdaa.dirty` — built from uncommitted changes.

**Decision:** Skip Phase 1 (config-only tuning) and fold it into Phase 2.
Single deployment once the new binary is ready.

---

## Phase 2 — vibesoftwarecoder/Apollo fork (ApolloVibe)  ← NEARLY COMPLETE

Goal: a clean, maintained fork on the latest Apollo HEAD with the mic passthrough
patches, better NVENC config, and a proper version string.

### 2.1 — Fork and rebase  ✅ DONE (2026-04-30)
- [x] Fork `ClassicOldSong/Apollo` → `vibesoftwarecoder/Apollo` on GitHub
- [x] Fork `logabell/moonlight-common-c` → `vibesoftwarecoder/moonlight-common-c`
- [x] Cherry-pick logabell's 6 `stream_mic` commits onto latest Apollo HEAD:
  - [x] `e556bb6` first working version  
        *(one trivial conflict: Boost URL variable name — kept our newer `BOOST_VERSION`)*
  - [x] `0affdaa` update moonlight-common-c mic submodule
  - [x] `9677160` Use Steam Streaming Microphone for Windows mic redirection
  - [x] `1f83e4f` Require encrypted microphone passthrough
  - [x] `45432ae` Improve microphone stream timing
  - [x] `9979406` Refine microphone troubleshooting UI
- [x] Fix submodule: `.gitmodules` updated to `vibesoftwarecoder/moonlight-common-c`;
      pinned to commit `7850932` (HEAD of `codex/mic-common-c` branch —
      "Use timed microphone packets with FEC")
- [x] Push `stream-mic-rebase` branch to GitHub

### 2.2 — Set up build environment and verify compile  ✅ DONE (2026-04-30)
Build uses **MSYS2 UCRT64 + GCC + Ninja** (not MSVC — `.a` libs and pkg-config are MinGW conventions).
CUDA is NOT required on Windows (DXGI capture, not NVFBC).

- [x] Install MSYS2 via winget (`C:\msys64`)
- [x] Install UCRT64 toolchain: gcc 15.2, cmake 4.3.2, ninja 1.13, MinHook, miniupnpc, openssl, curl-winssl, opus, onevpl, nsis, nodejs, nlohmann_json, cppwinrt
- [x] Extract pre-compiled FFmpeg binaries from `third-party/build-deps` (Git LFS + longpaths fix)
- [x] Fix Boost download 404: `BOOST_VERSION` variable was undefined after conflict resolution — added `set(BOOST_VERSION "1.89.0")` to Boost_Sunshine.cmake (commit `a6ada3e9`)
- [x] Fix compile error in `src/video.cpp:830`: `config_t::profile` removed in upstream Sunshine merge but AMD encoder code not updated — hardcoded to `"high"` matching NVENC behavior (commit `55d5b410`)
- [x] CMake configure: success (54s, Boost 1.89.0 fetched)
- [x] Ninja build: success — `build/sunshine.exe` produced, binary launches
- [x] Pushed both fix commits to `stream-mic-rebase` on GitHub

**Branch now has 9 commits ahead of master** (6 mic + 2 build fixes + 1 submodule fix).

### 2.3 — Pull in Sunshine upstream improvements  ✅ ALL PRESENT (2026-04-30)
ClassicOldSong/Apollo HEAD is already well beyond v0.4.6 and includes all targeted improvements.
No cherry-picks needed — confirmed by source search:

- [x] **Async NVENC** — `src/nvenc/nvenc_d3d11.cpp`: CreateEvent per encoder, nvEncRegisterAsyncEvent, doNotWait lock
- [x] **Frame timing** — `src/platform/windows/display_base.cpp:195`: full `frame_pacing_group` mechanism
      computes exact per-frame sleep intervals from `client_frame_rate_adjusted` (DXGI_RATIONAL from client FPS),
      with `sleep_overshoot_logger` to measure timing accuracy
- [x] **Color conversion matrix** — `src/video_colorspace.cpp`: full 4×4 matrix generator,
      8-bit and 10-bit, BT.601/709/2020, full/limited range
- [x] **Continuous audio streaming** — `stream_audio = true`, `auto_capture = true` defaults in config
- [x] **AV1 encoding** — NV_ENC_CODEC_AV1_GUID, full capability probing, RTSP advertisement, HDR mode 3

### 2.4 — Verify multi-instance still works  ✅ DONE (2026-05-01)
MultiSeat depends on per-seat `sunshine_state.json` UUID isolation.

**Bug found and fixed:** `platf::appdata()` on Windows returns `{exe_dir}/config/`, NOT
the working directory. Without an explicit `file_state` key, all seats shared
`C:\Program Files\Apollo\config\sunshine_state.json` → same UUID → Moonlight saw
all seats as one server. Fixed in `ApolloConfigBuilder.cs` (commit `10d7fb6`):
`file_state = <absolute per-seat path>` is now written into each sunshine.conf.

- [x] Root cause identified: appdata() is exe-path based, not working-dir based
- [x] Fix: write explicit `file_state = {seatDir}/config/sunshine_state.json` in config
- [x] **SudoVDA UUID detection fixed** — Apollo logs `friendly_name: ""` for the virtual display
      inside RDP sessions (no EDID, no SetupDi description available). Old parser required ≥1 char
      (`[^""]+`); fixed to accept empty string and fall back to 1000Hz refresh-rate heuristic.
      SudoVDA uniquely registers at 1000Hz; no real monitor uses that rate. (MultiSeat ApolloManager.cs)
- [x] **Junction cleanup fixed** — `Directory.Delete(recursive:true)` threw on `assets`/`tools`
      junctions ("The parameter is incorrect"). Fixed to delete junctions non-recursively first.
      (MultiSeat ApolloConfigBuilder.cs)
- [x] **always_use_virtual_display NOT a config key** in ClassicOldSong/Apollo HEAD —
      moved to per-paired-device state in sunshine_state.json. Removed from generated config
      (MultiSeat commit `38e6e29`). MultiSeat uses `output_name` instead.
- [x] Binary deployed to `C:\Program Files\Apollo\sunshine.exe` (55 MB, built 2026-04-30)
      Old logabell binary backed up as `sunshine.exe.bak`
- [x] Smoke test passed — binary launches, reads config, detects displays (SudoVDA VDD visible)
- [x] Live test: provision two seats, confirm distinct UUIDs in generated sunshine.conf
- [x] Live test: Moonlight sees each seat as a separate server with stream_mic working

### 2.5 — Tag release and update MultiSeat  ✅ DONE (2026-04-30)
- [x] Tag `v2026.4.30-multiseat.1` — initial build
- [x] Tag `v2026.4.30-multiseat.2` — version string + update check URL fix
- [x] **v3 ApolloVibe build done** (commit `0b71e8fa`, tag `v2026.4.30-multiseat.3`):
      - Rebranded: Apollo → **ApolloVibe** in all user-facing UI (browser title, navbar,
        config placeholder, locale strings across 22 language files, Windows version resource)
      - Issue tracker URL → `vibesoftwarecoder/Apollo`; copyright URL → our fork
      - PII clean: all 10 commits on `stream-mic-rebase` use `vibesoftwarecoder@users.noreply.github.com`
        for both author and committer
      - Release: https://github.com/vibesoftwarecoder/Apollo/releases/tag/v2026.4.30-multiseat.3
      - Binary deployed to `C:\Program Files\Apollo\sunshine.exe` (55 MB, 2026-04-30)
      - `install-prerequisites.ps1` updated to download v3 zip
- [x] MultiSeat service deployed (commit `2e418e0`):
      - SudoVDA UUID fix + junction cleanup + audio router + dashboard improvements
      - `scripts\install-service.ps1` run successfully — service Running
- [x] Merge `stream-mic-rebase` → `master` on `vibesoftwarecoder/Apollo` (PR #1, 2026-05-01)

**To deploy the new binary on the current machine** (replaces logabell Apollo):
```powershell
Expand-Archive "C:\path\to\apollo-v2026.4.30-multiseat.1-windows-x64.zip" `
  -DestinationPath "C:\Program Files\Apollo" -Force
```

---

## Phase 1 (folded into Phase 2) — ApolloConfigBuilder NVENC tuning  ✅ DONE (2026-04-30)

All written to each generated `sunshine.conf` via `ApolloConfigBuilder.cs` (commit `2dcdfe1`).

- [x] `nvenc_preset = 4` — exposed via `MultiSeatOptions.NvencPreset`, default P4 (balanced)
      Apollo default was 1 (P1). Raises quality meaningfully with no perceptible latency hit.
- [x] `nvenc_twopass = quarter_res` — already Apollo default, set explicitly for clarity
- [x] `nvenc_split_encode` — **NOT PRESENT** in this Apollo build. Key does not exist in config parser.
- [x] `nvenc_spatial_aq = enabled` — was false by default; allocates more bits to flat regions
- [x] `nvenc_vbv_increase = 20` — was 0; relaxes per-frame VBV buffer 20%, reduces fast-motion artifacts
- [x] `nvenc_latency_over_power = enabled` — already Apollo default true, set explicitly for clarity
- [x] AV1 — already handled: `av1_mode = 2` was already in the config (allow if client supports it)

---

## Phase 3 — Dashboard quality presets  ✅ DONE (2026-05-02)

- [x] Add encoding settings per seat in the dashboard SeatCard
- [x] Add quality preset concept: **Latency / Balanced / Quality** that set groups
      of NVENC options as a unit (`NvencQualityPreset` enum in `SeatRequest.cs`)
- [x] Store encoding preferences in `SeatPreset`
      (persisted to `C:\ProgramData\MultiSeat\seat-presets.json` via `SeatPresetStore`)
- [x] Wire preset selection through `SeatRequest` → `ApolloConfigBuilder`
      (commit `b6c0839`)

---

## Phase 4 — Frame generation / scaling  ✅ DONE (2026-05-01)

Host-side Lossless Scaling (LSFG) is **confirmed broken** with SudoVDA + Apollo:
DXGI capture does not pick up LSFG-generated frames. Labeled "Cannot Fix" in the
Virtual Display Driver repo. No host-side workaround exists.

Added **Streaming Tips** page to the dashboard (/tips):

- [x] Lossless Scaling — client-side, recommended, works with any GPU
- [x] DLSS Swapper + DLSS 3.8.1 — per-game FG (DLSS 310.1+ breaks streaming)
- [x] AMD AFMF — client-side, AMD GPUs, fullscreen mode
- [x] Network & latency tips (bitrate, wired, resolution/FPS/preset guidance)
- [x] Audio & microphone tips
- [x] HDR blocked notice with SudoVDA GitHub link

---

## Phase 5 — HDR  (blocked, revisit later)

Blocked at the **SudoVDA driver layer** — virtual displays do not expose an HDR-capable
output signal. No Apollo changes can fix this.

- [ ] Monitor SudoVDA GitHub for HDR virtual display support
- [ ] When SudoVDA adds HDR: wire Apollo HDR config options in `ApolloConfigBuilder`
- [ ] Re-evaluate Moonlight client HDR support at that time

---

## Phase 6 — Host-side frame generation  (blocked until RTX 40xx/50xx upgrade)

Host-side frame generation is not possible on the current RTX 3080 (Ampere). Revisit when upgrading GPU. Full research completed 2026-05-02.

### What was investigated
- **DLSS FG (DLSS 3, RTX 40xx) / DLSS 4 MFG (RTX 50xx):** DLSS 310.1+ injects frames via Independent Flip, bypassing DWM. DXGI capture misses them. Sunshine issue #3621 open, no upstream fix.
- **NvFBC:** Deprecated on Windows 10/11 since 2019, unavailable on consumer GPUs, and doesn't work inside RDP sessions with IddCx/SudoVDA virtual displays regardless. Dead end.
- **ForceComposedFlip** ([github.com/fernandoenzo/ForceComposedFlip](https://github.com/fernandoenzo/ForceComposedFlip)): Forces DWM Composed Flip mode via MPO registry key + persistent topmost window. Allows DXGI to capture FG frames. Standalone exe, zero dependencies, v1.2.2 (April 2026). Unknown behavior inside RDP sessions — untested with our setup.
- **Lossless Scaling (LSFG) host-side:** Confirmed broken with SudoVDA + DXGI (Apollo issue #535, "Cannot Fix"). Requires WGC capture or physical display.
- **Vibepollo** ([github.com/Nonary/Vibepollo](https://github.com/Nonary/Vibepollo)): Active Apollo fork with WGC IPC helper + LS launcher integration + NVIDIA Smooth Motion (RTX 40+). Uses `sunshine_wgc_capture.exe` helper that runs in user context and passes D3D11 shared textures back to the SYSTEM service via named pipe + keyed mutex. Also has per-seat LS process launcher (env var config injection).

### When upgrading to RTX 40xx/50xx — do in this order

1. **Port WGC IPC helper from Vibepollo into ApolloVibe**
   - ~20 files, ~300 KB C++, estimated 220 hours
   - Key files: `src/platform/windows/ipc/` (pipes, ipc_session, process_handler), `tools/sunshine_wgc_capture.cpp`, `src/platform/windows/display_wgc.cpp`
   - IPC protocol: named pipes for control, D3D11 shared texture handle for frames, keyed mutex sync, QPC timestamps
   - Fallback to DXGI on secure desktop / display mode change is built-in
   - Benefit: more reliable RDP session capture, foundation for LS integration

2. **Add ForceComposedFlip to MultiSeat**
   - Add `ForceComposedFlip.exe` to prerequisites
   - Register it to start with Windows (tray app, "Start with Windows" menu)
   - Alternatively: replicate its registry key (`HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode = 5`) in the MultiSeat install script and create a persistent topmost window inside each seat session

3. **Per-seat LS launcher in MultiSeat**
   - Launch Lossless Scaling inside each seat's RDP session (same pattern as Apollo process launch)
   - Pass settings via environment variables matching Vibepollo's `compute_lossless_runtime()` convention
   - LS must run in the same session as the game — cannot be launched from Session 0

4. **NVIDIA Smooth Motion (RTX 40xx, driver 571.86+)**
   - Controlled via NVIDIA DRS API (`SMOOTH_MOTION_ENABLE_ID`)
   - Vibepollo sets Ultra Low Latency to "Ultra" alongside it
   - Simpler than LS integration — evaluate as an alternative

---

## Research Notes

### vibesoftwarecoder/Apollo (our fork)
- Based on: ClassicOldSong/Apollo latest HEAD (ahead of v0.4.6)
- Branch `stream-mic-rebase`: 6 logabell mic commits cherry-picked + submodule fix
- moonlight-common-c: `vibesoftwarecoder/moonlight-common-c` at `7850932`
  (codex/mic-common-c HEAD — "Use timed microphone packets with FEC")

### logabell/Apollo fork (what we replaced)
- GitHub: https://github.com/logabell/Apollo
- Version: v2026.3.18-mic.1, version string `0.0.0.0affdaa.dirty`
- Mic feature: 6 commits on top of Apollo v0.4.6, PR #1428 open/unreviewed
- Client: https://github.com/logabell/moonlight-qt-mic
- Author: inactive (paternity leave as of April 2026)

### ClassicOldSong/Apollo (upstream base)
- GitHub: https://github.com/ClassicOldSong/Apollo
- Latest stable: v0.4.6 (July 2024), pre-release v0.4.7-alpha.1 (August 2024)
- Multi-instance: per-working-directory config isolation, unique UUID per seatDir

### LizardByte/Sunshine (upstream of Apollo)
- Latest stable: v2025.924.154138 (September 24, 2025)
- Key additions since Apollo v0.4.6: async NVENC, frame timing, color matrix fixes
- AV1 encoding: since v0.21.0 (October 2023)
- HDR: Linux-only via KMS capture (v0.22.0) — not relevant for Windows

### Lossless Scaling
- Host-side LSFG + SudoVDA + Apollo: **BROKEN** (Apollo issue #535, "Cannot Fix")
- Client-side LS: works perfectly, recommended approach
- DLSS FG 310.1+: breaks streaming (client gets base framerate only)
- DLSS 3.8.1 via DLSS Swapper: better, use for per-game FG
- AMD AFMF: works client-side in fullscreen mode
