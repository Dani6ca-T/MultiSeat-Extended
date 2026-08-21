<#
.SYNOPSIS
    Can THIS session open these HID devices? The asymmetry between sessions is what proves a
    HidHide session jail is real.

.DESCRIPTION
    HidHide filters at OPEN time, so "may I open this device" is the entire question. A jail rule
    (`<device instance path>!<sessionId>`) is only meaningfully different from a plain global hide
    if the answer differs BETWEEN sessions - denied in session 0 and in every other session, open
    inside the jailed one. One reading on its own proves nothing: a global hide, a stripped suffix
    and a working jail all look identical from a single session.

    So run this same script in each session and compare:

      * the service's session (0)   - via a scheduled task with a SYSTEM principal
      * the console session         - directly
      * the seat's session          - via a scheduled task, -LogonType Interactive

    It opens a control file first. If that fails too, a DENIED below says nothing about HidHide,
    only that this probe cannot open anything from here.

.PARAMETER Links
    Device symbolic links, e.g. \\?\hid#vid_045e&pid_028e&ig_00#3&8968588&0&0000#{4d1e55b2-...}.
    Read them out of `HidHideCLI.exe --dev-gaming --cancel` ("symbolicLink").

    NOTE: when this runs from a scheduled task or `powershell.exe -File`, arguments are passed
    LITERALLY - a `$var` stays the four characters `$var`. Pass real values, or use -LinksFile.

.PARAMETER LinksFile
    A file with one symbolic link per line. Easier than quoting these paths on a command line:
    they are full of `&` and `#`, which every shell layer wants to interpret.

.EXAMPLE
    .\hidhide-can-open.ps1 -LinksFile C:\ProgramData\MultiSeat\pads.txt -LogPath C:\ProgramData\MultiSeat\open-console.log

.NOTES
    GitHub issue #19. Companion to `MultiSeat.Service.exe --hidhide`, which reports the rules;
    this reports what they actually do.
#>
[CmdletBinding()]
param(
    [string[]]$Links = @(),
    [string]$LinksFile,
    [string]$LogPath = "$env:TEMP\hidhide-can-open.log"
)

if (-not ('HidHideOpen' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class HidHideOpen {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern SafeFileHandle CreateFileW(string p, uint access, uint share, IntPtr sa,
                                                  uint disp, uint flags, IntPtr tmpl);
  public static string Try(string path) {
    // Ask for no access at all: the question is "may I open it", not "give it to me".
    var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (h.IsInvalid) { return "DENIED err=" + Marshal.GetLastWin32Error(); }
    h.Dispose();
    return "OPEN";
  }
}
'@
}

function Write-Probe($message) {
    Add-Content $LogPath "$((Get-Date).ToString('HH:mm:ss.fff'))  $message" -Encoding ASCII
}

if ($LinksFile -and (Test-Path $LinksFile)) {
    $Links = @(Get-Content $LinksFile | Where-Object { $_.Trim() -ne '' })
}

Write-Probe "---- start ----"
Write-Probe "whoami    : $(whoami)"
Write-Probe "sessionId : $((Get-Process -Id $PID).SessionId)"

# The control. A probe that cannot open anything would report DENIED for every device and look
# exactly like a working jail.
$control = "$env:SystemRoot\System32\drivers\etc\hosts"
Write-Probe "control   : plain file -> $([HidHideOpen]::Try($control))"

if ($Links.Count -eq 0) {
    Write-Probe "VERDICT   : no links given - nothing was tested. This run proves NOTHING."
    Write-Probe "---- end ----"
    exit 2
}

foreach ($link in $Links) {
    Write-Probe "device    : $link"
    Write-Probe "            -> $([HidHideOpen]::Try($link))"
}
Write-Probe "---- end ----"
