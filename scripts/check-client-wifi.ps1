<#
.SYNOPSIS
    Run this ON THE MOONLIGHT CLIENT (not the host) to see whether its wifi can carry a stream.

.DESCRIPTION
    "It lags but I'm close to the router" almost always comes down to one of three things, and all
    three are invisible from the host - the host is wired, so it cannot see the client's radio at
    all. This reports them from the client side:

      1. BAND. 2.4 GHz is shared with every microwave, doorbell and neighbour on the street, and
         tops out far below what a game stream wants. Handhelds and laptops band-steer onto it more
         often than people expect, especially after roaming. Channel <= 14 means 2.4 GHz.

      2. LINK RATE. The negotiated rate is the ceiling, and real throughput runs well under it. A
         receive rate under ~200 Mbps will struggle to hold a 1080p60 stream steady even though a
         speed test looks fine, because streaming cares about consistency, not peak.

      3. POWER SAVING. The single most common cause of intermittent stream latency on battery
         devices. The radio sleeps between beacons, and every wake costs milliseconds at exactly
         the wrong moment. It is set per power plan, so it can be on for battery and off for AC.

    Read-only. Prints a verdict; changes nothing.

.EXAMPLE
    .\check-client-wifi.ps1
#>
[CmdletBinding()]
param()

Write-Host ''
Write-Host '== client wifi check ==' -ForegroundColor Cyan

$raw = netsh wlan show interfaces 2>&1
if ($raw -notmatch 'SSID') {
    Write-Host '  No active wireless interface.' -ForegroundColor Yellow
    Write-Host '  If this machine is on Ethernet, wifi is not your problem - say so and we look elsewhere.'
    exit 2
}

function Field($label) {
    $line = $raw | Where-Object { $_ -match "^\s*$label\s*:" } | Select-Object -First 1
    if ($line) { return ($line -split ':', 2)[1].Trim() }
    return $null
}

$ssid    = Field 'SSID'
$signal  = Field 'Signal'
$radio   = Field 'Radio type'
$channel = Field 'Channel'
$rx      = Field 'Receive rate \(Mbps\)'
$tx      = Field 'Transmit rate \(Mbps\)'
$band    = Field 'Band'

Write-Host ("  SSID          : {0}" -f $ssid)
Write-Host ("  signal        : {0}" -f $signal)
Write-Host ("  radio type    : {0}" -f $radio)
Write-Host ("  channel       : {0}" -f $channel)
if ($band) { Write-Host ("  band          : {0}" -f $band) }
Write-Host ("  receive rate  : {0} Mbps" -f $rx)
Write-Host ("  transmit rate : {0} Mbps" -f $tx)

# Channel is the reliable tell. The "Band" field only exists on newer Windows builds, so it is
# used when present and the channel decides otherwise.
$ch = 0
[void][int]::TryParse($channel, [ref]$ch)
$bandName =
    if ($band)            { $band }
    elseif ($ch -eq 0)    { 'unknown' }
    elseif ($ch -le 14)   { '2.4 GHz' }
    elseif ($ch -le 233)  { '5 GHz' }
    else                  { 'unknown' }

Write-Host ''
Write-Host '== power saving ==' -ForegroundColor Cyan
# The wifi adapter's power policy. This is the setting that produces "fine, then a spike, then
# fine" - the shape people describe as random lag.
$ps = powercfg /query SCHEME_CURRENT SUB_NONE 2>&1
$psLine = powercfg /getactivescheme 2>&1
Write-Host ("  active power plan : {0}" -f (($psLine -replace '.*\(', '') -replace '\)', ''))
try {
    $wifiPm = Get-NetAdapter -Physical | Where-Object { $_.MediaType -eq 'Native 802.11' -or $_.PhysicalMediaType -match '802.11' } | Select-Object -First 1
    if ($wifiPm) {
        $pm = Get-NetAdapterPowerManagement -Name $wifiPm.Name -ErrorAction Stop
        Write-Host ("  adapter           : {0}" -f $wifiPm.InterfaceDescription)
        Write-Host ("  may sleep to save power : {0}" -f $pm.AllowComputerToTurnOffDevice)
    }
} catch {
    Write-Host '  (adapter power settings not readable here)' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '== verdict ==' -ForegroundColor Cyan

$problems = @()

if ($bandName -eq '2.4 GHz') {
    $problems += "You are on 2.4 GHz (channel $channel). This alone explains lag that distance does not. Connect to the 5 GHz SSID."
} elseif ($bandName -eq 'unknown') {
    $problems += "Could not determine the band from channel '$channel' - read the radio type above."
}

$rxNum = 0
[void][int]::TryParse(($rx -replace '[^\d].*$',''), [ref]$rxNum)
if ($rxNum -gt 0 -and $rxNum -lt 200) {
    $problems += "Receive rate is only $rxNum Mbps. That is a weak link for streaming even if speed tests look fine - streaming needs steadiness, not peak."
}

if ($signal -and ($signal -replace '%','') -as [int]) {
    $sig = [int]($signal -replace '%','')
    if ($sig -lt 70) { $problems += "Signal is $signal. Under ~70% the radio starts dropping to slower, more robust rates." }
}

if ($problems.Count -eq 0) {
    Write-Host ("  Wifi looks healthy: {0}, {1} Mbps receive, signal {2}." -f $bandName, $rx, $signal) -ForegroundColor Green
    Write-Host '  If it still lags, the radio is not the cause - check the Moonlight performance'
    Write-Host '  overlay (network latency vs decode time) to see which side is actually slow.'
    exit 0
}

foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Yellow }
Write-Host ''
Write-Host '  Also worth doing regardless, on a handheld or laptop:' -ForegroundColor DarkGray
Write-Host '    - stream plugged in; battery profiles throttle the radio' -ForegroundColor DarkGray
Write-Host '    - Device Manager > wifi adapter > Power Management >' -ForegroundColor DarkGray
Write-Host '      uncheck "Allow the computer to turn off this device to save power"' -ForegroundColor DarkGray
exit 1
