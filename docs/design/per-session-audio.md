# Design: Per-session audio isolation

Status: **Implemented behind `AudioMode = PerSession`; default remains `SharedHost`** · Fixes: #10, #12 · Related: #11 (display-side twin)

> **R1 is answered — twice.** Our own spike (2026-08-07) measured a clean WASAPI loopback capture of a
> seat session's Remote Audio endpoint (`packets=600, silent=0`, peak 0.356) and confirmed that muting
> the console-side `mstsc` stops host leakage without killing capture. Independently, **jmlopezdona
> ([issue #15](https://github.com/vibesoftwarecoder/MultiSeat/issues/15)) has run this design in
> production since 2026-08-10** with the virtual cables uninstalled, several seats with simultaneous
> audio and the console unaffected. Two of his findings corrected this document — see
> "Corrections from the field" below. The implementation here is ours; the sharp edges are his.

## Problem

Every seat's RDP session is created with `audiomode:i:1` ("play audio on the **host** computer"; `SessionLauncher.EnsureDefaultRdp`). Seats therefore render onto the host's **shared** audio subsystem and use host-side virtual audio devices (VB-CABLE for seat 0, VoiceMeeter channels for seats 1–3). Consequences, confirmed by two reporters' logs:

- **#10** — MultiSeat forcing a seat's VAC as the machine-wide default output hijacked the console's default. (Mitigated: MultiSeat no longer sets the render default, and Apollo uses `virtual_sink` + `keep_sink_default = disabled`.)
- **#12** — but that only *shifted* the symptom. With `audiomode:i:1`, an active seat's RDP session renders onto the host's physical device **and Windows suspends the console session's own playback** while that seat is active. Reporter's logs proved: host apps silent on a second unrelated device too (whole console session suspended), and the seat's audio leaks onto the console's physical output.

Root cause: **seats share the host's single audio subsystem.** No amount of default-device juggling fixes it, because there is one global default and one shared physical endpoint. This is the audio twin of #11 (SudoVDA is a global IddCx display, not RDP-session-scoped).

## Goal / non-goals

**Goal:** each seat's game audio is captured for Moonlight from an endpoint that lives **inside that seat's RDP session**, so the host's physical audio and the console session are never touched, with no shared virtual cables.

**Non-goals (this pass):** microphone path (stays on Steam Streaming Microphone; Moonlight→game, unaffected); surround sound (see R3); the #11 display re-architecture (tracked separately).

## Target architecture — RDP per-session audio

RDP audio redirection gives every session its **own** "Remote Audio" (Microsoft Remote Audio) render endpoint. That is exactly the per-session isolation we need. DuoStream uses this same MS remote audio driver.

Flow per seat:

1. Seat RDP session uses **`audiomode:i:0`** ("play on this computer" = the client). Windows creates a per-session **"Remote Audio"** render endpoint inside the seat session and makes it the session default; seat games play to it automatically.
2. **Apollo, running inside the seat session, WASAPI-loopback-captures the Remote Audio endpoint** and streams it to Moonlight. Nothing renders on the host's physical devices.
3. The redirected audio is also sent to the `mstsc` client, which lives in the **console** session (hidden, holds the seat Active). We **mute that `mstsc` process's audio session** so seat audio never plays on the host. The mute path already exists (`SessionLauncher.MuteMstscAudio` → `--mute-audio <pid>` → `AudioMuteHelper.MuteByPid`); today it's a no-op safety net under `audiomode:i:1`, and it becomes load-bearing here.

Because the Remote Audio endpoint is unique to each session, setting/keeping it as that session's default is inherently session-scoped — it cannot collide with the console or other seats the way a shared VAC did.

## Why this fixes #10 and #12

- Host physical device is never a render target for any seat → console is never suspended (#12).
- No machine-wide default is ever changed to a shared device → no hijack (#10).
- Each seat has its own endpoint → seats can't fight each other.

## Key risks / spikes (validate BEFORE building)

| ID | Risk | Spike |
|----|------|-------|
| **R1** (gating) | Can Apollo **WASAPI-loopback-capture** the MS Remote Audio endpoint? Historically loopback on the RDP audio device has been unreliable/silent on some Windows builds. If this fails, the whole approach needs a fallback. | In a live seat session with `audiomode:i:0`, run a WASAPI loopback capture on the Remote Audio endpoint and confirm non-silent PCM. Can use Apollo itself pointed at that sink, or a tiny loopback test. |
| **R2** | Does RDP audio redirection work under **RDPWrap** multi-session on Win11 26100+? Does the Remote Audio endpoint appear in the loopback seat session? | Connect a seat with `audiomode:i:0`, enumerate render endpoints in-session, confirm "Remote Audio" present + default. |
| **R3** | MS Remote Audio driver is **stereo-only** (per DuoStream). Surround game audio downmixes to 2.0. | Accept + document; matches current Opus 2.0 streaming anyway. |
| **R4** | `mstsc` audio-session **mute timing** — the session may be created after we mute. Seat audio could briefly leak to the host. | Mute on connect + re-mute on the connect health tick; verify no console leakage. |
| **R5** | Added latency from the RDP audio hop + loopback. | Measure end-to-end; expected acceptable for game streaming. |

R1 is the gate. If loopback on Remote Audio fails, fallback options: (a) a per-session virtual audio driver, (b) keep the shared-VAC path for game audio but solve host coexistence differently. Both are worse; R1 passing is what makes this design win.

## Code changes (all behind a feature flag)

Add `MultiSeatOptions.AudioMode = { SharedHost (current default) | PerSession }` so the two paths coexist and we can flip the default once proven.

- ✅ **`SessionLauncher.EnsureDefaultRdp`** — `audiomode:i:1` → `audiomode:i:0` when `PerSession`. The mode is logged alongside the write so a pasted log says which path a reporter is on.
- ✅ **`SessionLauncher.MuteMstscAudio`** — load-bearing under `PerSession`: `--mute-audio <pid> [timeoutMs]` now polls (a process has no audio session until it renders), runs fire-and-forget so it never blocks provisioning, and logs a leak-specific warning on failure.
- ✅ **`ApolloConfigBuilder`** — `PerSession`: **name no sink at all** (see corrections) and write `stream_mic = disabled`. `keep_sink_default`/`auto_capture_sink` stay `disabled` in both modes.
- ❌ **New in-session helper to resolve the endpoint ID** — **not needed, and would have been harmful.** Apollo takes the session default, which is already the right endpoint.
- ✅ **`AudioRouter`** — `EnsureVacScanned` returns immediately under `PerSession`, so no VoiceMeeter start and no "missing devices" warnings on a host that deliberately has none.
- ✅ **`SeatManager`** — step 5 skips `AssignCable` (which *throws* when no cables exist — the expected state here); `ResetAudio` is a logged no-op; `SeatServices.AudioManaged` tells the dashboard to show "Session" instead of a down light.
- ✅ **Tests** — config generation under both modes, including a guard that a stale `AudioGameRenderFriendlyName` never leaks into a `PerSession` config.
- ⬜ **Prereqs (`install-prerequisites.ps1`)** — VB-CABLE / VoiceMeeter are **not required** under `PerSession`; `CLAUDE.md` documents this, the installer does not yet branch on it.

## Corrections from the field (issue #15)

Two things this document had wrong, each of which cost jmlopezdona a night:

1. **Do not name the endpoint.** The line above used to say "point Apollo at the Remote Audio endpoint (named `audio_sink`/`virtual_sink`)". Both namings fail: `audio_sink` makes Apollo **re-role** the endpoint, and `virtual_sink` makes Apollo **rewrite its wave format**, which breaks it for every loopback client *including Apollo itself*. Leaving both keys unset is not a shortcut — it is the only thing that works.
2. **Client-side "Play audio on host PC" must be ON** under `PerSession` — the opposite of `SharedHost`, and safe because the "host" of a redirected session *is* the seat's own session.

Also: the endpoint's friendly name is **localized** by Windows ("Audio remoto" on a Spanish install), which is a second, independent reason never to resolve it by name.

## The cost: no microphone

A session that keeps its own audio cannot see the host's Steam Streaming Microphone, and there is no in-session equivalent to render into — so `stream_mic` is written `disabled` under `PerSession`. Game audio out works; the Moonlight → game mic path does not. This is why `SharedHost` remains the default rather than being deleted: installs that rely on mic must be able to keep it.

## What this removes / simplifies (the upside beyond the bug fixes)

- **No VB-CABLE, no VoiceMeeter Potato** for game audio → removes the most painful prereqs (VoiceMeeter needs a reboot, exclusive-grab quirks, the P/Invoke B1 routing config).
- **No 4-seat audio ceiling** — that limit was "1 host VAC device per seat." Each session gets its own Remote Audio endpoint, so audio no longer caps seat count.
- **No global-default juggling** — deletes a class of fragile `--set-default-render`/`keep_sink_default` logic.

## Rollout

1. ✅ **Spike R1 + R2** — passed 2026-08-07 (ours) and corroborated in production by #15.
2. ✅ **Implement behind `AudioMode = PerSession`** (default stays `SharedHost`).
3. ⬜ **Dogfood on the box** — set `"AudioMode": "PerSession"`, provision a seat, and validate the #10 + #12 scenarios. All three readouts are objective, so this can be validated on a headless host with no one at the keyboard:
   - console keeps audio while a seat streams → `--audio-peaks` in the **console** session: host app peaks stay non-zero;
   - seat audio does not leak to the host → same run, the **mstsc** APP line reads `0.000000` (this is what proves `MuteMstscAudio` landed);
   - the seat has audio → `--audio-peaks` **inside the seat session**: the game's APP line is non-zero on `Remote Audio`.
4. ⬜ Flip default to `PerSession`; mark VAC/VoiceMeeter optional in prereqs.
5. ⬜ Later: remove the shared-host path — **only if** the mic path is replaced first, since that is what `SharedHost` still buys.

## The mute is a watcher, not a poll — and here is why

The first implementation bounded the mute at 120 s after `mstsc` launched. **Measured on 2026-08-14, that failed outright:** with a seat idle for several minutes and then playing audio, the console read

```
CABLE In 16ch (VB-Audio Virtual Cable) [DEFAULT]
     APP | mstsc (pid 12296) peak=0.356986 AUDIO
```

i.e. the seat's audio was coming out of the host's speakers. `mstsc` creates **no audio session at all** until the seat first renders something — it is not created when the RDP audio channel is negotiated at connect — so the bounded poll had long expired and had nothing to mute in the meantime.

`--mute-audio <pid> -1` therefore **watches for the life of the `mstsc` process**: polls every 1 s until a session appears, mutes it, then re-asserts every 10 s (so a session torn down and recreated cannot come back audible) and exits when `mstsc` does. One lightweight process per seat, which is the same lifetime as the `mstsc` it guards.

This is the one part of `PerSession` that is genuinely load-bearing and invisible when it breaks, so it is worth re-measuring after any change to seat launch.
