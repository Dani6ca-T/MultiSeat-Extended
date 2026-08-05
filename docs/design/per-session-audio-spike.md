# Runbook: per-session audio spike (R1/R2)

Companion to [per-session-audio.md](per-session-audio.md). This is the go/no-go gate for the #10 / #12 fix.

**Time:** ~30 minutes · **Code changes:** none · **You must be at the physical machine** (you need to hear the speakers)

---

## What this answers

Today every seat is told *"play your audio on the host PC."* That's why an active seat silences the console (#12) and fights over the default device (#10).

The proposed fix tells each seat *"play your audio on the client"* instead — which makes Windows create a **private playback device inside that seat's session**, called "Remote Audio". Nothing touches the host's real speakers.

That plan only works if **Apollo can record from that private device.** On some Windows builds recording from it silently produces nothing. This runbook answers that.

---

## ⚠️ Read before starting

**This test opens a second Windows session**, which only works if RDPWrap is installed and current. RDPWrap breaks whenever a Windows update replaces `termsrv.dll` — and when it's broken, connecting **disconnects your console session instead of adding one**.

If you're connected remotely (RustDesk), that could drop your access. **Do this test sitting at the machine, not remotely.** Step 0.3 checks this before anything risky happens.

**Don't run this while a MultiSeat seat is live.** This test is completely independent of the MultiSeat service — it doesn't touch the service, its config, or any seat — but a live seat muddies the audio picture.

---

## Part 0 — Preparation

### 0.1 Confirm no seat is running

```powershell
Get-Process mstsc -ErrorAction SilentlyContinue
```

**Expected:** no output. If processes are listed, stop the seats from the dashboard first.

### 0.2 Install Audacity on the console

Install it once on your normal desktop; it'll then be available inside the test session too.

```powershell
winget install Audacity.Audacity
```

No winget? Download from <https://www.audacityteam.org/download/windows/>.

### 0.3 ⚠️ Verify RDPWrap is installed — DO THIS BEFORE CONNECTING

**This is the step that protects your console session.** Without RDPWrap, connecting in Part 1 will *disconnect* you instead of opening a second session. Checking afterwards is too late — the damage is already done.

```powershell
(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\TermService\Parameters' -Name ServiceDll).ServiceDll
Test-Path 'C:\Windows\System32\rdpwrap.dll'
```

**Required to continue:**
- `ServiceDll` must point at **`rdpwrap.dll`** — not `termsrv.dll`
- `Test-Path` must return **`True`**

> ❌ **If `ServiceDll` says `termsrv.dll`, or `Test-Path` is `False`, RDPWrap is not installed. STOP.**
> Connecting will kick you off your own desktop. Re-run `prerequisites\install-prerequisites.ps1`,
> reboot, and re-check before going any further.

You can also confirm via the service's own view, which checks the same thing at every startup:

```powershell
.\scripts\show-logs.ps1 -Hours 168
```

An `RDP Wrapper multi-session patch not detected` error there means the same thing: stop.

### 0.4 Record your session state

```powershell
qwinsta
```

Write down what you see. Your console session should show as `Active`. You'll re-run this in Part 1 to confirm you **gained** a session rather than **replaced** one.

### 0.5 Create a throwaway test account

Using a real seat account works too if you know its password, but a scratch account keeps seat state untouched. Cleanup is Part 6.

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

### 0.7 Note your console's default playback device

Open `mmsys.cpl` → **Playback** tab. Note which device has the green check. Leave this window open — you'll use its **level meters** (the green bars) as your measuring instrument throughout.

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

> ❌ **If your console session got disconnected**, RDPWrap is broken. Stop here — re-run `prerequisites\install-prerequisites.ps1` to refresh `rdpwrap.ini`, then start over.

**Leave this RDP session open for every test below.**

---

## Part 2 — TEST 1: does the private audio device exist?

**Inside the RDP session**, run:

```powershell
mmsys.cpl
```

Look at the **Playback** tab.

- ✅ **PASS** — a device named **"Remote Audio"** is listed, and has the green default check.
- ❌ **FAIL** — no such device. Stop; the design's foundation is missing. Screenshot what you *do* see.

**Record:** Test 1 = PASS / FAIL

---

## Part 3 — TEST 2: does the host keep its audio?

This is the whole point of the change, and the cheapest test here.

### 3.1 Play something on the console

On your **normal desktop** (not the RDP session), play music or a video.

### 3.2 Listen, and watch the meter

- Listen to your speakers.
- In the console's `mmsys.cpl` window, watch the green level bar next to your physical device.

- ✅ **PASS** — you hear it, and the meter moves. **This is #12 fixed.** Under today's `audiomode:i:1` the console would be silent.
- ❌ **FAIL** — still silent → `audiomode` isn't the cause, and our root-cause analysis on #10/#12 is wrong. That's significant; capture everything.

**Record:** Test 2 = PASS / FAIL

---

## Part 4 — TEST 3: can it be recorded? ⭐ THE GATE

Everything depends on this one.

### 4.1 Start continuous audio inside the session

**Inside the RDP session**, run this and leave the window open:

```powershell
$player = New-Object System.Media.SoundPlayer "C:\Windows\Media\Alarm01.wav"
$player.PlayLooping()
```

The sound plays to the session's default device — "Remote Audio".

### 4.2 Try to record it

**Inside the RDP session**, open Audacity and set:

- **Audio Host:** `Windows WASAPI`
- **Recording Device:** `Remote Audio (loopback)`

Press **Record** for about 10 seconds, then Stop.

> If no `(loopback)` entry appears in the device list at all, that itself is a FAIL — note it.

### 4.3 Read the result

Look at the waveform, and play it back.

- ✅ **PASS** — visible waveform, audible on playback → **loopback works. The design is viable. Build it.**
- ❌ **FAIL** — flat line / silence → **R1 fails, the gate closes.** Note the exact device list and any error.

**Record:** Test 3 = PASS / FAIL

### 4.4 Confirm with Apollo (only if 4.3 passed)

Audacity passing is a strong signal, but Apollo is the real consumer. To be certain, run a scratch ApolloVibe instance **inside the RDP session** with a throwaway config containing:

```
audio_sink = Remote Audio
```

Connect Moonlight from a phone or another PC and confirm you hear the session's audio.

If Audacity passed but Apollo doesn't, the problem is in Apollo's capture path specifically — important to know before designing around it.

**Record:** Test 3b = PASS / FAIL / skipped

---

## Part 5 — TEST 4: does seat audio leak to the host?

Under `audiomode:i:0` the session's audio is routed to the `mstsc` window, which lives on the console. **We expect it to come out of the host's speakers** — that's the risk the real implementation has to engineer away.

### 5.1 Confirm the leak is real

With the alarm still looping inside the session, listen to the **host's speakers**.

**Expected:** you hear the session's audio. This confirms the risk is real (not a surprise — it's why `MuteMstscAudio` exists).

### 5.2 Mute it

On the **console**: right-click the speaker icon → **Volume Mixer** → find the `mstsc.exe` entry → mute it.

Listen again — the host should go quiet.

### 5.3 The step that actually matters

**Go back to Audacity inside the RDP session and record again — while `mstsc` is still muted.**

- ✅ **PASS** — host is silent **and** the recording still captures audio → muting is a safe mechanism; `MuteMstscAudio` just needs to be made reliable.
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

4. Confirm your console's default playback device is unchanged (`mmsys.cpl`).

Nothing else was modified — the MultiSeat service, its config, and all seats were untouched throughout.

---

## Results sheet

```
Date:
Windows build:            (winver)

Test 1  Remote Audio device exists      PASS / FAIL
Test 2  Console keeps its audio         PASS / FAIL
Test 3  Loopback recording works  ⭐    PASS / FAIL
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

**No sound from `PlayLooping()`** — confirm "Remote Audio" is the session default in `mmsys.cpl` inside the session; set it as default and retry.

**Session logs straight back out** — the account needs to be in **Remote Desktop Users** (Step 0.5).

## Worth posting either way

Test 2's result is worth posting to #12 whichever way it goes — it either confirms the fix direction to two waiting reporters, or saves everyone a wrong turn. Both have been patient and are actively testing.
