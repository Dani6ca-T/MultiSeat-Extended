<#
.SYNOPSIS
    Is this host's termsrv.dll covered by the installed rdpwrap.ini? Generate the section if not.

.DESCRIPTION
    RDPWrap patches termsrv.dll at fixed byte offsets, looked up in rdpwrap.ini by the DLL's
    version. A Windows update replaces termsrv.dll, the offsets move, the ini no longer has a
    matching section, and multi-session RDP stops - which takes MultiSeat with it, because every
    seat is an RDP session.

    Today the remedy is to wait for the community ini (sebaxakerhtc/rdpwrap.ini) to add the new
    build. That is a dependency on someone else's release cadence, sitting directly in front of the
    thing this project cannot run without.

    This answers two questions locally:

      1. IS THE CURRENT BUILD COVERED? Both [version] and [version-SLInit] must be present. A
         section pair that only half exists is worse than none, because RDPWrap will patch with
         what it finds.

      2. IF NOT, WHAT ARE THE RIGHT OFFSETS? -Generate runs llccd/RDPWrapOffsetFinder (MIT) against
         the local termsrv.dll and writes a section you can review. -Apply merges it into the
         installed ini, after backing the ini up, and restarts TermService.

    WARNING - READ THE VERSION CORRECTLY - this is a real trap and it nearly fooled the author.
    termsrv.dll's StringFileInfo and its VS_FIXEDFILEINFO DISAGREE. Measured on the reference host
    on 2026-09-01:

        StringFileInfo FileVersion : 10.0.26100.8115   <- what .VersionInfo.FileVersion returns
        VS_FIXEDFILEINFO (raw)     : 10.0.26100.8972   <- what RDPWrap actually keys on

    Both sections happened to exist in the ini that day, so checking the wrong one still said
    "covered" - a right answer for the wrong reason, which would have gone unnoticed until a build
    where only one of them existed. This script reads FileVersionRaw.

.PARAMETER Generate
    Run the offset finder and print/write the section for the current termsrv.dll. Downloads the
    tool to a temp folder if it is not already there. Does not modify the installed ini.

.PARAMETER Apply
    Implies -Generate. Appends the generated sections to the installed rdpwrap.ini (after a
    timestamped backup) and restarts TermService.

.EXAMPLE
    .\check-rdpwrap-offsets.ps1
    Read-only. Exit 0 = covered, 1 = not covered, 2 = could not tell.

.EXAMPLE
    .\check-rdpwrap-offsets.ps1 -Apply
    Fix a host whose RDP broke after a Windows update, without waiting for the community ini.
#>
[CmdletBinding()]
param(
    [switch]$Generate,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
if ($Apply) { $Generate = $true }

$IniPath   = 'C:\Program Files\RDP Wrapper\rdpwrap.ini'
$TermSrv   = Join-Path $env:SystemRoot 'System32\termsrv.dll'
$ToolUrl   = 'https://github.com/llccd/RDPWrapOffsetFinder/releases/download/v1.0/RDPWrapOffsetFinder-1.0.zip'

Write-Host ''
Write-Host '== rdpwrap offset coverage ==' -ForegroundColor Cyan

if (-not (Test-Path $TermSrv)) {
    Write-Host "  termsrv.dll not found at $TermSrv" -ForegroundColor Red
    exit 2
}

$info = (Get-Item $TermSrv).VersionInfo

# FileVersionRaw, NOT FileVersion. See the warning in the help above.
$raw = $info.FileVersionRaw
if (-not $raw) {
    Write-Host '  Could not read VS_FIXEDFILEINFO from termsrv.dll.' -ForegroundColor Yellow
    Write-Host '  Refusing to fall back to the version STRING, which is known to disagree.' -ForegroundColor Yellow
    exit 2
}
$version = '{0}.{1}.{2}.{3}' -f $raw.Major, $raw.Minor, $raw.Build, $raw.Revision

Write-Host ("  termsrv.dll version (raw)   : {0}" -f $version)
Write-Host ("  version STRING says         : {0}" -f ($info.FileVersion -replace '\s.*',''))
if (($info.FileVersion -replace '\s.*','') -ne $version) {
    Write-Host '  NOTE: those disagree. The raw one is what RDPWrap uses; the string lies.' -ForegroundColor DarkGray
}
Write-Host ("  termsrv.dll modified        : {0}" -f (Get-Item $TermSrv).LastWriteTime)

if (-not (Test-Path $IniPath)) {
    Write-Host "  rdpwrap.ini not found at $IniPath - is RDPWrap installed?" -ForegroundColor Yellow
    exit 2
}

$ini = Get-Content $IniPath
$hasMain   = [bool]($ini | Select-String -SimpleMatch -Pattern "[$version]"        -Quiet)
$hasSlInit = [bool]($ini | Select-String -SimpleMatch -Pattern "[$version-SLInit]" -Quiet)

Write-Host ''
Write-Host ("  [{0}]         : {1}" -f $version, $(if ($hasMain)   { 'present' } else { 'ABSENT' }))
Write-Host ("  [{0}-SLInit]  : {1}" -f $version, $(if ($hasSlInit) { 'present' } else { 'ABSENT' }))

$covered = $hasMain -and $hasSlInit

Write-Host ''
if ($covered -and -not $Generate) {
    Write-Host '== verdict ==' -ForegroundColor Cyan
    Write-Host '  Covered. This build has both sections, so RDPWrap can patch it.' -ForegroundColor Green
    Write-Host '  (That the sections exist does not prove the offsets are correct - if multi-session' -ForegroundColor DarkGray
    Write-Host '   is broken anyway, re-run with -Generate and compare.)' -ForegroundColor DarkGray
    exit 0
}

if (-not $covered) {
    Write-Host '== verdict ==' -ForegroundColor Cyan
    Write-Host '  NOT covered. Multi-session RDP will not work on this build until the ini has both' -ForegroundColor Yellow
    Write-Host '  sections - which means every MultiSeat seat is down.' -ForegroundColor Yellow
    if (-not $Generate) {
        Write-Host ''
        Write-Host '  Re-run with -Generate to compute the correct offsets locally, or -Apply to' -ForegroundColor Yellow
        Write-Host '  merge them in and restart TermService.' -ForegroundColor Yellow
        exit 1
    }
}

# ---- generate ----------------------------------------------------------

Write-Host '== generating offsets from the local termsrv.dll ==' -ForegroundColor Cyan

$work = Join-Path $env:TEMP 'rdpwrap-offsetfinder'
$exe  = Join-Path $work '64bit\RDPWrapOffsetFinder.exe'

if (-not (Test-Path $exe)) {
    Write-Host '  downloading llccd/RDPWrapOffsetFinder v1.0 (MIT)...'
    New-Item -ItemType Directory -Force $work | Out-Null
    $zip = Join-Path $work 'finder.zip'
    Invoke-WebRequest -Uri $ToolUrl -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $work -Force
}
if (-not (Test-Path $exe)) {
    Write-Host '  could not obtain the offset finder.' -ForegroundColor Red
    exit 2
}

# The symbol build needs Microsoft's symbol server; the _nosymbol build pattern-matches instead.
# Verified on the reference host 2026-09-01: on a build the community ini already covered, BOTH
# produced all 20 keys IDENTICAL to that known-good section. So falling back costs nothing here,
# and the fallback is what works on a host with no internet.
Push-Location (Split-Path $exe)
try {
    $out = & .\RDPWrapOffsetFinder.exe $TermSrv 2>&1 | ForEach-Object { "$_".Trim() }
    if (-not ($out | Where-Object { $_ -match '^SingleUserOffset' })) {
        Write-Host '  symbol lookup produced nothing usable - falling back to the nosymbol build.' -ForegroundColor Yellow
        $out = & .\RDPWrapOffsetFinder_nosymbol.exe $TermSrv 2>&1 | ForEach-Object { "$_".Trim() }
    }
}
finally { Pop-Location }

$keys = @($out | Where-Object { $_ -match '=' })
if ($keys.Count -lt 20) {
    Write-Host ("  only {0} keys produced (expected 20) - not trustworthy, refusing to go on." -f $keys.Count) -ForegroundColor Red
    $out | ForEach-Object { "    $_" }
    exit 2
}

Write-Host ''
$out | ForEach-Object { Write-Host "  $_" }

$generated = Join-Path $env:TEMP ("rdpwrap-{0}.ini" -f $version)
$out | Set-Content $generated -Encoding ASCII
Write-Host ''
Write-Host ("  written to: {0}" -f $generated)

if (-not $Apply) {
    Write-Host ''
    Write-Host '  Review it, then re-run with -Apply to merge and restart TermService.' -ForegroundColor Yellow
    exit $(if ($covered) { 0 } else { 1 })
}

# ---- apply -------------------------------------------------------------

Write-Host ''
Write-Host '== applying ==' -ForegroundColor Cyan

$backup = "$IniPath.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
Copy-Item $IniPath $backup -Force
Write-Host ("  backed up the existing ini to {0}" -f $backup)

Add-Content -Path $IniPath -Value '' -Encoding ASCII
Add-Content -Path $IniPath -Value $out -Encoding ASCII
Write-Host '  appended the generated sections'

Restart-Service TermService -Force
Write-Host ("  TermService restarted - now {0}" -f (Get-Service TermService).Status) -ForegroundColor Green
Write-Host ''
Write-Host '  Provision a seat to confirm multi-session actually works. If it does not, restore' -ForegroundColor Yellow
Write-Host ("  the backup: Copy-Item '{0}' '{1}' -Force" -f $backup, $IniPath) -ForegroundColor Yellow
exit 0
