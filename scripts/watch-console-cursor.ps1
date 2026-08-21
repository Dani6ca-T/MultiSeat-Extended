<#
.SYNOPSIS
    Watch the console session's cursor, and prove the watcher can see a move before reporting none.

.DESCRIPTION
    The console half of the #18 cursor-leak instrument. Run it interactively in the console session
    while probe-seat-cursor.ps1 drives the cursor inside a seat session.

    The self-test is the point. A sampling loop that reports "0 changes" is only evidence if it
    would have caught a change, so before watching anything it nudges the console cursor 4 px,
    confirms its own loop noticed, and puts the cursor back. A FAIL there means any quiet result
    below is meaningless.

.PARAMETER Seconds
    How long to sample for. Default 60. The seat probe takes about 10 seconds end to end.

.NOTES
    GitHub issue #18. Pair with probe-seat-cursor.ps1; start this one FIRST.
#>
param([int]$Seconds = 60)

if (-not ('Cur' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public struct PT { public int X; public int Y; }
public static class Cur {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetCursorPos(int x, int y);
  public static int Err() { return Marshal.GetLastWin32Error(); }
}
'@
}

$p = New-Object PT
[void][Cur]::GetCursorPos([ref]$p)
$origin = @($p.X, $p.Y)
Write-Host ("sessionId : {0}" -f (Get-Process -Id $PID).SessionId)
Write-Host ("start     : {0},{1}" -f $origin[0], $origin[1])

# --- Control: nudge the console cursor 4 px and confirm this loop sees it.
$ok = [Cur]::SetCursorPos($origin[0] + 4, $origin[1] + 4)
Start-Sleep -Milliseconds 200
[void][Cur]::GetCursorPos([ref]$p)
$sawIt = ($p.X -ne $origin[0] -or $p.Y -ne $origin[1])
[void][Cur]::SetCursorPos($origin[0], $origin[1])
if ($sawIt) {
    Write-Host "selftest  : PASS - this watcher does detect console cursor movement"
} else {
    Write-Host ("selftest  : FAIL - SetCursorPos ok={0} err={1}, cursor did not move." -f $ok, [Cur]::Err())
    Write-Host "selftest  : A '0 changes' result below would be MEANINGLESS. Fix this first."
}

Write-Host ("watching  : {0}s, sampling every 75 ms - start the seat probe NOW" -f $Seconds)
[void][Cur]::GetCursorPos([ref]$p)
$last = @($p.X, $p.Y)
$samples = 0; $changes = 0
$deadline = (Get-Date).AddSeconds($Seconds)

while ((Get-Date) -lt $deadline) {
    [void][Cur]::GetCursorPos([ref]$p)
    $samples++
    if ($p.X -ne $last[0] -or $p.Y -ne $last[1]) {
        $changes++
        if ($changes -le 20) {
            Write-Host ("{0}  MOVED {1},{2} -> {3},{4}" -f (Get-Date).ToString('HH:mm:ss.fff'), $last[0], $last[1], $p.X, $p.Y)
        }
        $last = @($p.X, $p.Y)
    }
    Start-Sleep -Milliseconds 75
}

Write-Host ("result    : {0} samples, {1} changes, selftest={2}" -f $samples, $changes, $(if ($sawIt) { 'PASS' } else { 'FAIL' }))
