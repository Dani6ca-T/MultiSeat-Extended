<#
.SYNOPSIS
    Which desktop is the keepalive mstsc actually on? Answers issue #18's follow-up question.

.DESCRIPTION
    Run this in the CONSOLE session while a seat is up.

    The fix for #18 moves the keepalive mstsc onto WinSta0\MultiSeatKeepalive so its pointer cannot
    reach the console desktop. Before concluding the fix does not work, this checks that it is
    actually in effect - which is a different question, and the one that has to be answered first.

    Two things it exists to catch:

      1. THE SEAT PREDATES THE UPGRADE. Restarting the service does not relaunch mstsc: an RDP
         session that is still Active keeps the mstsc that created it, which on an upgraded host is
         the OLD one, still on the console desktop. A seat has to be torn down and provisioned
         again for the new path to run at all.

      2. THE WRONG KEEPALIVE. The service logs two unrelated things containing the word
         "keepalive": timeout.exe running INSIDE the seat session, and the mstsc in the CONSOLE
         session. Only the second one is what #18 is about.

    It maps every mstsc to the desktop it is really on, by enumerating each desktop's windows and
    reading back the owning process - not by trusting configuration or a log line.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$src = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DeskMap {
    [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    delegate bool EnumDesktopProc([MarshalAs(UnmanagedType.LPWStr)] string desktop, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumDesktopsW(IntPtr winsta, EnumDesktopProc cb, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenDesktopW(string desktop, int flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseDesktop(IntPtr h);

    delegate bool EnumWindowProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindowProc cb, IntPtr param);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    public static List<string> Desktops() {
        var found = new List<string>();
        EnumDesktopsW(GetProcessWindowStation(), (d, p) => { found.Add(d); return true; }, IntPtr.Zero);
        return found;
    }

    /// <summary>PIDs owning at least one window on the named desktop.</summary>
    public static List<int> PidsOn(string desktop) {
        var pids = new List<int>();
        var h = OpenDesktopW(desktop, 0, false, 0x0100 /* DESKTOP_ENUMERATE */);
        if (h == IntPtr.Zero) return pids;
        try {
            EnumDesktopWindows(h, (hwnd, p) => {
                int pid; GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0 && !pids.Contains(pid)) pids.Add(pid);
                return true;
            }, IntPtr.Zero);
        } finally { CloseDesktop(h); }
        return pids;
    }
}
'@
if (-not ('DeskMap' -as [type])) { Add-Type -TypeDefinition $src }

Write-Host ''
Write-Host '== keepalive placement check ==' -ForegroundColor Cyan
Write-Host ("this shell is in session {0}" -f (Get-Process -Id $PID).SessionId)
Write-Host ''

$desktops = [DeskMap]::Desktops()
Write-Host ("desktops in this window station: {0}" -f ($desktops -join ', '))
$isolated = $desktops -contains 'MultiSeatKeepalive'
Write-Host ("MultiSeatKeepalive present     : {0}" -f $isolated)
Write-Host ''

$mstsc = @(Get-Process mstsc -ErrorAction SilentlyContinue)
if ($mstsc.Count -eq 0) {
    Write-Host 'No mstsc is running, so there is no keepalive to place. Provision a seat first.' -ForegroundColor Yellow
    exit 2
}

# Map each desktop to the PIDs owning windows on it, then say where each mstsc really is.
$map = @{}
foreach ($d in $desktops) { $map[$d] = [DeskMap]::PidsOn($d) }

Write-Host 'mstsc processes:'
$anyIsolated = $false
$anyConsole = $false
foreach ($p in $mstsc) {
    $where = @()
    foreach ($d in $desktops) { if ($map[$d] -contains $p.Id) { $where += $d } }
    $desk = if ($where.Count) { $where -join '+' } else { 'unknown (no visible window)' }
    if ($where -contains 'MultiSeatKeepalive') { $anyIsolated = $true }
    if ($where -contains 'Default') { $anyConsole = $true }

    Write-Host ("  pid {0,-7} session {1,-4} started {2}  desktop: {3}" -f `
        $p.Id, $p.SessionId, $p.StartTime.ToString('HH:mm:ss'), $desk)
}
Write-Host ''

Write-Host 'what the service logged about it:'
$since = (Get-Date).AddHours(-6)
$events = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='MultiSeat.Service'; StartTime=$since} -ErrorAction SilentlyContinue |
          Where-Object { $_.Message -match 'Keepalive mstsc|Falling back to the console desktop|mstsc launched' } |
          Sort-Object TimeCreated | Select-Object -Last 5
if ($events) {
    foreach ($e in $events) {
        $line = ($e.Message -split "`n" | Where-Object { $_ -match 'Keepalive mstsc|Falling back|mstsc launched' } | Select-Object -First 1)
        Write-Host ("  {0}  {1}" -f $e.TimeCreated.ToString('HH:mm:ss'), $line.Trim())
    }
} else {
    Write-Host '  nothing in the last 6 hours.' -ForegroundColor Yellow
    Write-Host '  NOTE: lines mentioning timeout.exe are a DIFFERENT keepalive - that one runs inside' -ForegroundColor DarkGray
    Write-Host '  the seat session and has nothing to do with the console cursor.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '== verdict ==' -ForegroundColor Cyan
if ($anyIsolated -and -not $anyConsole) {
    Write-Host '  The keepalive IS isolated. If the console cursor still moves, the fix is not the' -ForegroundColor Green
    Write-Host '  whole story and something else is mirroring it - worth reporting.' -ForegroundColor Green
    exit 0
}
if ($anyConsole) {
    Write-Host '  An mstsc is on the CONSOLE desktop (Default), so the fix is NOT in effect for it.' -ForegroundColor Yellow
    Write-Host '  Most likely this seat predates the upgrade: restarting the service does not relaunch' -ForegroundColor Yellow
    Write-Host '  mstsc, and a session that stayed Active keeps the one that created it.' -ForegroundColor Yellow
    Write-Host '  TEAR THE SEAT DOWN, provision a new one, and run this again.' -ForegroundColor Yellow
    exit 1
}
Write-Host '  Could not place the mstsc on any desktop - it may have no window yet. Re-run in a moment.' -ForegroundColor Yellow
exit 2
