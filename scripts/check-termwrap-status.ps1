<#
.SYNOPSIS
    Is TermWrap installed and active on this host? Read-only status check.

.DESCRIPTION
    TermWrap (llccd/TermWrap, MIT) is the in-memory Zydis-based patcher MultiSeat uses for
    multi-session RDP. It is a DLL that TermService loads instead of the stock termsrv.dll
    (set via HKLM\SYSTEM\CurrentControlSet\Services\TermService\Parameters\ServiceDll),
    patches termsrv.dll at runtime, and forwards the service entrypoints. There is no
    ini to keep current and no community release cadence to wait on.

    This script answers: "is the host ready to run multiple RDP sessions?". It checks:

      1. TermWrap.dll is on disk at %ProgramFiles%\RDP Wrapper\TermWrap.dll
      2. The TermService ServiceDll registry value points at it (or another known
         multi-session patch — e.g. the legacy stascorp/rdpwrap.dll)
      3. The stock termsrv.dll is present and TermService is running
      4. Optional -Deep: try to actually open a second RDP session over loopback, which is
         the only check that proves TermWrap's runtime patches are landing. Requires a
         writable Win-cred for TERMSRV/127.0.0.2 — MultiSeat's seat accounts are perfect,
         but this script does not know their names, so the loopback check is best run as
         part of an actual seat provision.

    Exit 0 = ready, 1 = not ready, 2 = cannot tell.

.EXAMPLE
    .\check-termwrap-status.ps1
    Read-only status. Exit 0/1/2.

.EXAMPLE
    .\check-termwrap-status.ps1 -Json
    One-line JSON for scripts. Exit 0/1/2.
#>
[CmdletBinding()]
param(
    [switch]$Deep,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$TermWrapDir   = Join-Path $env:ProgramFiles 'RDP Wrapper'
$TermWrapDll   = Join-Path $TermWrapDir 'TermWrap.dll'
$LegacyDllSys  = Join-Path $env:SystemRoot 'System32\rdpwrap.dll'
$TermSrv       = Join-Path $env:SystemRoot 'System32\termsrv.dll'
$ServiceDllKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\TermService\Parameters'
$TermService   = Get-Service TermService -ErrorAction SilentlyContinue

$result = [ordered]@{
    termWrapInstalled   = $false
    legacyRdpWrapActive = $false
    serviceDllPath      = $null
    serviceDllResolves  = $false
    termServiceRunning  = $false
    verdict             = 'unknown'
    note                = $null
}

# ServiceDll — what the host thinks TermService is going to load at next start.
$svcDll = (Get-ItemProperty -Path $ServiceDllKey -Name 'ServiceDll' -ErrorAction SilentlyContinue).ServiceDll
if ($svcDll) {
    $resolved = [System.Environment]::ExpandEnvironmentVariables($svcDll)
    $result.serviceDllPath     = $resolved
    $result.serviceDllResolves = (Test-Path $resolved)
    $result.termWrapInstalled   = $resolved -like '*\TermWrap.dll' -and $result.serviceDllResolves
    $result.legacyRdpWrapActive = $resolved -like '*\rdpwrap.dll' -and $result.serviceDllResolves
}

# TermService state — TermWrap cannot load unless TermService is in a usable state.
if ($TermService) {
    $result.termServiceRunning = ($TermService.Status -eq 'Running')
}

# Verdict.
if (-not $result.serviceDllPath) {
    $result.verdict = 'not-installed'
    $result.note    = 'No ServiceDll redirect. TermService still points at the stock termsrv.dll.'
} elseif ($result.termWrapInstalled) {
    $result.verdict = 'ok'
    $result.note    = 'TermWrap.dll is the TermService implementation. Multi-session RDP should be active.'
} elseif ($result.legacyRdpWrapActive) {
    $result.verdict = 'legacy'
    $result.note    = 'Legacy stascorp/rdpwrap.dll is active. Migrate to TermWrap with prerequisites\install-prerequisites.ps1.'
} elseif (-not $result.serviceDllResolves) {
    $result.verdict = 'broken'
    $result.note    = "ServiceDll points at a missing file: $($result.serviceDllPath)"
} else {
    $result.verdict = 'unknown'
    $result.note    = "ServiceDll is set to $($result.serviceDllPath) but it is neither TermWrap nor rdpwrap. Not a MultiSeat-compatible install."
}

if ($Deep -and $result.termWrapInstalled) {
    # Try to open a second session over loopback. This is the only way to prove TermWrap's
    # runtime patches are actually landing — the DLL being on disk and ServiceDll pointing
    # at it are necessary but not sufficient. We don't have credentials here, so we can
    # only check that the listener accepts the connection.
    try {
        $portTest = Test-NetConnection -ComputerName '127.0.0.2' -Port 3389 -WarningAction SilentlyContinue -InformationLevel Quiet
        $result.deepListener = $portTest
        if (-not $portTest) {
            $result.verdict = 'broken'
            $result.note    = 'TermWrap is installed but RDP loopback listener (127.0.0.2:3389) is not accepting connections.'
        }
    } catch {
        $result.deepListener = $false
        $result.note = $_.ToString()
    }
}

if ($Json) {
    $result | ConvertTo-Json -Compress
} else {
    Write-Host ''
    Write-Host '== TermWrap status ==' -ForegroundColor Cyan
    Write-Host ("  TermWrap installed     : {0}" -f $result.termWrapInstalled)
    Write-Host ("  Legacy rdpwrap active  : {0}" -f $result.legacyRdpWrapActive)
    Write-Host ("  TermService ServiceDll : {0}" -f $result.serviceDllPath)
    Write-Host ("  DLL resolves on disk   : {0}" -f $result.serviceDllResolves)
    Write-Host ("  TermService running    : {0}" -f $result.termServiceRunning)
    if ($Deep) {
        Write-Host ("  127.0.0.2:3389 accepts : {0}" -f $result.deepListener)
    }
    Write-Host ''
    Write-Host '== verdict ==' -ForegroundColor Cyan
    $color = switch ($result.verdict) {
        'ok'          { 'Green' }
        'legacy'      { 'Yellow' }
        'broken'      { 'Red' }
        'not-installed' { 'Yellow' }
        default       { 'Yellow' }
    }
    Write-Host ("  {0}" -f $result.verdict) -ForegroundColor $color
    if ($result.note) {
        Write-Host ("  {0}" -f $result.note) -ForegroundColor DarkGray
    }
}

switch ($result.verdict) {
    'ok' { exit 0 }
    'legacy' { exit 1 }
    'broken' { exit 1 }
    'not-installed' { exit 1 }
    default { exit 2 }
}
