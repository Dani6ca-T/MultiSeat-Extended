# Check HKCU trust store
$trustKey = 'HKCU:\Software\Microsoft\Terminal Server Client\Servers\127.0.0.2'
$stored = (Get-ItemProperty $trustKey -EA SilentlyContinue).CertHash
if ($stored) {
    $storedHex = ($stored | ForEach-Object { $_.ToString('X2') }) -join ''
    Write-Host "HKCU CertHash: $storedHex"
} else {
    Write-Host "HKCU CertHash: NOT SET"
}

# Check actual cert in Remote Desktop store
$cert = Get-ChildItem 'Cert:\LocalMachine\Remote Desktop' -EA SilentlyContinue | Select-Object -First 1
if ($cert) {
    $certHex = ($cert.GetCertHash() | ForEach-Object { $_.ToString('X2') }) -join ''
    Write-Host "Cert store thumbprint: $certHex"
    Write-Host "Match: $($storedHex -eq $certHex)"
} else {
    Write-Host "Cert store: NO CERT FOUND"
}

# Check Default.rdp
$rdp = [Environment]::GetFolderPath('MyDocuments')
$rdpFile = Join-Path $rdp 'Default.rdp'
Write-Host "Default.rdp path: $rdpFile"
Write-Host "Default.rdp exists: $(Test-Path $rdpFile)"
if (Test-Path $rdpFile) { Get-Content $rdpFile }

# Check AuthenticationLevel policy
$al = (Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services' -Name AuthenticationLevel -EA SilentlyContinue).AuthenticationLevel
Write-Host "Policy AuthenticationLevel: $al"
