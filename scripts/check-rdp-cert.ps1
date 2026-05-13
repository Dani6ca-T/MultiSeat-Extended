$hash = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' -Name SSLCertificateSHA1Hash -EA SilentlyContinue).SSLCertificateSHA1Hash
if ($hash -and $hash.Length -gt 0) {
    Write-Host "SSLCertificateSHA1Hash: $(($hash | ForEach-Object { $_.ToString('X2') }) -join '')"
} else {
    Write-Host "SSLCertificateSHA1Hash: NOT SET"
}

$rdpCert = Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.FriendlyName -eq 'Remote Desktop' } | Select-Object -First 1
if ($rdpCert) {
    Write-Host "RDP cert thumbprint: $($rdpCert.Thumbprint)"
} else {
    Write-Host "RDP cert in Cert:\LocalMachine\My: NOT FOUND"
    Write-Host "All certs in LocalMachine\My:"
    Get-ChildItem 'Cert:\LocalMachine\My' | ForEach-Object { Write-Host "  Subject=$($_.Subject) FriendlyName='$($_.FriendlyName)' Thumbprint=$($_.Thumbprint)" }
}

$trustKey = 'HKCU:\Software\Microsoft\Terminal Server Client\Servers\127.0.0.2'
if (Test-Path $trustKey) {
    $cert = (Get-ItemProperty $trustKey -Name CertHash -EA SilentlyContinue).CertHash
    if ($cert) {
        Write-Host "HKCU CertHash: $(($cert | ForEach-Object { $_.ToString('X2') }) -join '')"
    } else {
        Write-Host "HKCU CertHash: NOT SET"
    }
} else {
    Write-Host "HKCU 127.0.0.2 trust key: NOT FOUND"
}
