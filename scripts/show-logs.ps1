<#
.SYNOPSIS
    Show MultiSeat service logs.
.DESCRIPTION
    The service does NOT write its own log files. It is hosted via AddWindowsService(),
    whose default logging provider is the Windows Event Log, so service logs live in the
    Application log under two sources:

        MultiSeat.Service  -- application logging (ILogger categories; the detailed logs)
        MultiSeatService   -- service lifecycle (started / stopped)

    C:\ProgramData\MultiSeat\logs\ is NOT where service logs go. The only file that ever
    lands there is audio-helper.log, written by the in-session audio helper after a seat
    has run. Per-seat Apollo logs live under C:\ProgramData\MultiSeat\apollo\<account>\.

    This script shows all three.
.PARAMETER Lines
    Number of recent entries to show. Default 80.
.PARAMETER Hours
    How far back to search the Event Log, in hours. Default 24.
.EXAMPLE
    .\show-logs.ps1
.EXAMPLE
    .\show-logs.ps1 -Lines 200 -Hours 72
#>
param(
    [int]$Lines = 80,
    [int]$Hours = 24
)

$providers = @('MultiSeat.Service', 'MultiSeatService')
$since     = (Get-Date).AddHours(-$Hours)

# -- Service log (Event Log) ------------------------------------------
Write-Host "=== MultiSeat service log -- Application event log, last $Hours h ===" -ForegroundColor Cyan

$events = foreach ($p in $providers) {
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = $p; StartTime = $since } -ErrorAction SilentlyContinue
}

if (-not $events) {
    Write-Host "  No events from $($providers -join ' / ') in the last $Hours hours." -ForegroundColor Yellow
    $svc = Get-Service MultiSeatService -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "  Service status: $($svc.Status). Try a longer window: -Hours 168" -ForegroundColor DarkGray
    } else {
        Write-Host "  MultiSeatService is not installed -- run scripts\install-service.ps1" -ForegroundColor DarkGray
    }
} else {
    $events | Sort-Object TimeCreated | Select-Object -Last $Lines | ForEach-Object {
        $color = switch ($_.LevelDisplayName) {
            'Error'       { 'Red' }
            'Critical'    { 'Red' }
            'Warning'     { 'Yellow' }
            default       { 'Gray' }
        }
        Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1,-11} {2}" -f $_.TimeCreated, $_.LevelDisplayName, $_.Message) -ForegroundColor $color
    }
}

# -- Helper log files (these really are files) ------------------------
$logDir = 'C:\ProgramData\MultiSeat\logs'
Write-Host "`n=== Helper log files in $logDir ===" -ForegroundColor Cyan

$files = Get-ChildItem $logDir -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
if (-not $files) {
    Write-Host "  (none -- expected. Only audio-helper.log appears here, and only after a seat has run.)" -ForegroundColor DarkGray
} else {
    $files | ForEach-Object { Write-Host "  $($_.Name)  ($($_.LastWriteTime))" }
    Write-Host "`n--- Last $Lines lines of $($files[-1].Name) ---" -ForegroundColor Cyan
    Get-Content $files[-1].FullName -Tail $Lines
}

# -- Per-seat Apollo logs ---------------------------------------------
$apolloLogs = Get-ChildItem 'C:\ProgramData\MultiSeat\apollo' -Recurse -Filter 'apollo.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime
if ($apolloLogs) {
    Write-Host "`n=== Per-seat Apollo logs (pass a path to Get-Content to read one) ===" -ForegroundColor Cyan
    $apolloLogs | ForEach-Object { Write-Host "  $($_.FullName)  ($($_.LastWriteTime))" }
}
