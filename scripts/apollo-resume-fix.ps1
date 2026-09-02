<#
.SYNOPSIS
    Restart the standalone Apollo after the host resumes from sleep, so the first connection is
    not a black screen.

.DESCRIPTION
    THE FAULT. The SudoVDA virtual display does not survive S3 sleep, but Apollo's registration of
    it does. On resume, Apollo's cleanup fails - it logs "Failed to locate an output device" and
    never logs "Virtual Display removed successfully" - so its "is a display active?" check then
    returns a false positive. The next client to connect gets attached to a display that is no
    longer there, and sees a BLACK SCREEN. The encoder is healthy; it simply has nothing to
    capture.

    Measured on the reference host, 2026-09-02:

        07:44:24.591  CLIENT DISCONNECTED                     <- resume from S3 drops the session
        07:44:24.910  Error: Failed to locate an output device <- cleanup FAILED, display left stale
        07:44:56.092  CLIENT CONNECTED                         <- black screen
        07:47:53.229  CLIENT DISCONNECTED                      <- user gives up
        07:47:53.537  Virtual Display removed successfully     <- NOW the cleanup works
        07:47:58.621  Virtual Display created at \\.\DISPLAY30 <- fresh display
        07:48:01.244  CLIENT CONNECTED                         <- works

    Disconnecting and reconnecting is the manual workaround - the first disconnect performs the
    cleanup that failed on resume. This does it for you instead, by restarting ApolloService once
    the machine wakes, which forces a clean display state before anyone connects.

    WHY EVENT 131 AND NOT THE ONE EVERY GUIDE NAMES. The usual advice is to trigger on
    Microsoft-Windows-Power-Troubleshooter event 1. On this host that provider does not fire AT ALL
    - checked across the last six resumes, every one produced Kernel-Power 130/131/566 and no
    Power-Troubleshooter event. A task built on the usual advice would sit there looking correct
    and never run. 131 is the resume firmware-timing event and fired on all six.

.PARAMETER Install
    Register the scheduled task. Runs as SYSTEM, triggered on resume, with a settling delay.

.PARAMETER Uninstall
    Remove the scheduled task.

.PARAMETER DelaySeconds
    How long to wait after resume before restarting Apollo. Default 30. Devices are still coming
    back at resume+10s on this host (the NIC re-initialised then), and restarting Apollo into a
    half-initialised device tree risks recreating the very state this is meant to clear.

.EXAMPLE
    .\apollo-resume-fix.ps1 -Install

.EXAMPLE
    .\apollo-resume-fix.ps1
    Run the fix once, now. Useful to test it without sleeping the machine.
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Uninstall,
    [int]$DelaySeconds = 30
)

$ErrorActionPreference = 'Stop'

$TaskName    = 'MultiSeat-ApolloResumeFix'
$ApolloLog   = 'C:\Program Files\Apollo\config\sunshine.log'
$ServiceName = 'ApolloService'
$OwnPath     = $MyInvocation.MyCommand.Path

function Write-Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Write-Host "  $msg"
    try {
        $dir = 'C:\ProgramData\MultiSeat\logs'
        if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -Path (Join-Path $dir 'apollo-resume-fix.log') -Value $line -Encoding ASCII
    } catch { }
}

# ---- install / uninstall ----------------------------------------------

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "  Removed scheduled task '$TaskName'." -ForegroundColor Green
    } else {
        Write-Host "  Task '$TaskName' is not registered." -ForegroundColor Yellow
    }
    exit 0
}

if ($Install) {
    Write-Host ''
    Write-Host '== installing the Apollo resume fix ==' -ForegroundColor Cyan

    if (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) {
        Write-Host "  '$ServiceName' is not a service on this host - nothing to restart." -ForegroundColor Yellow
        Write-Host "  If Apollo runs as a bare process here, this fix does not apply as written." -ForegroundColor Yellow
        exit 2
    }

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -DelaySeconds {1}' -f $OwnPath, $DelaySeconds)

    # An event trigger, because there is no built-in "on resume" trigger type. Subscribing to
    # Kernel-Power 131 rather than Power-Troubleshooter 1 - see the note in the help above; the
    # popular choice does not fire on this machine.
    $class   = Get-CimClass -ClassName MSFT_TaskEventTrigger -Namespace Root/Microsoft/Windows/TaskScheduler
    $trigger = New-CimInstance -CimClass $class -ClientOnly
    $trigger.Enabled = $true
    $trigger.Subscription =
        '<QueryList><Query Id="0" Path="System"><Select Path="System">' +
        '*[System[Provider[@Name=''Microsoft-Windows-Kernel-Power''] and EventID=131]]' +
        '</Select></Query></QueryList>'

    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force `
        -Description 'Restarts Apollo after resume from sleep so the first client connection is not a black screen (stale SudoVDA display).' | Out-Null

    Write-Host "  Registered '$TaskName'" -ForegroundColor Green
    Write-Host "    trigger : System log, Kernel-Power, EventID 131 (resume)"
    Write-Host "    delay   : ${DelaySeconds}s after resume, so devices settle first"
    Write-Host "    runs as : SYSTEM"
    Write-Host "    log     : C:\ProgramData\MultiSeat\logs\apollo-resume-fix.log"
    Write-Host ''
    Write-Host "  Test it without sleeping the machine:  .\apollo-resume-fix.ps1" -ForegroundColor DarkGray
    exit 0
}

# ---- the action itself -------------------------------------------------

Write-Host ''
Write-Host '== apollo resume fix ==' -ForegroundColor Cyan

if ($DelaySeconds -gt 0) {
    Write-Log "resume detected; waiting ${DelaySeconds}s for devices to settle"
    Start-Sleep -Seconds $DelaySeconds
}

# Do not restart Apollo out from under someone who is streaming. On resume nobody should be
# connected, but this task can also be run by hand, and an unconditional restart would kill a live
# session - the same mistake as tearing down a seat mid-stream.
$connected = $false
if (Test-Path $ApolloLog) {
    $last = Get-Content $ApolloLog -ErrorAction SilentlyContinue |
            Select-String -Pattern 'CLIENT CONNECTED|CLIENT DISCONNECTED' |
            Select-Object -Last 1
    if ($last -and $last.Line -match 'CLIENT CONNECTED') { $connected = $true }
}

if ($connected) {
    Write-Log 'a client is CONNECTED - refusing to restart Apollo and interrupt it'
    exit 1
}

$svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Log "service '$ServiceName' not found - nothing to do"
    exit 2
}

try {
    Restart-Service -Name $ServiceName -Force -ErrorAction Stop
    Start-Sleep -Seconds 2
    $now = (Get-Service $ServiceName).Status
    Write-Log "restarted $ServiceName - now $now"
    if ($now -ne 'Running') { exit 1 }
    exit 0
} catch {
    Write-Log "failed to restart ${ServiceName}: $_"
    exit 1
}
