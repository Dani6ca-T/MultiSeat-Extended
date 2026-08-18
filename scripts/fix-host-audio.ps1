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
    twice.

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
    The .wav to loop while measuring. Defaults to a Windows system sound. The unattended wake check
    passes a quiet generated tone instead, so an automatic run does not blast a system alert into a
    live Moonlight stream.

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
    [string] $ToneWav = 'C:\Windows\Media\Alarm03.wav'
)

$ErrorActionPreference = 'Stop'

$Exe         = 'C:\Program Files\MultiSeat\MultiSeat.Service.exe'
$CableInstId = 'ROOT\MEDIA\0003'      # VB-Audio Virtual Cable on this host

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
function Start-Tone {
    if ($SkipToneGenerator) { return $null }
    if (-not (Test-Path $ToneWav)) {
        Write-Info "tone file missing ($ToneWav) - measuring whatever is already playing"
        return $null
    }
    $cmd = '$p = New-Object System.Media.SoundPlayer ' + "'$ToneWav'" + '; $p.PlayLooping(); Start-Sleep -Seconds 600'
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
    Write-Head 'VB-CABLE device state'
    $d = Get-PnpDevice -InstanceId $CableInstId -ErrorAction SilentlyContinue
    if (-not $d) { Write-Bad "device $CableInstId not found"; return $false }

    Write-Info ("status      : {0}" -f $d.Status)
    $cf = (Get-PnpDeviceProperty -InstanceId $CableInstId -KeyName 'DEVPKEY_Device_ConfigFlags' -ErrorAction SilentlyContinue).Data
    if ($cf -eq 1) {
        Write-Bad 'ConfigFlags = 1 (CONFIGFLAG_DISABLED) - this device would come back DISABLED after a reboot. Fixing.'
        Enable-PnpDevice -InstanceId $CableInstId -Confirm:$false -ErrorAction SilentlyContinue
    }
    else {
        Write-Info ("ConfigFlags : {0}" -f $cf)
    }

    $out = pnputil /restart-device "$CableInstId" 2>&1 | ForEach-Object { "$_" }
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
        Disable-PnpDevice -InstanceId $CableInstId -Confirm:$false -ErrorAction Stop
        Start-Sleep -Seconds 3
        Enable-PnpDevice -InstanceId $CableInstId -Confirm:$false -ErrorAction Stop
        Write-Ok 'device disabled and re-enabled'
    }
    catch {
        Write-Bad ("device reset failed: {0}" -f $_.Exception.Message.Split([char]10)[0])
    }
    finally {
        # NEVER leave the disable flag set.
        $cf = (Get-PnpDeviceProperty -InstanceId $CableInstId -KeyName 'DEVPKEY_Device_ConfigFlags' -ErrorAction SilentlyContinue).Data
        if ($cf -eq 1) {
            Write-Bad 'ConfigFlags still 1 - clearing so the device is not disabled at next boot'
            Enable-PnpDevice -InstanceId $CableInstId -Confirm:$false -ErrorAction SilentlyContinue
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
