# Runbook: per-session audio spike (R1/R2)

Companion to [per-session-audio.md](per-session-audio.md). This is the go/no-go gate for the #10 / #12 fix.

**Time:** ~20 minutes · **Code changes:** none (uses the `--audio-peaks` helper already in the service)

---

## What this answers

Today every seat is told *"play your audio on the host PC."* That's why an active seat silences the console (#12) and fights over the default device (#10).

The proposed fix tells each seat *"play your audio on the client"* instead — which makes Windows create a **private playback device inside that seat's session**, called "Remote Audio". Nothing touches the host's real speakers.

That plan only works if **Apollo can record from that private device.** On some Windows builds recording from it silently produces nothing. This runbook answers that.

---

## How this is measured — read this first

**The host is headless and nobody is ever physically at it.** Every readout here is therefore a number, not a sound. Do not substitute listening: the machine is reached over RustDesk, which *forwards host audio to the operator*, so "can I hear it?" measures RustDesk's re-routed stream rather than the endpoint under test. When the thing being diagnosed is audio routing, that confound is fatal.

The instrument is:

```powershell
MultiSeat.Service.exe --audio-peaks [seconds]     # default 5
```

It polls every active render endpoint — and every application session on it — and reports the peak each reached.

Three properties of it that change how you read the output:

1. **Run it inside the session you are measuring.** `IAudioSessionManager2::GetSessionEnumerator` is session-scoped, so it only sees the Windows session it runs in. Console session for host audio; the RDP session for that session's audio.
2. **⚠️ Read the per-`APP` lines, not the endpoint line.** On virtual devices (VB-CABLE, VoiceMeeter) the endpoint meter does not reflect the session mix. This is measured, not theoretical:
   ```
   silent peak=0.000031  CABLE In 16ch (VB-Audio Virtual Cable) [DEFAULT]
            APP | Playnite.DesktopApp (pid 4188) peak=0.327480 AUDIO
   ```
   The device reads its noise floor while an app on it is plainly loud. **Never conclude "this device has no audio" from the endpoint number.**
3. **Peaks prove audio is *flowing to* an endpoint. They do not prove it can be *captured from* it.** Those are different claims, and the gate (Test 3) is specifically about capture. Don't let a healthy peak talk you into passing Test 3.

The sound source must be a **real application started interactively**. `System.Media.SoundPlayer` invoked from a non-interactive/automation context renders nothing and reads 0 on every device — that has produced two wrong conclusions on this project already.

---

## ⚠️ Read before starting

**This test opens a second Windows session**, which only works if RDPWrap is installed and current. RDPWrap breaks whenever a Windows update replaces `termsrv.dll` — and when it's broken, connecting **disconnects your console session instead of adding one**. On a headless box reached over RustDesk, that costs you access until you can recover it.

Step 0.3 checks this before anything risky happens. Don't skip it.

**Don't run this while a MultiSeat seat is live.** This test is completely independent of the MultiSeat service — it doesn't touch the service, its config, or any seat — but a live seat muddies the audio picture.

---

## Part 0 — Preparation

### 0.1 Confirm no seat is running

```powershell
Get-Process mstsc -ErrorAction SilentlyContinue
qwinsta
```

**Expected:** no `mstsc`, and no seat sessions in `qwinsta`. If a seat is up, tear it down from the dashboard first.

### 0.2 Install Audacity on the console

Needed for Test 3 only. Installing it on the console makes it available inside the test session too.

```powershell
winget install Audacity.Audacity
```

No winget? Download from <https://www.audacityteam.org/download/windows/>.

### 0.3 ⚠️ Verify RDPWrap is installed — DO THIS BEFORE CONNECTING

**This is the step that protects your access.** Without RDPWrap, connecting in Part 1 will *disconnect* you instead of opening a second session. Checking afterwards is too late.

```powershell
$dll = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\TermService\Parameters' -Name ServiceDll).ServiceDll
"ServiceDll : $dll"
"dll exists : $(Test-Path $dll)"
"ini exists : $(Test-Path ([IO.Path]::ChangeExtension($dll,'ini')))"
```

**Required to continue:**
- `ServiceDll` must end in **`rdpwrap.dll`** — not `termsrv.dll`
- both `Test-Path` lines must be **`True`**

> **Do not hardcode `C:\Windows\System32\rdpwrap.dll`.** RDPWrap commonly installs to
> `C:\Program Files\RDP Wrapper\`, and an earlier version of this runbook tested the
> System32 path — which returns `False` on a perfectly healthy install and told the
> operator to STOP. Always resolve the path from `ServiceDll`, as above.

> ❌ **If `ServiceDll` says `termsrv.dll`, or either file is missing, RDPWrap is not active. STOP.**
> Re-run `prerequisites\install-prerequisites.ps1`, reboot, and re-check.

The strongest evidence is empirical: if `qwinsta` already shows **two `Active` sessions** (your console plus a seat), multi-session is provably working right now.

### 0.4 Record your session state

```powershell
qwinsta
```

Your console session should show as `Active`. You'll re-run this in Part 1 to confirm you **gained** a session rather than **replaced** one.

### 0.5 Create a throwaway test account

A scratch account keeps seat state untouched. Cleanup is Part 6.

```powershell
$pw = Read-Host -AsSecureString "Password for test account"
New-LocalUser -Name "audiotest" -Password $pw -AccountNeverExpires
Add-LocalGroupMember -Group "Remote Desktop Users" -Member "audiotest"
```

### 0.6 Create the test connection file

This is MultiSeat's real `Default.rdp` with **one line changed** — `audiomode:i:1` → `audiomode:i:0` — and the CPU-saving lines dropped so you can see the desktop.

```powershell
@"
full address:s:127.0.0.2
authentication level:i:0
prompt for credentials:i:0
audiomode:i:0
"@ | Set-Content -Path "C:\ProgramData\MultiSeat\spike-test.rdp" -Encoding ASCII
```

> Editing `C:\ProgramData\MultiSeat\Default.rdp` by hand does nothing — `EnsureDefaultRdp` rewrites it on every seat launch. That's why this test uses its own file.

### 0.7 Baseline the console's audio

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 5
```

Note which endpoint is marked `[DEFAULT]`. Save this output — it's the "before" half of Test 2.

---

## Part 1 — Open the test session

### 1.1 Connect

```powershell
mstsc "C:\ProgramData\MultiSeat\spike-test.rdp"
```

Log in as `audiotest`. Accept any certificate warning.

### 1.2 Verify you gained a session

**Back on the console**, run:

```powershell
qwinsta
```

**Expected:** your original console session still `Active`, **plus** a new `audiotest` session.

> ❌ **If your console session got disconnected**, RDPWrap is broken. Stop — re-run `prerequisites\install-prerequisites.ps1` to refresh `rdpwrap.ini`, then start over.

**Leave this RDP session open for every test below.**

---

## Part 2 — TEST 1: does the private audio device exist?

**Inside the RDP session:**

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 3
```

- ✅ **PASS** — an endpoint named **"Remote Audio"** appears in the list, marked `[DEFAULT]`.
- ❌ **FAIL** — no such endpoint. Stop; the design's foundation is missing. Save the full output.

The header line also confirms you're measuring the right place — it prints the Windows session id, which should be the RDP session's, not 1.

**Record:** Test 1 = PASS / FAIL

---

## Part 3 — TEST 2: does the host keep its audio?

This is the whole point of the change, and the cheapest test here.

### 3.1 Play something on the console

On the **console session**, start real audio — a browser video, a music player. Leave it playing.

### 3.2 Measure, on the console

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 8
```

Find the **`APP |`** line for the player you started.

- ✅ **PASS** — that app shows a clearly non-zero peak (order 0.01–1.0) while the RDP session is open. **This is #12 fixed.** Under today's `audiomode:i:1` the console would be silent.
- ❌ **FAIL** — the app's peak stays at the noise floor (≈0.00003 or 0.000000) → `audiomode` isn't the cause, and our root-cause analysis on #10/#12 is wrong. That's significant; save everything.

> Judge this on the app line. The endpoint line can read its noise floor even while the app on it is loud (see "How this is measured").

**Record:** Test 2 = PASS / FAIL, and the peak value.

---

## Part 4 — TEST 3: can it be recorded? ⭐ THE GATE

Everything depends on this one. **This is the only step that still needs a human**, because it tests *capture*, which peak metering cannot answer.

### 4.1 Start continuous audio inside the session

**Inside the RDP session**, run this and leave the window open:

```powershell
$player = New-Object System.Media.SoundPlayer "C:\Windows\Media\Alarm01.wav"
$player.PlayLooping()
```

This works here because you are running it interactively in a real session.

### 4.2 Confirm the sound is actually flowing before trying to record it

Still **inside the RDP session**, in a second window:

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 5
```

You should see a non-zero `APP |` peak for `powershell` on "Remote Audio". If you don't, the source isn't playing — fix that before blaming capture, or you'll record silence and wrongly fail the gate.

### 4.3 Try to record it

**Inside the RDP session**, open Audacity and set:

- **Audio Host:** `Windows WASAPI`
- **Recording Device:** `Remote Audio (loopback)`

Press **Record** for about 10 seconds, then Stop.

> If no `(loopback)` entry appears in the device list at all, that itself is a FAIL — note it.

### 4.4 Read the result objectively

Don't judge by listening. **File → Export → Export as WAV**, then measure it:

```powershell
# Reports peak amplitude of a 16-bit PCM WAV. >0.01 means real audio was captured.
$bytes = [IO.File]::ReadAllBytes("C:\path\to\export.wav")
$max = 0
for ($i = 44; $i -lt $bytes.Length - 1; $i += 2) {
    $s = [BitConverter]::ToInt16($bytes, $i)
    $a = [Math]::Abs([int]$s) / 32768.0
    if ($a -gt $max) { $max = $a }
}
"peak amplitude: $max"
```

- ✅ **PASS** — peak well above 0.01 → **loopback works. The design is viable. Build it.**
- ❌ **FAIL** — peak ~0 (flat) → **R1 fails, the gate closes.** Save the exact device list and any error.

**Record:** Test 3 = PASS / FAIL, and the peak amplitude.

### 4.5 Confirm with Apollo (only if 4.4 passed)

Audacity passing is a strong signal, but Apollo is the real consumer. Run a scratch ApolloVibe instance **inside the RDP session** with a throwaway config containing:

```
audio_sink = Remote Audio
```

Connect Moonlight from a phone or another PC and confirm you get the session's audio.

If Audacity passed but Apollo doesn't, the problem is in Apollo's capture path specifically — important to know before designing around it.

**Record:** Test 3b = PASS / FAIL / skipped

---

## Part 5 — TEST 4: does seat audio leak to the host?

Under `audiomode:i:0` the session's audio is routed to the `mstsc` window, which lives on the console. **We expect it to come out on the host** — that's the risk the real implementation has to engineer away.

### 5.1 Confirm the leak is real

With the alarm still looping inside the session, measure **on the console**:

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 6
```

**Expected:** an `APP | mstsc` line with a non-zero peak. That is the leak, measured. (Not a surprise — it's why `MuteMstscAudio` exists.)

### 5.2 Mute it

On the **console**, mute mstsc's audio session by PID:

```powershell
$pid = (Get-Process mstsc).Id
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --mute-audio $pid
```

Re-measure on the console — the `mstsc` peak should fall to the noise floor.

### 5.3 The step that actually matters

**Go back to Audacity inside the RDP session and record again — while `mstsc` is still muted.** Export and measure as in 4.4.

- ✅ **PASS** — host `mstsc` peak is at the noise floor **and** the new recording still has real amplitude → muting is a safe mechanism; `MuteMstscAudio` just needs to be made reliable.
- ❌ **FAIL** — muting also killed the recording → mute isn't viable and we need another way to stop host leakage. Design change required.

**Record:** Test 4 = PASS / FAIL

---

## Part 6 — Cleanup

1. Close Audacity and the looping PowerShell window.
2. Log off the RDP session (Start → user icon → Sign out). Don't just close the window.
3. Remove the test account and file:

```powershell
Remove-LocalUser -Name "audiotest"
Remove-Item "C:\ProgramData\MultiSeat\spike-test.rdp"
```

4. Confirm the console's default endpoint is unchanged:

```powershell
& "C:\Program Files\MultiSeat\MultiSeat.Service.exe" --audio-peaks 3
```

Compare the `[DEFAULT]` marker against your 0.7 baseline. Nothing else was modified — the MultiSeat service, its config, and all seats were untouched throughout.

---

## Results sheet

```
Date:
Windows build:            (winver)

Test 1  Remote Audio endpoint exists    PASS / FAIL
Test 2  Console keeps its audio         PASS / FAIL   peak:
Test 3  Loopback recording works  ⭐    PASS / FAIL   peak:
Test 3b Apollo captures it              PASS / FAIL / skipped
Test 4  Mute works, capture survives    PASS / FAIL

Notes / anything unexpected:
```

## What each outcome means

| Test 1 | Test 2 | Test 3 | Test 4 | Verdict |
|:---:|:---:|:---:|:---:|---|
| ✅ | ✅ | ✅ | ✅ | **Green light.** Build behind the `AudioMode` flag. |
| ✅ | ✅ | ✅ | ❌ | Build it, but solve host leakage another way first. |
| ✅ | ✅ | ❌ | — | **Gate closes.** Fall back to a per-session virtual audio driver or per-app routing — both worse. Reassess before spending effort. |
| ✅ | ❌ | — | — | Root-cause analysis on #10/#12 is wrong. Stop and re-diagnose. |
| ❌ | — | — | — | Foundation missing. Re-diagnose. |

## Troubleshooting

**Console session disconnected when connecting** — RDPWrap is broken. Re-run `prerequisites\install-prerequisites.ps1`.

**Certificate / "can't verify identity" warning** — expected for loopback RDP; accept it.

**No `(loopback)` devices in Audacity** — confirm Audio Host is `Windows WASAPI`, not MME or DirectSound. If it's set correctly and they're still absent, that's a genuine Test 3 FAIL.

**`--audio-peaks` shows everything at 0.000031** — that's the VB-CABLE/VoiceMeeter noise floor, i.e. silence. Confirm your sound source is actually playing.

**`--audio-peaks` shows no app sessions at all** — you're running it in the wrong Windows session. Check the session id in its header line.

**No sound from `PlayLooping()`** — confirm "Remote Audio" is the session default inside the session, and that 4.2 shows a non-zero peak for `powershell`.

**Session logs straight back out** — the account needs to be in **Remote Desktop Users** (Step 0.5).

## Worth posting either way

Test 2's result is worth posting to #12 whichever way it goes — it either confirms the fix direction to two waiting reporters, or saves everyone a wrong turn. Both have been patient and are actively testing.

## Possible follow-up: make Test 3 scriptable too

Test 3 is the only step still needing a human, because it tests capture rather than flow. A `--capture-loopback <device> <seconds> <out.wav>` verb (`IAudioClient` with `AUDCLNT_STREAMFLAGS_LOOPBACK` + `IAudioCaptureClient`) would close that gap and make the entire spike runnable unattended — worth building if this spike needs re-running on other hosts, or if external reporters are asked to run it.
