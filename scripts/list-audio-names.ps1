$devDescKey = '{a45c254e-df1c-4efd-8020-67d146a850e0},2'
$ifaceKey   = '{b3f8fa53-0004-438e-9003-51a46e139bfc},6'

Write-Host "=== Render devices ==="
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' | ForEach-Object {
    $props = Get-ItemProperty (Join-Path $_.PSPath 'Properties') -EA SilentlyContinue
    $desc = $props.$devDescKey
    $iface = $props.$ifaceKey
    if ($desc) { Write-Host "  '$desc ($iface)'  {$($_.PSChildName)}" }
}

Write-Host "`n=== Capture devices ==="
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture' | ForEach-Object {
    $props = Get-ItemProperty (Join-Path $_.PSPath 'Properties') -EA SilentlyContinue
    $desc = $props.$devDescKey
    $iface = $props.$ifaceKey
    if ($desc) { Write-Host "  '$desc ($iface)'  {$($_.PSChildName)}" }
}
