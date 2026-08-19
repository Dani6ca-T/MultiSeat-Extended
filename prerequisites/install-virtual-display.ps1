#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Virtual Display Driver (itsmikethetech) for headless operation.
.DESCRIPTION
    Downloads and installs a persistent IddCx virtual display that appears at boot
    without requiring any streaming client (Moonlight, Parsec, etc.) to be connected.

    This is required for headless machines where AnyDesk or RDP needs a display
    to connect to before any seat is provisioned. SudoVDA (Apollo's driver) only
    creates displays during active streaming — this driver fills the gap.

    The driver creates one persistent virtual monitor at 1920x1080@60Hz by default.
    Resolution can be changed afterwards in Windows Display Settings.

.PARAMETER SkipDownload
    Skip downloading; only install if the zip is already in this folder.
.PARAMETER Resolution
    Resolution preset to configure. Options: 1080p (default), 1440p, 4k.
#>
param(
    [switch]$SkipDownload,
    [ValidateSet("1080p","1440p","4k")]
    [string]$Resolution = "1080p"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step($msg) { Write-Host "`n[VDD] $msg" -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host "  OK: $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  WARNING: $msg" -ForegroundColor Yellow }

# ── Already installed? ───────────────────────────────────────────────────────
Write-Step "Checking for existing Virtual Display Driver..."
$existing = Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object FriendlyName -match "Virtual Display Driver|IddSample|MTT"

# Exclude Apollo's SudoVDA — we want the standalone always-on driver
$existing = $existing | Where-Object FriendlyName -notmatch "SudoMaker"

if ($existing) {
    Write-OK "Virtual Display Driver already installed: $($existing.FriendlyName | Select-Object -First 1)"
    Write-Host "  If you need to reinstall, remove it from Device Manager first." -ForegroundColor DarkGray
    exit 0
}

# ── Download ─────────────────────────────────────────────────────────────────
Write-Step "Downloading Virtual Display Driver..."

# GitHub latest release API — gets the most recent release asset
$releaseApi  = "https://api.github.com/repos/itsmikethetech/Virtual-Display-Driver/releases/latest"
$zipDest     = Join-Path $ScriptDir "VirtualDisplayDriver.zip"

if (Test-Path $zipDest) {
    Write-Host "  Found locally: VirtualDisplayDriver.zip" -ForegroundColor DarkGray
} elseif ($SkipDownload) {
    Write-Warn "-SkipDownload specified and VirtualDisplayDriver.zip not found. Aborting."
    exit 1
} else {
    try {
        Write-Host "  Fetching latest release info from GitHub..." -ForegroundColor White
        $ProgressPreference = 'SilentlyContinue'
        $release  = Invoke-RestMethod -Uri $releaseApi -UseBasicParsing -Headers @{ "User-Agent" = "MultiSeat-Prereq" }
        $asset    = $release.assets | Where-Object { $_.name -match "\.zip$" } | Select-Object -First 1

        if (-not $asset) {
            Write-Warn "No zip asset found in latest release. Check the GitHub page manually."
            exit 1
        }

        Write-Host "  Downloading $($asset.name) v$($release.tag_name)..." -ForegroundColor White
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipDest -UseBasicParsing
        $ProgressPreference = 'Continue'
        Write-OK "Downloaded $($asset.name)"
    } catch {
        Write-Warn "Download failed: $_"
        Write-Host ""
        Write-Host "  Manual install steps:" -ForegroundColor Yellow
        Write-Host "  1. Go to: https://github.com/itsmikethetech/Virtual-Display-Driver/releases" -ForegroundColor Yellow
        Write-Host "  2. Download the latest zip release" -ForegroundColor Yellow
        Write-Host "  3. Save it as: $zipDest" -ForegroundColor Yellow
        Write-Host "  4. Re-run this script with -SkipDownload" -ForegroundColor Yellow
        exit 1
    }
}

# ── Extract ───────────────────────────────────────────────────────────────────
Write-Step "Extracting..."
$extractDir = Join-Path $env:TEMP "MultiSeat_VDD_Install"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
Expand-Archive $zipDest -DestinationPath $extractDir -Force

# Find the INF file — it may be in a subdirectory
$inf = Get-ChildItem $extractDir -Recurse -Filter "*.inf" |
    Where-Object { $_.Name -match "vdd|virtualdisplay|iddsample|MTT" -or $_.DirectoryName -match "driver" } |
    Select-Object -First 1

if (-not $inf) {
    # Fallback: any INF in the zip
    $inf = Get-ChildItem $extractDir -Recurse -Filter "*.inf" | Select-Object -First 1
}

if (-not $inf) {
    Write-Warn "No INF file found in zip. Contents:"
    Get-ChildItem $extractDir -Recurse | ForEach-Object { Write-Host "    $($_.FullName)" -ForegroundColor DarkGray }
    exit 1
}

Write-OK "Found driver INF: $($inf.FullName)"

# ── Configure resolution ──────────────────────────────────────────────────────
# The driver reads resolution from an options file or registry key depending on version.
# Write preferred resolution to both common locations.
$resMap = @{
    "1080p" = @{ W = 1920; H = 1080; R = 60 }
    "1440p" = @{ W = 2560; H = 1440; R = 60 }
    "4k"    = @{ W = 3840; H = 2160; R = 60 }
}
$res = $resMap[$Resolution]

Write-Step "Setting resolution: $Resolution ($($res.W)x$($res.H)@$($res.R)Hz)..."

# VDD option file (some versions read this from driver directory)
$optFile = Join-Path (Split-Path $inf.FullName) "option.txt"
if (-not (Test-Path $optFile)) {
    $optFile = Join-Path $extractDir "option.txt"
}
"$($res.W),$($res.H),$($res.R)" | Set-Content $optFile -Encoding ASCII
Write-OK "Wrote option.txt: $($res.W),$($res.H),$($res.R)"

# Registry fallback (newer versions use this)
$regPath = "HKLM:\SOFTWARE\VirtualDisplayDriver"
if (-not (Test-Path $regPath)) { New-Item $regPath -Force | Out-Null }
Set-ItemProperty $regPath -Name "Width"       -Value $res.W  -Type DWord
Set-ItemProperty $regPath -Name "Height"      -Value $res.H  -Type DWord
Set-ItemProperty $regPath -Name "RefreshRate" -Value $res.R  -Type DWord
Write-OK "Wrote registry: HKLM:\SOFTWARE\VirtualDisplayDriver"

# ── Install driver via pnputil ────────────────────────────────────────────────
Write-Step "Installing driver via pnputil..."
$pnpResult = & pnputil.exe /add-driver "$($inf.FullName)" /install 2>&1
Write-Host ($pnpResult | Out-String).Trim() -ForegroundColor DarkGray

if ($LASTEXITCODE -ne 0) {
    Write-Warn "pnputil /add-driver returned exit code $LASTEXITCODE"
    Write-Host "  Trying devcon as fallback..." -ForegroundColor Yellow

    # Try devcon if available
    $devcon = Get-Command devcon.exe -ErrorAction SilentlyContinue
    if ($devcon) {
        & devcon.exe install "$($inf.FullName)" 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    } else {
        Write-Warn "devcon not found. Manual install: Device Manager > Add legacy hardware > from disk > $($inf.FullName)"
        exit 1
    }
}

# ── Create the virtual device node ────────────────────────────────────────────
# The driver INF declares a root-enumerated device. Plug in the device node
# so Windows instantiates the driver and the display appears.
Write-Step "Creating virtual device node..."

# Extract hardware ID from INF
$hwid = $null
Get-Content $inf.FullName | ForEach-Object {
    if ($_ -match "^\s*%.*%\s*=\s*\S+\s*,\s*(\S+)") {
        $hwid = $Matches[1].Trim()
    }
}

if ($hwid) {
    Write-Host "  Hardware ID: $hwid" -ForegroundColor DarkGray
    $enum = & pnputil.exe /scan-devices 2>&1
    Write-Host ($enum | Out-String).Trim() -ForegroundColor DarkGray
} else {
    Write-Warn "Could not parse hardware ID from INF — device node may need manual creation."
    Write-Warn "In Device Manager: Action > Add legacy hardware > Install the hardware manually > Display adapters > Have disk > $($inf.FullName)"
}

# ── Verify ────────────────────────────────────────────────────────────────────
Write-Step "Verifying installation..."
Start-Sleep -Seconds 2

$installed = Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object FriendlyName -notmatch "SudoMaker" |
    Where-Object FriendlyName -match "Virtual Display|IddSample|MTT|VDD"

if ($installed) {
    Write-OK "Virtual Display Driver installed: $($installed.FriendlyName | Select-Object -First 1)"
    Write-Host ""
    Write-Host "  The virtual display is now persistent — it will appear at every boot" -ForegroundColor Green
    Write-Host "  without requiring Moonlight or any streaming client to connect." -ForegroundColor Green
    Write-Host "  AnyDesk and RDP will be able to connect to it immediately after boot." -ForegroundColor Green
} else {
    Write-Warn "Driver files installed but device node not yet visible."
    Write-Host ""
    Write-Host "  This is normal — a reboot may be required for the display to appear." -ForegroundColor Yellow
    Write-Host "  After reboot, check Device Manager > Display adapters for the new device." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Driver INF location: $($inf.FullName)" -ForegroundColor DarkGray
Write-Host "  To change resolution: re-run with -Resolution 1440p or -Resolution 4k" -ForegroundColor DarkGray
Write-Host ""
