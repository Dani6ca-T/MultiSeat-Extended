<#
.SYNOPSIS
    Measure the host audio path automatically, every time this machine resumes from sleep.

.DESCRIPTION
    Exists to settle one question that repeated manual testing could not: does the VB-CABLE endpoint
    wedge during SLEEP itself, or only once a Moonlight client connects afterwards?

    Nobody is at this host (it is headless), and by the time the silence is noticed the moment has
    passed. So this runs unattended on resume, before any stream necessarily exists, and writes a
    verdict with a timestamp. Two possible outcomes, and they point at different causes:

        wedged already at resume  -> sleep/resume breaks the endpoint
        healthy at resume         -> something AFTER resume breaks it, and the leading suspect is
                                     Apollo's "Failed to install Steam Streaming Speakers: 259",
                                     logged at every stream start

    Trigger note, measured rather than assumed: the usual resume trigger
    (Microsoft-Windows-Power-Troubleshooter, event 1) has fired ONCE on this host, back in April. It
    is not reliable here. Kernel-Power 107 fires on every real resume, so that is what the task
    subscribes to.

    Runs INTERACTIVELY in the console session on purpose. Audio APIs are session-scoped: the same
    check running as SYSTEM in session 0 would measure silence forever and report a wedge every
    single time - a confident answer to nothing.

    Uses a quiet generated tone rather than a Windows alert sound, because this task tends to fire at
    exactly the moment a Moonlight client connects, and the tone goes to the default endpoint that
    client is listening to.

.PARAMETER Register
    Create or refresh the scheduled task, then exit.

.PARAMETER Unregister
    Remove the scheduled task, then exit.

.PARAMETER ShowLog
    Print the log collected so far, then exit.

.PARAMETER SettleSeconds
    How long to wait after the resume event before measuring, so the audio stack can finish coming
    back. Default 45.

.EXAMPLE
    .\wake-audio-check.ps1 -Register
    .\wake-audio-check.ps1 -ShowLog
    .\wake-audio-check.ps1 -SettleSeconds 0

.NOTES
    Exit codes match fix-host-audio.ps1: 0 healthy, 1 wedged, 2 no verdict.

    ASCII only, same reason as fix-host-audio.ps1: PowerShell 5.1 reads these files as ANSI and
    non-ASCII punctuation becomes mojibake that breaks parsing.
#>
[CmdletBinding()]
param(
    [switch] $Register,
    [switch] $Unregister,
    [switch] $ShowLog,
    [int]    $SettleSeconds = 45
)

$ErrorActionPreference = 'Stop'

$TaskName  = 'MultiSeat-WakeAudioCheck'
$Root      = 'C:\ProgramData\MultiSeat\wake-audio'
$LogPath   = Join-Path $Root 'wake-audio.log'
$TonePath  = Join-Path $Root 'quiet-tone.wav'
$Diagnoser = Join-Path $PSScriptRoot 'fix-host-audio.ps1'

function Write-Log($text) {
    Add-Content -LiteralPath $LogPath -Value $text -Encoding ASCII
}

# A soft 220 Hz sine. The diagnoser's thresholds are 0.001 and this peaks near 0.08, so it sits
# roughly 80x above the floor while staying easy to ignore if it lands in a live stream.
function New-QuietToneWav {
    if (Test-Path $TonePath) { return }
    $rate    = 44100
    $seconds = 2
    $freq    = 220.0
    $amp     = 0.08
    $count   = $rate * $seconds
    $fade    = 441
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $dataBytes = $count * 2
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $bw.Write([int](36 + $dataBytes))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $bw.Write([int]16)
        $bw.Write([int16]1)
        $bw.Write([int16]1)
        $bw.Write([int]$rate)
        $bw.Write([int]($rate * 2))
        $bw.Write([int16]2)
        $bw.Write([int16]16)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $bw.Write([int]$dataBytes)
        for ($i = 0; $i -lt $count; $i++) {
            $envelope = 1.0
            if ($i -lt $fade) { $envelope = $i / $fade }
            elseif ($i -gt ($count - $fade)) { $envelope = ($count - $i) / $fade }
            $sample = [math]::Sin(2.0 * [math]::PI * $freq * $i / $rate) * $amp * $envelope
            $bw.Write([int16][math]::Round($sample * 32767))
        }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($TonePath, $ms.ToArray())
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }
}

function Get-LastResumeTime {
    try {
        $e = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=107} -MaxEvents 1 -ErrorAction Stop
        return $e.TimeCreated
    }
    catch { return $null }
}

function Get-ApolloContext {
    $procs = @(Get-Process -Name 'sunshine','apollo' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return 'none running' }
    $names = ($procs | ForEach-Object { $_.ProcessName + '/' + $_.Id }) -join ', '
    return ('{0} running: {1}' -f $procs.Count, $names)
}

function Invoke-Check {
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    New-QuietToneWav

    $resume = Get-LastResumeTime
    if ($SettleSeconds -gt 0) { Start-Sleep -Seconds $SettleSeconds }

    $apollo = Get-ApolloContext
    # *>&1, not 2>&1: the diagnoser reports through Write-Host, which goes to the information
    # stream. With 2>&1 this captured NOTHING and the log recorded a verdict with no evidence
    # under it - measured, not theorised.
    $out    = & $Diagnoser -DiagnoseOnly -ToneWav $TonePath *>&1 | ForEach-Object { "$_" }
    $code   = $LASTEXITCODE

    switch ($code) {
        0       { $verdict = 'HEALTHY    - audio path works at this point in time' }
        1       { $verdict = 'WEDGED     - audio is submitted but does not come out' }
        2       { $verdict = 'NO VERDICT - nothing measurable, or the diagnoser failed' }
        default { $verdict = "UNEXPECTED - diagnoser exited $code" }
    }

    if ($resume) { $resumeText = $resume.ToString('yyyy-MM-dd HH:mm:ss') } else { $resumeText = 'unknown' }
    $header = @(
        '================================================================'
        ('{0}  wake-audio-check' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
        ('  last resume  : {0}  (Kernel-Power 107)' -f $resumeText)
        ('  settle wait  : {0} s' -f $SettleSeconds)
        ('  apollo       : {0}' -f $apollo)
        ('  VERDICT      : {0}  (exit {1})' -f $verdict, $code)
        '----------------------------------------------------------------'
    )
    Write-Log ($header -join "`r`n")
    Write-Log (($out | ForEach-Object { '  ' + $_ }) -join "`r`n")
    Write-Log ''

    Write-Host ($header -join "`n")
    $out | ForEach-Object { Write-Host "  $_" }
    Write-Host "logged to $LogPath"
    return $code
}

function Register-WakeTask {
    if (-not (Test-Path $Diagnoser)) { throw "fix-host-audio.ps1 not found next to this script ($Diagnoser)" }
    $self = Join-Path $PSScriptRoot 'wake-audio-check.ps1'

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $self)

    # Kernel-Power 107, NOT Power-Troubleshooter 1 - see the trigger note in the description.
    $subscription = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]</Select></Query></QueryList>"

    $class   = Get-CimClass -ClassName MSFT_TaskEventTrigger -Namespace Root/Microsoft/Windows/TaskScheduler
    $trigger = New-CimInstance -CimClass $class -ClientOnly
    $trigger.Enabled      = $true
    $trigger.Subscription = $subscription

    # Interactive, in the console session: audio APIs are session-scoped and session 0 sees nothing.
    $principal = New-ScheduledTaskPrincipal -UserId ('{0}\{1}' -f $env:USERDOMAIN, $env:USERNAME) -LogonType Interactive -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 15)

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Measures host audio on resume from sleep (VB-CABLE wedge investigation).' -Force | Out-Null

    Write-Host "registered task '$TaskName'" -ForegroundColor Green
    Write-Host "  runs as     : $($principal.UserId) (interactive, highest)"
    Write-Host "  triggers on : System / Microsoft-Windows-Kernel-Power / EventID 107"
    Write-Host "  writes      : $LogPath"
}

function Unregister-WakeTask {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "removed task '$TaskName'" -ForegroundColor Yellow
    }
    else {
        Write-Host "task '$TaskName' was not registered" -ForegroundColor Gray
    }
}

if ($Register)   { Register-WakeTask;   exit 0 }
if ($Unregister) { Unregister-WakeTask; exit 0 }
if ($ShowLog) {
    if (Test-Path $LogPath) { Get-Content -LiteralPath $LogPath } else { Write-Host "no log yet at $LogPath" }
    exit 0
}
exit (Invoke-Check)
