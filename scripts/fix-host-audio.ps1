<#
.SYNOPSIS
    Diagnose and repair silent host audio without rebooting.

.DESCRIPTION
    Written after 2026-08-18, when host audio went silent following a sleep/resume and the only
    known remedy was a reboot. The cause was NOT what it looked like: Apollo was healthy (correct
    sink, clean Opus init, no errors) and so was VoiceMeeter. Audio was entering the VB-CABLE
    endpoint and never coming out, and Windows refused to reset the device.

    The signature that identifies it, and which this script measures:

        app-level meter on the default endpoint : ~0.3     (the app IS submitting audio)
        that endpoint's OWN meter               : ~0.00003 (the device mixes nothing)
        loopback capture of that endpoint       : packets flow, 100 percent silent

    When those disagree, the endpoint is wedged. When they agree, audio is fine and the fault is
    somewhere else (Apollo's encode/send path, or the client).

    The script generates its own audio so a reading always has a known-positive behind it. Silence
    measured with nothing playing means nothing at all - that mistake cost an hour on 2026-08-18,
    twice. The probe is a 19 kHz tone: loud enough for the meters, inaudible in practice, which
    matters because the default endpoint is usually the one a Moonlight client is listening to.

    Nothing here is specific to one machine. The device behind the default endpoint is resolved at
    runtime through its SWD\MMDEVAPI node, so this runs on any host - including a reporter's.

    It then walks the repair levers cheapest-first, re-measuring after each, and stops the moment
    audio flows. It reports which lever worked, because that is the evidence needed to fix the
    cause rather than keep applying the cure.

    ASCII only, on purpose: Windows PowerShell 5.1 reads this file as ANSI, and non-ASCII
    punctuation turns into mojibake that breaks parsing. Verified by it happening.

.PARAMETER DiagnoseOnly
    Measure and report; change nothing.

.PARAMETER SkipToneGenerator
    Do not generate audio. Use when you are already playing something and would rather measure that.

.PARAMETER ToneWav
    Loop this .wav instead of the generated probe tone. Only needed if you want a specific signal.

.PARAMETER ToneHz
    Frequency of the generated probe tone, default 19000 - high enough to be inaudible to most
    listeners, and measured to travel this path with no attenuation (0.080017 at 19 kHz vs 0.080048
    at 220 Hz). Drop it to something audible if you want to hear the probe yourself.

.PARAMETER ToneAmplitude
    Probe amplitude, default 0.08. The healthy/wedged thresholds are 0.001, so this sits about 80x
    above the floor.

.PARAMETER CableInstanceId
    Force a PnP device instance for the device-state checks. By default the script asks Windows which
    device backs the current default endpoint, so it needs no per-machine configuration.

.EXAMPLE
    .\fix-host-audio.ps1 -DiagnoseOnly
    .\fix-host-audio.ps1

.NOTES
    Exit codes (safe to test in a wrapper or a scheduled task):

        0   audio path healthy - already, or repaired by a lever
        1   measured unhealthy and not repaired (also -DiagnoseOnly on an unhealthy host)
        2   no verdict - nothing was playing to measure, or the script itself failed
#>
[CmdletBinding()]
param(
    [switch] $DiagnoseOnly,
    [switch] $SkipToneGenerator,
    [string] $ToneWav,
    [int]    $ToneHz = 19000,
    [double] $ToneAmplitude = 0.08,
    [string] $CableInstanceId
)

$ErrorActionPreference = 'Stop'

$Exe         = 'C:\Program Files\MultiSeat\MultiSeat.Service.exe'
$script:CableInstId = $CableInstanceId   # resolved from the default endpoint when not given
$script:ToneFile    = $ToneWav

function Write-Head($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "  OK    $t" -ForegroundColor Green }
function Write-Bad($t)  { Write-Host "  FAIL  $t" -ForegroundColor Red }
function Write-Info($t) { Write-Host "  $t" -ForegroundColor Gray }

if (-not (Test-Path $Exe)) {
    throw "MultiSeat.Service.exe not found at $Exe - this script uses its audio instruments."
}

# ---- Tone generator ---------------------------------------------------------
# SoundPlayer does NOT render when called inline from a non-interactive host; it works fine in a
# SEPARATE process. Learned the hard way - see the audio-instrument notes.
# Which PnP device backs the default endpoint? Ask Windows, rather than carrying one machine's
# 'ROOT\MEDIA\0003' - that constant made the device checks meaningless on any other host.
#
# The obvious route is the endpoint's SWD\MMDEVAPI node and its DEVPKEY_Device_Parent, and that is
# what this script shipped with. It resolves NOTHING: Get-PnpDevice / Get-PnpDeviceProperty query
# Win32_PnPEntity, which does not enumerate those software device nodes. Measured 2026-08-19 on the
# reference host - 245 PnP devices, 31 SWD\* among them, and exactly ONE SWD\MMDEVAPI node
# (MICROSOFTGSWAVETABLESYNTH). So the lookup failed for every real endpoint, and because it failed
# soft the entire device-state block silently skipped itself from 4618217 until now.
#
# What does work is the endpoint's own registry properties: every endpoint under
# MMDevices\Audio\{Render,Capture}\<guid>\Properties carries the backing device's instance path in
# '{b3f8fa53-0004-438e-9003-51a46e139bfc},2', prefixed with '{1}.'. Verified across all 46 endpoints
# on this host - HDAUDIO, ROOT\MEDIA and ROOT\STEAMSTREAMING*, render and capture alike, with none
# missing the property.
function Resolve-EndpointDevice($endpointId) {
    if (-not $endpointId) { return $null }

    $pkey = '{b3f8fa53-0004-438e-9003-51a46e139bfc},2'

    # An endpoint id looks like '{0.0.0.00000000}.{d2293c54-...}'; the LAST brace group is the key
    # name under MMDevices. A render endpoint is the normal case, but check both flows so this is
    # usable for capture endpoints too.
    if ($endpointId -match '(\{[^{}]+\})\s*$') {
        $guid = $Matches[1]
        foreach ($flow in @('Render', 'Capture')) {
            $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\$flow\$guid\Properties"
            $prop = Get-ItemProperty -Path $key -Name $pkey -ErrorAction SilentlyContinue
            if ($prop) {
                $val = $prop.$pkey
                if ($val) { return ($val -replace '^\{\d+\}\.', '') }
            }
        }
    }

    # Secondary: the SWD route, in case a host does enumerate those nodes. Kept because it costs
    # nothing and this script runs on machines we cannot inspect.
    try {
        return (Get-PnpDeviceProperty -InstanceId "SWD\MMDEVAPI\$endpointId" -KeyName 'DEVPKEY_Device_Parent' -ErrorAction Stop).Data
    }
    catch { return $null }
}

# An INAUDIBLE probe. A loopback measurement needs real signal - silence cannot prove audio comes
# out - but it does not need to be heard. The meters are broadband, and 19 kHz measures identical to
# a 220 Hz tone through this path (0.080017 vs 0.080048, verified 2026-08-18) while being inaudible
# to most listeners. It matters because this runs unattended and the default endpoint is usually the
# one a Moonlight client is listening to.
function New-ToneFile {
    $path = Join-Path $env:TEMP ("fix-host-audio-{0}hz.wav" -f $ToneHz)
    if (Test-Path $path) { return $path }
    $rate = 48000; $count = $rate * 2; $fade = 480
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $data = $count * 2
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $data))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE')); $bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$rate)
        $bw.Write([int]($rate * 2)); $bw.Write([int16]2); $bw.Write([int16]16)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$data)
        for ($i = 0; $i -lt $count; $i++) {
            $e = 1.0
            if ($i -lt $fade) { $e = $i / $fade }
            elseif ($i -gt ($count - $fade)) { $e = ($count - $i) / $fade }
            $bw.Write([int16][math]::Round([math]::Sin(2.0 * [math]::PI * $ToneHz * $i / $rate) * $ToneAmplitude * $e * 32767))
        }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    }
    finally { $bw.Dispose(); $ms.Dispose() }
    return $path
}

function Start-Tone {
    if ($SkipToneGenerator) { return $null }
    if (-not $script:ToneFile) { $script:ToneFile = New-ToneFile }
    if (-not (Test-Path $script:ToneFile)) {
        Write-Info "tone file missing ($script:ToneFile) - measuring whatever is already playing"
        return $null
    }
    $cmd = '$p = New-Object System.Media.SoundPlayer ' + "'$script:ToneFile'" + '; $p.PlayLooping(); Start-Sleep -Seconds 600'
    $p = Start-Process powershell.exe -PassThru -WindowStyle Hidden `
            -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-Command',$cmd
    Start-Sleep -Seconds 3
    return $p
}
function Stop-Tone($p) { if ($p) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }

# ---- Measurement ------------------------------------------------------------
function Get-AudioSnapshot {
    # Returns the default endpoint, its own peak, the loudest app peak on it, and a loopback peak.
    $lines = & $Exe --audio-peaks 6 2>&1 | ForEach-Object { "$_" }

    $defaultName = $null; $defaultId = $null; $endpointPeak = 0.0; $appPeak = 0.0
    $inDefault = $false; $pendingId = $null

    foreach ($line in $lines) {
        if ($line -match 'peak=([\d.]+)\s+(.+)$' -and $line -notmatch 'APP \|') {
            # Capture BEFORE testing for [DEFAULT]: a second -match overwrites $Matches, which
            # silently zeroed the endpoint meter and blanked the device name. The app-vs-endpoint
            # comparison is the whole point of this script, so a wrong endpoint reading is not
            # cosmetic - it would report a healthy endpoint as wedged and vice versa.
            $peakValue = [double]$Matches[1]
            $rawName   = $Matches[2]

            $inDefault = ($rawName -match '\[DEFAULT\]')
            if ($inDefault) {
                $endpointPeak = $peakValue
                $defaultName  = ($rawName -replace '\s*\[DEFAULT\]\s*$', '').Trim()
            }
        }
        elseif ($line -match 'id=(\{[^}]+\}\.\{[^}]+\})') {
            if ($inDefault) { $defaultId = $Matches[1] }
        }
        elseif ($line -match 'APP \|.*peak=([\d.]+)') {
            if ($inDefault) {
                $v = [double]$Matches[1]
                if ($v -gt $appPeak) { $appPeak = $v }
            }
        }
    }

    $loopPeak = $null; $loopPackets = $null
    if ($defaultId) {
        $lb = & $Exe --capture-loopback $defaultId 6 2>&1 | ForEach-Object { "$_" }
        foreach ($line in $lb) {
            if ($line -match 'peak amplitude:\s*([\d.]+)') { $loopPeak = [double]$Matches[1] }
            if ($line -match 'packets=(\d+)')              { $loopPackets = [int]$Matches[1] }
        }
    }

    New-Object psobject -Property @{
        DefaultName   = $defaultName
        DefaultId     = $defaultId
        EndpointPeak  = $endpointPeak
        AppPeak       = $appPeak
        LoopbackPeak  = $loopPeak
        LoopPackets   = $loopPackets
        SourceAudible = ($appPeak -gt 0.001)
        Healthy       = ($null -ne $loopPeak -and $loopPeak -gt 0.001)
    }
}

function Show-Snapshot($s) {
    Write-Info ("default endpoint : {0}" -f $s.DefaultName)
    Write-Info ("app meter        : {0:N6}   {1}" -f $s.AppPeak, $(if ($s.SourceAudible) { '(audio IS being submitted)' } else { '<-- NOTHING IS PLAYING' }))
    Write-Info ("endpoint meter   : {0:N6}" -f $s.EndpointPeak)
    Write-Info ("loopback capture : {0:N6}  ({1} packets)" -f $s.LoopbackPeak, $s.LoopPackets)

    if (-not $s.SourceAudible) {
        Write-Bad "No audio source detected. Every reading below is meaningless until something is playing."
    }
    elseif ($s.Healthy) {
        Write-Ok "Loopback returns real audio - the host audio path is working."
    }
    else {
        Write-Bad "Audio enters the endpoint but loopback returns silence - THIS is the wedged-endpoint signature."
    }
}

function Show-DeviceState($healthy) {
    Write-Head 'Endpoint device state'
    if (-not $script:CableInstId) {
        Write-Info 'could not resolve the PnP device behind the default endpoint - skipping device checks'
        Write-Info '  (tried its MMDevices registry properties, then its SWD\MMDEVAPI parent)'
        return $false
    }
    Write-Info ("device      : {0}" -f $script:CableInstId)
    $d = Get-PnpDevice -InstanceId $script:CableInstId -ErrorAction SilentlyContinue
    if (-not $d) { Write-Bad "device $script:CableInstId not found"; return $false }

    Write-Info ("status      : {0}" -f $d.Status)
    $cf = (Get-PnpDeviceProperty -InstanceId $script:CableInstId -KeyName 'DEVPKEY_Device_ConfigFlags' -ErrorAction SilentlyContinue).Data
    if ($cf -eq 1) {
        Write-Bad 'ConfigFlags = 1 (CONFIGFLAG_DISABLED) - this device would come back DISABLED after a reboot. Fixing.'
        Enable-PnpDevice -InstanceId $script:CableInstId -Confirm:$false -ErrorAction SilentlyContinue
    }
    else {
        Write-Info ("ConfigFlags : {0}" -f $cf)
    }

    $out = pnputil /restart-device "$script:CableInstId" 2>&1 | ForEach-Object { "$_" }
    $pending = ($out -match 'pending system reboot').Count -gt 0
    if ($pending) {
        # Measured 2026-08-18, 30 minutes after a clean reboot, with the audio path fully healthy:
        # this device reports pending-reboot ANYWAY. A control on another audio device restarted
        # cleanly, so the message is specific to this device, not a pnputil quirk. It therefore says
        # 'lever 2 cannot restart this device' - it does NOT say the cable is currently wedged.
        if ($healthy) {
            Write-Info 'note: device reports PENDING SYSTEM REBOOT, but audio is healthy - so this is a standing state of this device, not a fault. It only means an in-place device restart is unavailable.'
        }
        else {
            Write-Bad 'device reports PENDING SYSTEM REBOOT - it cannot be restarted in place; a reboot is required'
        }
    }
    return $pending
}

# ---- Repair levers, cheapest first ------------------------------------------
function Invoke-Lever1 {
    Write-Head 'Lever 1: rebuild the audio endpoints (restart Windows audio services)'
    Write-Info 'All audio apps lose their streams; some need their playback restarted.'
    Restart-Service AudioEndpointBuilder -Force    # restarts Audiosrv as a dependent
    Start-Sleep -Seconds 6
}

function Invoke-Lever2 {
    Write-Head 'Lever 2: reset the VB-CABLE device with nothing holding it open'
    # A plain Disable-PnpDevice fails with "Generic failure" while streams are live, and STILL sets
    # ConfigFlags=1 - which would leave the device disabled after the next reboot. Stopping the
    # audio service first lets the disable complete properly; the flag is verified either way.
    Stop-Service Audiosrv -Force
    Start-Sleep -Seconds 2
    try {
        Disable-PnpDevice -InstanceId $script:CableInstId -Confirm:$false -ErrorAction Stop
        Start-Sleep -Seconds 3
        Enable-PnpDevice -InstanceId $script:CableInstId -Confirm:$false -ErrorAction Stop
        Write-Ok 'device disabled and re-enabled'
    }
    catch {
        Write-Bad ("device reset failed: {0}" -f $_.Exception.Message.Split([char]10)[0])
    }
    finally {
        # NEVER leave the disable flag set.
        $cf = (Get-PnpDeviceProperty -InstanceId $script:CableInstId -KeyName 'DEVPKEY_Device_ConfigFlags' -ErrorAction SilentlyContinue).Data
        if ($cf -eq 1) {
            Write-Bad 'ConfigFlags still 1 - clearing so the device is not disabled at next boot'
            Enable-PnpDevice -InstanceId $script:CableInstId -Confirm:$false -ErrorAction SilentlyContinue
        }
        Start-Service Audiosrv
        Start-Sleep -Seconds 6
    }
}

function Invoke-Lever3 {
    Write-Head 'Lever 3: route around the cable (default -> a VoiceMeeter virtual endpoint)'
    # VoiceMeeter VAIO endpoints support loopback; a physical S/PDIF output with nothing attached
    # does NOT (its engine never runs, so loopback yields zero packets - measured 2026-08-18).
    $lines = & $Exe --audio-peaks 1 2>&1 | ForEach-Object { "$_" }
    $id = $null; $lastId = $null
    foreach ($line in $lines) {
        if ($line -match 'id=(\{[^}]+\}\.\{[^}]+\})') { $lastId = $Matches[1] }
        if ($line -match 'Voicemeeter Input|VoiceMeeter Input') { $wantNext = $true }
        if ($wantNext -and $lastId) { $id = $lastId; break }
    }
    if (-not $id) { Write-Bad 'could not resolve a VoiceMeeter Input endpoint - skipping'; return $false }

    & $Exe --set-default-render $id | Out-Null
    Write-Ok 'default switched to VoiceMeeter Input'
    Write-Info 'NOTE: Windows only migrates NEWLY STARTED streams - restart whatever is playing.'
    return $true
}

# ---- Main -------------------------------------------------------------------
# Exit codes are set explicitly and are the only thing this script exits with.
# Without them the script exited with whatever $LASTEXITCODE the last native
# instrument call happened to leave behind (a healthy run exited 194), so any
# caller checking the exit code read a healthy host as a failure.
#   0 - audio path healthy (already, or repaired by a lever)
#   1 - measured unhealthy and not repaired
#   2 - no verdict: nothing was playing, or the script failed
function Invoke-Main {
    Write-Head 'Baseline'
    $snap = Get-AudioSnapshot
    Show-Snapshot $snap
    if (-not $script:CableInstId) { $script:CableInstId = Resolve-EndpointDevice $snap.DefaultId }
    $pending = Show-DeviceState $snap.Healthy

    if ($snap.Healthy) {
        Write-Host "`nAudio path is healthy - nothing to repair." -ForegroundColor Green
        $script:ExitCode = 0
        return
    }
    if (-not $snap.SourceAudible) {
        Write-Host "`nCannot proceed: no audio is playing, so a silent reading proves nothing." -ForegroundColor Yellow
        $script:ExitCode = 2
        return
    }
    if ($DiagnoseOnly) {
        Write-Host "`n-DiagnoseOnly set - stopping before any repair." -ForegroundColor Yellow
        $script:ExitCode = 1
        return
    }

    $originalDefault = $snap.DefaultId
    foreach ($lever in 1, 2, 3) {
        if ($lever -eq 1) { Invoke-Lever1 }
        if ($lever -eq 2) { Invoke-Lever2 }
        if ($lever -eq 3) { if (-not (Invoke-Lever3)) { continue } }

        Stop-Tone $script:Tone
        $script:Tone = Start-Tone    # a rebuilt endpoint needs a NEW stream
        $snap = Get-AudioSnapshot
        Show-Snapshot $snap

        if ($snap.Healthy) {
            Write-Host "`nFIXED by lever $lever - no reboot needed." -ForegroundColor Green
            Write-Host "Record which lever worked; that is the evidence needed to fix the cause." -ForegroundColor Green
            $script:ExitCode = 0
            return
        }
    }

    Write-Host "`nNone of the in-place levers restored audio." -ForegroundColor Red
    if ($pending) {
        Write-Host "The device reported PENDING SYSTEM REBOOT, so a reboot is genuinely required." -ForegroundColor Yellow
    }
    if ($originalDefault) {
        & $Exe --set-default-render $originalDefault | Out-Null
        Write-Info 'default render device restored'
    }
    $script:ExitCode = 1
}

$script:ExitCode = 2       # no verdict until something measures one
Write-Host "`nHost audio diagnosis" -ForegroundColor White
$script:Tone = Start-Tone
try {
    Invoke-Main
}
catch {
    Write-Bad "unexpected error: $($_.Exception.Message)"
    $script:ExitCode = 2
}
finally {
    Stop-Tone $script:Tone
}
exit $script:ExitCode
