Write-Host "=== LocalMachine cert stores ==="
Get-ChildItem 'Cert:\LocalMachine' | ForEach-Object { Write-Host $_.PSChildName }

Write-Host "`n=== Certs in Remote Desktop store ==="
$rdStore = Get-ChildItem 'Cert:\LocalMachine\Remote Desktop' -EA SilentlyContinue
if ($rdStore) { $rdStore | ForEach-Object { Write-Host "  Thumbprint=$($_.Thumbprint) Subject=$($_.Subject)" } }
else { Write-Host "  Empty or not found" }

Write-Host "`n=== SSLCertificateSHA1Hash registry value ==="
$k = 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
$v = (Get-ItemProperty $k -EA SilentlyContinue).SSLCertificateSHA1Hash
if ($v) { Write-Host "  $(($v | ForEach-Object { $_.ToString('X2') }) -join '')" }
else { Write-Host "  NOT SET (null or missing)" }

Write-Host "`n=== TermService status ==="
Get-Service TermService | ForEach-Object { Write-Host "  Status: $($_.Status)" }
