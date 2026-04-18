#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the MultiSeat Service as a Windows Service.
.DESCRIPTION
    Publishes the MultiSeat.Service project, copies the InputHook DLL,
    creates the required data directories, and registers the Windows service.
.PARAMETER Uninstall
    Remove the service and clean up.
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$ServiceName = "MultiSeatService"
$DisplayName = "MultiSeat Multi-Seat Streaming Service"
$Description = "Manages multi-seat headless game streaming sessions with isolated input, audio, and display."
$InstallDir = "C:\Program Files\MultiSeat"
$DataDir = "C:\ProgramData\MultiSeat"
$ProjectDir = Join-Path $PSScriptRoot "..\src\MultiSeat.Service"
$InputHookBuild = Join-Path $PSScriptRoot "..\src\MultiSeat.InputHook\build\Release\MultiSeatInputHook.dll"

function Write-Step($msg) { Write-Host "[MultiSeat] $msg" -ForegroundColor Cyan }

# -- Uninstall -------------------------------------------------------
if ($Uninstall) {
    Write-Step "Stopping service..."
    $svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq 'Running') {
            Stop-Service $ServiceName -Force
            Write-Step "Service stopped"
        }
        sc.exe delete $ServiceName | Out-Null
        Write-Step "Service removed"
    } else {
        Write-Step "Service not found -- nothing to remove"
    }
    Write-Host "`nTo fully clean up, manually delete:" -ForegroundColor Yellow
    Write-Host "  $InstallDir"
    Write-Host "  $DataDir"
    return
}

# -- Prerequisites check ---------------------------------------------
Write-Step "Checking prerequisites..."
$missing = @()
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { $missing += ".NET SDK" }
if (!(Test-Path "HKLM:\SOFTWARE\Nefarius Software Solutions\HidHide")) {
    Write-Warning "HidHide not detected -- controller hiding will be unavailable"
}
if ($missing.Count -gt 0) {
    throw "Missing prerequisites: $($missing -join ', ')"
}

# -- RDP configuration -----------------------------------------------
# These settings are also applied by prerequisites\install-prerequisites.ps1.
# Re-applying here ensures a fresh service deploy always has the correct RDP
# configuration, even if the prereq script was run before these settings existed
# or was skipped entirely.

Write-Step "Verifying RDP configuration..."

# Enable Remote Desktop (fDenyTSConnections = 0)
$tsKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server"
if ((Get-ItemProperty $tsKey -Name "fDenyTSConnections" -ErrorAction SilentlyContinue).fDenyTSConnections -ne 0) {
    Set-ItemProperty $tsKey -Name "fDenyTSConnections" -Value 0
    Enable-NetFirewallRule -DisplayGroup "Remote Desktop" -ErrorAction SilentlyContinue
    Write-Host "  Applied: Remote Desktop enabled" -ForegroundColor Green
} else {
    Write-Host "  OK: Remote Desktop enabled" -ForegroundColor DarkGray
}

# Disable NLA on the RDP listener (UserAuthentication = 0).
# Required so mstsc can connect to 127.0.0.2 without a pre-auth certificate exchange.
$rdpTcpKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp"
if ((Get-ItemProperty $rdpTcpKey -Name "UserAuthentication" -ErrorAction SilentlyContinue).UserAuthentication -ne 0) {
    Set-ItemProperty $rdpTcpKey -Name "UserAuthentication" -Value 0
    Set-ItemProperty $rdpTcpKey -Name "SecurityLayer"      -Value 1
    Write-Host "  Applied: NLA disabled on RDP listener" -ForegroundColor Green
} else {
    Write-Host "  OK: NLA disabled on RDP listener" -ForegroundColor DarkGray
}

# Suppress the RDP client certificate trust dialog (AuthenticationLevel = 0 machine policy).
# MultiSeat launches mstsc via CreateProcessAsUser with no interactive user to click dialogs.
# This is safe because MultiSeat only ever connects to 127.0.0.2 (local loopback).
$rdpClientKey = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"
if (-not (Test-Path $rdpClientKey)) { New-Item $rdpClientKey -Force | Out-Null }
if ((Get-ItemProperty $rdpClientKey -Name "AuthenticationLevel" -ErrorAction SilentlyContinue).AuthenticationLevel -ne 0) {
    Set-ItemProperty $rdpClientKey -Name "AuthenticationLevel" -Value 0 -Type DWord
    Write-Host "  Applied: RDP client cert dialog suppressed" -ForegroundColor Green
} else {
    Write-Host "  OK: RDP client cert dialog suppressed" -ForegroundColor DarkGray
}

# Allow unsigned .rdp files without showing "publisher cannot be identified" warning.
# MultiSeat uses a connection.rdp in C:\ProgramData\MultiSeat\ which is not digitally signed.
if ((Get-ItemProperty $rdpClientKey -Name "AllowUnsignedFiles" -ErrorAction SilentlyContinue).AllowUnsignedFiles -ne 1) {
    Set-ItemProperty $rdpClientKey -Name "AllowUnsignedFiles" -Value 1 -Type DWord
    Write-Host "  Applied: unsigned .rdp file warning suppressed" -ForegroundColor Green
} else {
    Write-Host "  OK: unsigned .rdp file warning suppressed" -ForegroundColor DarkGray
}

# -- SudoVDA check ---------------------------------------------------
# SudoVDA is required for per-seat virtual display isolation.
# Without it, Apollo captures the primary physical display and all seats
# share the same view. Warn loudly here; the prereq script installs it.
Write-Step "Checking SudoVDA (virtual display driver)..."
$sudovdaDevice = Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -match "VDD|Virtual Display|SudoVDA|MTT" }
if ($sudovdaDevice) {
    Write-Host "  OK: SudoVDA detected ($($sudovdaDevice.FriendlyName | Select-Object -First 1))" -ForegroundColor DarkGray
} else {
    Write-Host ""
    Write-Host "  *** WARNING: SudoVDA virtual display driver is NOT installed. ***" -ForegroundColor Yellow
    Write-Host "  Seats will launch in degraded mode — Apollo will capture the" -ForegroundColor Yellow
    Write-Host "  physical display instead of an isolated virtual display." -ForegroundColor Yellow
    Write-Host "  Run prerequisites\install-prerequisites.ps1 to install SudoVDA." -ForegroundColor Yellow
    Write-Host ""
}

# -- VirtualDisplayDriver service start ----------------------------------------
# The SudoVDA kernel driver registers a "VirtualDisplayDriver" service that must
# be running for virtual displays to appear. It is set to auto-start at boot, but
# after driver installation (before first reboot) or on fresh deploys it may be
# stopped. Start it here so the service can enumerate displays immediately.
$vddSvc = Get-Service -Name "VirtualDisplayDriver" -ErrorAction SilentlyContinue
if ($vddSvc) {
    if ($vddSvc.Status -eq 'Running') {
        Write-Host "  OK: VirtualDisplayDriver running" -ForegroundColor DarkGray
    } else {
        Write-Step "Starting VirtualDisplayDriver service (virtual displays)..."
        try {
            Start-Service "VirtualDisplayDriver" -ErrorAction Stop
            Write-Host "  OK: VirtualDisplayDriver started" -ForegroundColor Green
        } catch {
            Write-Host "  WARNING: Could not start VirtualDisplayDriver: $_" -ForegroundColor Yellow
            Write-Host "  Virtual displays may be unavailable. Reboot may be required." -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  NOTE: VirtualDisplayDriver service not found — " `
        "run prerequisites\install-prerequisites.ps1 to install SudoVDA" -ForegroundColor Yellow
}

# -- VoiceMeeter start ---------------------------------------------------------
# VoiceMeeter Potato must be running for its virtual audio devices (VoiceMeeter
# Input, Aux Input, VAIO3) to route audio. It is registered in HKLM\Run so it
# auto-starts at user login, but may not be running if the session just booted or
# if the service is being re-deployed without a user login in between.
# Start it here (from the admin session, NOT Session 0) before the MultiSeat
# service begins — the service's AudioRouter will also try to start it, but
# launching a GUI app from SYSTEM/Session 0 is unreliable.
$vmExe = $null
foreach ($candidate in @(
    "C:\Program Files\VB\Voicemeeter\voicemeeterpro.exe",
    "C:\Program Files (x86)\VB\Voicemeeter\voicemeeterpro.exe"
)) {
    if (Test-Path $candidate) { $vmExe = $candidate; break }
}
if ($vmExe) {
    $vmRunning = Get-Process -Name "voicemeeterpro" -ErrorAction SilentlyContinue
    if ($vmRunning) {
        Write-Host "  OK: VoiceMeeter Potato already running" -ForegroundColor DarkGray
    } else {
        Write-Step "Starting VoiceMeeter Potato (audio routing)..."
        Start-Process $vmExe -WindowStyle Minimized
        Start-Sleep -Seconds 3
        $vmRunning = Get-Process -Name "voicemeeterpro" -ErrorAction SilentlyContinue
        if ($vmRunning) {
            Write-Host "  OK: VoiceMeeter Potato started" -ForegroundColor Green
        } else {
            Write-Host "  WARNING: VoiceMeeter may not have started. Check manually." -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  NOTE: VoiceMeeter not found — audio isolation unavailable. " `
        "Run prerequisites\install-prerequisites.ps1." -ForegroundColor Yellow
}

# -- Disable default ApolloService (prevents port 47984 conflicts) ------------
# Apollo ships as an auto-start Windows service that runs sunshine.exe on port 47984.
# MultiSeat manages its own per-seat Apollo instances; the default service must be
# stopped and set to Manual start so it does not compete after reboot.
$apolloSvc = Get-Service -Name "ApolloService" -ErrorAction SilentlyContinue
if ($apolloSvc) {
    if ($apolloSvc.Status -eq 'Running') {
        Write-Step "Stopping default ApolloService (port 47984 conflict with MultiSeat)..."
        Stop-Service "ApolloService" -Force
        Write-Host "  OK: ApolloService stopped" -ForegroundColor Green
    }
    Set-Service "ApolloService" -StartupType Manual
    Write-Host "  OK: ApolloService set to Manual start (MultiSeat manages all Apollo instances)" -ForegroundColor Green
} else {
    Write-Host "  OK: ApolloService not found" -ForegroundColor DarkGray
}

# -- Stop service before publish so its DLLs are not locked ----------
$svcBeforePublish = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($svcBeforePublish -and $svcBeforePublish.Status -eq 'Running') {
    Write-Step "Stopping service before publish..."
    Stop-Service $ServiceName -Force
    try { $svcBeforePublish.WaitForStatus('Stopped', (New-TimeSpan -Seconds 15)) }
    catch { Write-Warning "SCM stop timed out -- will force-kill the process" }
}

# Kill any surviving process running the service exe by path
# (handles cases where the process outlives the SCM status change)
$serviceExe = Join-Path $InstallDir "MultiSeat.Service.exe"
$lingering = Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $serviceExe }
foreach ($proc in $lingering) {
    Write-Step "Force-killing lingering process PID $($proc.Id)..."
    $proc | Stop-Process -Force
    $proc.WaitForExit(10000)
}

# Brief pause to let the OS release all file handles before publish
Start-Sleep -Milliseconds 500
Write-Step "Service stopped and file handles released"

# -- Publish ----------------------------------------------------------
Write-Step "Publishing MultiSeat.Service..."
dotnet publish $ProjectDir -c Release -o "$InstallDir" --no-self-contained 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

# -- Copy InputHook DLL -----------------------------------------------
if (Test-Path $InputHookBuild) {
    Copy-Item $InputHookBuild "$InstallDir\MultiSeatInputHook.dll" -Force
    Write-Step "Copied MultiSeatInputHook.dll"
} else {
    Write-Warning "MultiSeatInputHook.dll not found at $InputHookBuild -- keyboard/mouse isolation unavailable"
}

# -- Build and deploy Dashboard ---------------------------------------
$DashboardDir = Join-Path $PSScriptRoot "..\src\MultiSeat.Dashboard"
if (Test-Path (Join-Path $DashboardDir "package.json")) {
    Write-Step "Building dashboard..."
    Push-Location $DashboardDir
    try {
        # Install npm dependencies if node_modules is missing or incomplete
        $nodeModules = Join-Path $DashboardDir "node_modules"
        $viteMarker  = Join-Path $nodeModules "vite\bin\vite.js"
        if (-not (Test-Path $viteMarker)) {
            Write-Step "Installing dashboard npm dependencies..."
            $nodeExe = (Get-Command node -ErrorAction SilentlyContinue)?.Source
            if (-not $nodeExe) { $nodeExe = "C:\Program Files\nodejs\node.exe" }
            if (-not (Test-Path $nodeExe)) { throw "node.exe not found. Install Node.js first." }
            $result = Start-Process $nodeExe -ArgumentList "install.cjs" `
                -Wait -NoNewWindow -PassThru -WorkingDirectory $DashboardDir
            if ($result.ExitCode -ne 0) { throw "npm install failed (exit $($result.ExitCode))" }
        }

        & cmd /c "$DashboardDir\build.bat"
        if ($LASTEXITCODE -ne 0) { throw "Dashboard build failed" }
        $distDir = Join-Path $DashboardDir "dist"
        if (Test-Path $distDir) {
            $wwwroot = Join-Path $InstallDir "wwwroot"
            if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
            Copy-Item $distDir $wwwroot -Recurse
            Write-Step "Dashboard deployed to $wwwroot"
        } else {
            Write-Warning "Dashboard dist/ not found after build"
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Warning "Dashboard not found -- skipping"
}

# -- Create data directories ------------------------------------------
@("$DataDir", "$DataDir\apollo", "$DataDir\logs") | ForEach-Object {
    if (!(Test-Path $_)) {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
        Write-Step "Created $_"
    }
}

# -- Register Windows service ----------------------------------------
$exePath = Join-Path $InstallDir "MultiSeat.Service.exe"

$existing = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Service already exists -- stopping and updating..."
    if ($existing.Status -eq 'Running') {
        Stop-Service $ServiceName -Force
    }
    sc.exe config $ServiceName binPath= "`"$exePath`"" start= auto | Out-Null
} else {
    Write-Step "Creating Windows service..."
    sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null
    sc.exe description $ServiceName "`"$Description`"" | Out-Null
}

# Configure service recovery (restart on failure)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
Write-Step "Configured automatic restart on failure"

# -- Start ------------------------------------------------------------
Write-Step "Starting service..."
Start-Service $ServiceName
$svc = Get-Service $ServiceName
Write-Host "`n[MultiSeat] Service installed and $($svc.Status)!" -ForegroundColor Green
Write-Host "  Dashboard: http://localhost:9550"
Write-Host "  Logs:      $DataDir\logs"
Write-Host "  Config:    $InstallDir\appsettings.json"
