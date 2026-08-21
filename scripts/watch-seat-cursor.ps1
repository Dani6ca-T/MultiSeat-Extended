<#
.SYNOPSIS
    Watch a seat session's own cursor, and report what is holding it if it cannot move.

.DESCRIPTION
    The seat-side counterpart to watch-console-cursor.ps1. Run both AT THE SAME TIME, while a
    Moonlight client is connected and its mouse is being moved. Every round of issue #18 so far has
    watched one end only, which cannot distinguish these three cases:

        seat moves, console still   -> normal; the reported symptom is something else
        seat pinned, console moves  -> the client's input is landing in the CONSOLE session
        both move                   -> something is mirroring the seat's cursor onto the console

    It also answers a question that came out of the last round on the reporter's host, where the
    seat's cursor could not be moved by SetCursorPos OR SendInput and sat at exactly screen centre:
    is something CLIPPING or owning the cursor in that session? `ClipCursor` confines the cursor to
    a rectangle, and a rectangle of nearly zero size pins it in place while making SetCursorPos fail
    with no error recorded - which is not a permission problem and does not look like one.

    Reports, then samples:

      * window station / desktop / whether it is the INPUT desktop
      * the virtual desktop rectangle
      * the CLIP rectangle, flagged when it is smaller than the virtual desktop
      * GetCursorInfo flags: showing, hidden, or SUPPRESSED (a real state - Windows suppresses the
        cursor when it believes the last input came from touch)
      * GetCursorPos every 75 ms for the sampling window, with a count of changes

.PARAMETER Seconds
    Sampling window. Default 20. Move the client's mouse continuously for the whole window.

.NOTES
    GitHub issue #18. Runs unelevated inside the seat session, normally via a scheduled task with
    -LogonType Interactive so no password is needed.
#>
[CmdletBinding()]
param(
    [string]$LogPath = 'C:\ProgramData\MultiSeat\seat-cursor-watch.log',
    [int]$Seconds = 20
)

if (-not ('SeatCur' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct CURSORINFO {
  public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos;
}

public static class SeatCur {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetClipCursor(out RECT r);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetCursorInfo(ref CURSORINFO ci);
  [DllImport("user32.dll", SetLastError=true)] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetThreadDesktop(uint tid);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint f, bool inh, uint acc);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  public static extern bool GetUserObjectInformationW(IntPtr h, int idx, StringBuilder p, int len, out int need);

  public static int Err() { return Marshal.GetLastWin32Error(); }

  public static string NameOf(IntPtr h) {
    if (h == IntPtr.Zero) { return "<null>"; }
    StringBuilder sb = new StringBuilder(256); int need;
    if (GetUserObjectInformationW(h, 2, sb, sb.Capacity, out need)) { return sb.ToString(); }
    return "<err " + Marshal.GetLastWin32Error() + ">";
  }

  public static string CursorState() {
    CURSORINFO ci = new CURSORINFO();
    ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
    if (!GetCursorInfo(ref ci)) { return "GetCursorInfo failed err=" + Marshal.GetLastWin32Error(); }
    string flags;
    if (ci.flags == 0) { flags = "HIDDEN"; }
    else if (ci.flags == 1) { flags = "SHOWING"; }
    else if (ci.flags == 2) { flags = "SUPPRESSED (Windows thinks the last input was touch)"; }
    else { flags = "flags=" + ci.flags; }
    return flags + " at " + ci.ptScreenPos.X + "," + ci.ptScreenPos.Y + " hCursor=" + ci.hCursor;
  }
}
'@
}

function Write-Probe($message) {
    Add-Content $LogPath "$((Get-Date).ToString('HH:mm:ss.fff'))  $message" -Encoding ASCII
}

Write-Probe "---- start ----"
Write-Probe "whoami     : $(whoami)"
Write-Probe "sessionId  : $((Get-Process -Id $PID).SessionId)"

$deskCur = [SeatCur]::NameOf([SeatCur]::GetThreadDesktop([SeatCur]::GetCurrentThreadId()))
$hInput = [SeatCur]::OpenInputDesktop(0, $false, 0x0001 -bor 0x0100)
if ($hInput -eq [IntPtr]::Zero) {
    $deskIn = "<OpenInputDesktop failed err $([SeatCur]::Err())>"
} else {
    $deskIn = [SeatCur]::NameOf($hInput)
}
Write-Probe "windowsta  : $([SeatCur]::NameOf([SeatCur]::GetProcessWindowStation()))"
Write-Probe "my desktop : $deskCur"
Write-Probe "input desk : $deskIn"
Write-Probe "on input?  : $($deskCur -eq $deskIn)"

$vx = [SeatCur]::GetSystemMetrics(76); $vy = [SeatCur]::GetSystemMetrics(77)
$vw = [SeatCur]::GetSystemMetrics(78); $vh = [SeatCur]::GetSystemMetrics(79)
Write-Probe "virtualdesk: origin=$vx,$vy size=${vw}x${vh}"

# -- Is something confining the cursor? --------------------------------
# ClipCursor is the usual reason a cursor cannot be moved while nothing reports an error.
$clip = New-Object RECT
if ([SeatCur]::GetClipCursor([ref]$clip)) {
    $cw = $clip.Right - $clip.Left
    $ch = $clip.Bottom - $clip.Top
    Write-Probe "clip rect  : $($clip.Left),$($clip.Top) - $($clip.Right),$($clip.Bottom)  (${cw}x${ch})"
    if ($cw -lt $vw -or $ch -lt $vh) {
        Write-Probe "clip rect  : *** SMALLER THAN THE DESKTOP - something in this session is CLIPPING the cursor."
        Write-Probe "clip rect  : *** That pins it, and makes SetCursorPos fail without recording an error."
    } else {
        Write-Probe "clip rect  : full desktop - nothing is confining the cursor"
    }
} else {
    Write-Probe "clip rect  : GetClipCursor failed err=$([SeatCur]::Err())"
}

Write-Probe "cursor     : $([SeatCur]::CursorState())"

# -- Sample ------------------------------------------------------------
$p = New-Object POINT
[void][SeatCur]::GetCursorPos([ref]$p)
$last = @($p.X, $p.Y)
Write-Probe "start pos  : $($last[0]),$($last[1])"
Write-Probe "watching   : ${Seconds}s - MOVE THE MOONLIGHT CLIENT'S MOUSE CONTINUOUSLY NOW"

$samples = 0; $changes = 0
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    [void][SeatCur]::GetCursorPos([ref]$p)
    $samples++
    if ($p.X -ne $last[0] -or $p.Y -ne $last[1]) {
        $changes++
        if ($changes -le 15) { Write-Probe "MOVED      : $($last[0]),$($last[1]) -> $($p.X),$($p.Y)" }
        $last = @($p.X, $p.Y)
    }
    Start-Sleep -Milliseconds 75
}

Write-Probe "result     : $samples samples, $changes changes"
if ($changes -eq 0) {
    Write-Probe "VERDICT    : the seat's OWN cursor never moved while the client's mouse did."
    Write-Probe "VERDICT    : compare against the console watcher run over the same window."
} else {
    Write-Probe "VERDICT    : the seat's own cursor tracked the client's mouse, which is normal."
}
Write-Probe "---- end ----"
