$nameKey = '{a45c254e-df1c-4efd-8020-67d146a850e0},14'

Write-Host "=== Render (output) endpoints ==="
$renderBase = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
$items = Get-ChildItem $renderBase -EA SilentlyContinue
Write-Host "Found $($items.Count) render devices"
foreach ($item in $items) {
    $id = $item.PSChildName
    $propPath = Join-Path $item.PSPath 'Properties'
    if (Test-Path $propPath) {
        $props = Get-ItemProperty $propPath -EA SilentlyContinue
        $name = $props.$nameKey
        if ($name) { Write-Host "  '$name'  {$id}" }
        else { Write-Host "  (no name)  {$id}" }
    }
}

Write-Host "`n=== Capture (input) endpoints ==="
$captureBase = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture'
$items2 = Get-ChildItem $captureBase -EA SilentlyContinue
Write-Host "Found $($items2.Count) capture devices"
foreach ($item in $items2) {
    $id = $item.PSChildName
    $propPath = Join-Path $item.PSPath 'Properties'
    if (Test-Path $propPath) {
        $props = Get-ItemProperty $propPath -EA SilentlyContinue
        $name = $props.$nameKey
        if ($name) { Write-Host "  '$name'  {$id}" }
        else { Write-Host "  (no name)  {$id}" }
    }
}
