<#
.SYNOPSIS
    Move the cursor from INSIDE a seat session, and prove it actually moved.

.DESCRIPTION
    The seat half of the #18 cursor-leak instrument. Pair it with watch-console-cursor.ps1 running
    in the console session: this drives the seat's cursor, that one watches the console's.

    It exists in this shape because the first version of it produced a worthless run. On the
    reporter's host every SetCursorPos returned False and the seat cursor never moved, so the
    console's "0 changes" was a result the run had to produce no matter what the machine does - a
    reading that could not fail. See docs and the memory rule: validate the instrument before
    concluding.

    So this one refuses to hand back a clean answer it has not earned:

      * logs the window station, its own desktop, and the INPUT desktop, plus whether they match -
        SetCursorPos is refused outright when the caller is not on the input desktop, which is what
        a locked or Disconnected session looks like from in here;
      * reports GetLastError only on a call that FAILED (Windows does not clear it on success, so
        logging it unconditionally prints alarming leftovers next to ok=True);
      * attaches to the input desktop with SetThreadDesktop on a mismatch - the same thing Apollo's
        syncThreadDesktop() does before retrying input;
      * fires TWO stimuli per point: SetCursorPos, then SendInput with MOVE|ABSOLUTE|VIRTUALDESK,
        which is Apollo's actual mouse call, in case one path is permitted and the other is not;
      * ends with PROBE VALID / PROBE INVALID. If the seat cursor never moved, the console reading
        means nothing and the log says so in as many words.

    Runs as a normal, non-elevated seat user. Restores the cursor position when done.

.PARAMETER LogPath
    Where to write the log. Defaults to C:\ProgramData\MultiSeat\seat-cursor.log.

.EXAMPLE
    # Register it against a seat account, no password needed: -LogonType Interactive lands it in
    # the interactive session that account already has open.
    $act  = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\ProgramData\MultiSeat\probe-seat-cursor.ps1"'
    $prin = New-ScheduledTaskPrincipal -UserId 'HOST\SeatAccount' -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'CursorProbe' -Action $act -Principal $prin
    Start-ScheduledTask -TaskName 'CursorProbe'

.NOTES
    Baseline on the reference host, seat provisioned with no Moonlight client connected: seat probe
    5/5 points landed by both mechanisms, console watcher 320 samples / 0 changes, selftest PASS.
    Seat input does not reach the console there. GitHub issue #18.
#>
[CmdletBinding()]
param(
    [string]$LogPath = 'C:\ProgramData\MultiSeat\seat-cursor.log'
)

$log = $LogPath

if (-not ('Probe' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public struct PT { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT {
  public int dx; public int dy; public uint mouseData;
  public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
}
[StructLayout(LayoutKind.Sequential)]
public struct INPUT { public uint type; public MOUSEINPUT mi; }

public static class Probe {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] p, int cb);
  [DllImport("user32.dll", SetLastError=true)] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetThreadDesktop(uint tid);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint f, bool inh, uint acc);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetThreadDesktop(IntPtr h);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool CloseDesktop(IntPtr h);
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

  public static IntPtr InputDesktop() { return OpenInputDesktop(0, false, 0x0001 | 0x0100); }

  // Apollo's own call: SendInput MOVE|ABSOLUTE|VIRTUALDESK with 0-65535 coords.
  public static uint MoveAbsRaw(int nx, int ny) {
    INPUT[] inp = new INPUT[1];
    inp[0].type = 0;
    inp[0].mi.dx = nx;
    inp[0].mi.dy = ny;
    inp[0].mi.dwFlags = 0x0001 | 0x8000 | 0x4000;
    return SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
  }
}
'@
}

function Log($m) { Add-Content $log "$((Get-Date).ToString('HH:mm:ss.fff'))  $m" -Encoding ASCII }

Log "---- start ----"
Log "whoami      : $(whoami)"
Log "sessionId   : $((Get-Process -Id $PID).SessionId)"
Log "elevated    : $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"

# --- Where is this process actually running? This is what decides whether input APIs work at all.
$winsta  = [Probe]::NameOf([Probe]::GetProcessWindowStation())
$deskCur = [Probe]::NameOf([Probe]::GetThreadDesktop([Probe]::GetCurrentThreadId()))
$hInput  = [Probe]::InputDesktop()
if ($hInput -eq [IntPtr]::Zero) {
    $deskIn = "<OpenInputDesktop failed, err $([Probe]::Err())>"
} else {
    $deskIn = [Probe]::NameOf($hInput)
}
Log "windowsta   : $winsta"
Log "my desktop  : $deskCur"
Log "input desk  : $deskIn"
Log "on input?   : $($deskCur -eq $deskIn)"

$vx = [Probe]::GetSystemMetrics(76); $vy = [Probe]::GetSystemMetrics(77)
$vw = [Probe]::GetSystemMetrics(78); $vh = [Probe]::GetSystemMetrics(79)
Log "virtualdesk : origin=$vx,$vy size=${vw}x${vh}"

# --- If we are not on the input desktop, do what Apollo's syncThreadDesktop() does and attach to it.
if ($hInput -ne [IntPtr]::Zero -and $deskCur -ne $deskIn) {
    $ok = [Probe]::SetThreadDesktop($hInput)
    Log "SetThreadDesktop -> $ok err=$([Probe]::Err())  (now on: $([Probe]::NameOf([Probe]::GetThreadDesktop([Probe]::GetCurrentThreadId()))))"
}

$p = New-Object PT
[void][Probe]::GetCursorPos([ref]$p)
$restore = @($p.X, $p.Y)
$moved = 0
$points = @(@(200,150), @(1500,150), @(900,800), @(200,900), @(1500,900))

foreach ($pt in $points) {
    $x = $pt[0]; $y = $pt[1]

    [void][Probe]::GetCursorPos([ref]$p)
    $before = "$($p.X),$($p.Y)"

    # stimulus 1: SetCursorPos
    $ok1  = [Probe]::SetCursorPos($x, $y)
    if ($ok1) { $err1 = '-' } else { $err1 = [Probe]::Err() }
    Start-Sleep -Milliseconds 250
    [void][Probe]::GetCursorPos([ref]$p)
    $after1 = "$($p.X),$($p.Y)"

    # stimulus 2: Apollo's SendInput, absolute over the virtual desktop
    $nx = [int]((($x - $vx) * 65535.0) / [Math]::Max(1, $vw - 1))
    $ny = [int]((($y - $vy) * 65535.0) / [Math]::Max(1, $vh - 1))
    $sent = [Probe]::MoveAbsRaw($nx, $ny)
    if ($sent -gt 0) { $err2 = '-' } else { $err2 = [Probe]::Err() }
    Start-Sleep -Milliseconds 250
    [void][Probe]::GetCursorPos([ref]$p)
    $after2 = "$($p.X),$($p.Y)"

    if ($after2 -ne $before) { $moved++ }

    Log "target=$x,$y before=$before | SetCursorPos ok=$ok1 err=$err1 pos=$after1 | SendInput sent=$sent err=$err2 pos=$after2"
    Start-Sleep -Milliseconds 1500
}

[void][Probe]::SetCursorPos($restore[0], $restore[1])
if ($hInput -ne [IntPtr]::Zero) { [void][Probe]::CloseDesktop($hInput) }

# --- The gate. Without this the console reading means nothing.
if ($moved -gt 0) {
    Log "VERDICT     : moved $moved/$($points.Count) -> PROBE VALID, the console watcher output is meaningful"
} else {
    Log "VERDICT     : moved 0/$($points.Count) -> PROBE INVALID. The seat cursor never moved, so"
    Log "VERDICT     : a still console cursor proves NOTHING. Do not draw a conclusion from this run."
}
Log "---- end ----"
