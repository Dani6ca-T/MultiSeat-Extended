$nameKey = '{a45c254e-df1c-4efd-8020-67d146a850e0},14'

Write-Host "=== Render (output) endpoints ==="
$renderBase = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
Get-ChildItem $renderBase -EA SilentlyContinue | ForEach-Object {
    $id = $_.PSChildName
    $props = Get-ItemProperty "$($_.PSPath)\Properties" -EA SilentlyContinue
    $name = $props.$nameKey
    if ($name) { Write-Host "  $name`n    {$id}" }
}

Write-Host "`n=== Capture (input) endpoints ==="
$captureBase = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture'
Get-ChildItem $captureBase -EA SilentlyContinue | ForEach-Object {
    $id = $_.PSChildName
    $props = Get-ItemProperty "$($_.PSPath)\Properties" -EA SilentlyContinue
    $name = $props.$nameKey
    if ($name) { Write-Host "  $name`n    {$id}" }
}
