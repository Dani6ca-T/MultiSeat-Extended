$logDir = 'C:\ProgramData\MultiSeat\logs'
$files = Get-ChildItem $logDir -File -EA SilentlyContinue | Sort-Object LastWriteTime
if (-not $files) { Write-Host "No log files found in $logDir"; exit }
Write-Host "Log files:"
$files | ForEach-Object { Write-Host "  $($_.Name) ($($_.LastWriteTime))" }
Write-Host "`n=== Last 80 lines of most recent log ==="
Get-Content $files[-1].FullName -Tail 80
