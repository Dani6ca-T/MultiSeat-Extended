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

.EXAMPLE
    .\fix-host-audio.ps1 -DiagnoseOnly
    .\fix-host-audio.ps1
#>
[CmdletBinding()]
param(
    [switch] $DiagnoseOnly,
    [switch] $SkipToneGenerator
)

$ErrorActionPreference = 'Stop'

$Exe         = 'C:\Program Files\MultiSeat\MultiSeat.Service.exe'
$CableInstId = 'ROOT\MEDIA\0003'      # VB-Audio Virtual Cable on this host
$ToneWav     = 'C:\Windows\Media\Alarm03.wav'

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

function Show-DeviceState {
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
        Write-Bad 'device reports PENDING SYSTEM REBOOT - in-place resets cannot work; a reboot is required'
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
Write-Host "`nHost audio diagnosis" -ForegroundColor White
$tone = Start-Tone
try {
    Write-Head 'Baseline'
    $snap = Get-AudioSnapshot
    Show-Snapshot $snap
    $pending = Show-DeviceState

    if ($snap.Healthy) {
        Write-Host "`nAudio path is healthy - nothing to repair." -ForegroundColor Green
        return
    }
    if (-not $snap.SourceAudible) {
        Write-Host "`nCannot proceed: no audio is playing, so a silent reading proves nothing." -ForegroundColor Yellow
        return
    }
    if ($DiagnoseOnly) {
        Write-Host "`n-DiagnoseOnly set - stopping before any repair." -ForegroundColor Yellow
        return
    }

    $originalDefault = $snap.DefaultId
    foreach ($lever in 1, 2, 3) {
        if ($lever -eq 1) { Invoke-Lever1 }
        if ($lever -eq 2) { Invoke-Lever2 }
        if ($lever -eq 3) { if (-not (Invoke-Lever3)) { continue } }

        Stop-Tone $tone
        $tone = Start-Tone          # a rebuilt endpoint needs a NEW stream
        $snap = Get-AudioSnapshot
        Show-Snapshot $snap

        if ($snap.Healthy) {
            Write-Host "`nFIXED by lever $lever - no reboot needed." -ForegroundColor Green
            Write-Host "Record which lever worked; that is the evidence needed to fix the cause." -ForegroundColor Green
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
}
finally {
    Stop-Tone $tone
}
