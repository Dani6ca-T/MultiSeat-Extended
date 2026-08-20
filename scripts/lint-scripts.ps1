<#
.SYNOPSIS
    Parse and encoding check for every shipped PowerShell script.

.DESCRIPTION
    Run this under BOTH engines - Windows PowerShell 5.1 and PowerShell 7 - because the two
    disagree in ways that matter and only one of them is what a user gets by default.

    This exists because install-service.ps1 shipped in the FIRST public release with a
    PowerShell 7 only null-conditional operator in it, which is a parse error under
    powershell.exe. That made the documented install command fail before its first step, on
    the default shell of every Windows host, and it survived four releases because nothing
    ever checked. See the 2026-08-19 fixes.

    Two checks, both cheap:

      1. PARSE - the file must parse on the engine running this script.
      2. ENCODING - a file must be pure ASCII, or carry a UTF-8 BOM. UTF-8 without a BOM is
         read as ANSI by 5.1, which turns em-dashes into mojibake and, in one real case,
         mangled string terminators badly enough to be a parse failure on its own.

.PARAMETER Path
    Directories to scan. Defaults to the repo's scripts\ and prerequisites\ folders.
#>
[CmdletBinding()]
param(
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $repo = Split-Path $PSScriptRoot -Parent
    $Path = @(
        (Join-Path $repo 'scripts'),
        (Join-Path $repo 'prerequisites')
    )
}

$engine = "PowerShell $($PSVersionTable.PSVersion)"
Write-Host "Linting shipped scripts under $engine" -ForegroundColor White

$files = foreach ($p in $Path) {
    if (Test-Path $p) { Get-ChildItem -LiteralPath $p -Filter *.ps1 -File -Recurse }
}
$files = @($files | Sort-Object FullName)

if ($files.Count -eq 0) {
    Write-Host "No .ps1 files found under: $($Path -join ', ')" -ForegroundColor Yellow
    exit 2
}

$failures = @()

foreach ($f in $files) {
    $name = $f.Name
    $problems = @()

    # -- parse --------------------------------------------------------
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        $first = $errors[0]
        $problems += "parse error line $($first.Extent.StartLineNumber): $($first.Message)"
    }

    # -- encoding -----------------------------------------------------
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = 0
    foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii++ } }
    if ($nonAscii -gt 0 -and -not $hasBom) {
        $problems += "$nonAscii non-ASCII byte(s) and no UTF-8 BOM - Windows PowerShell 5.1 will read this as ANSI"
    }

    if ($problems.Count -gt 0) {
        $failures += [pscustomobject]@{ File = $name; Problems = $problems }
        Write-Host ("  FAIL  {0}" -f $name) -ForegroundColor Red
        foreach ($p in $problems) { Write-Host ("          {0}" -f $p) -ForegroundColor Red }
    }
    else {
        Write-Host ("  ok    {0}" -f $name) -ForegroundColor DarkGray
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host ("{0} of {1} script(s) FAILED under {2}" -f $failures.Count, $files.Count, $engine) -ForegroundColor Red
    exit 1
}

Write-Host ("All {0} script(s) pass under {1}" -f $files.Count, $engine) -ForegroundColor Green
exit 0
