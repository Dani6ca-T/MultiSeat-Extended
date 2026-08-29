<#
.SYNOPSIS
    Decide whether MultiSeat's hidden mstsc keepalive is what moves the console cursor (issue #18).

.DESCRIPTION
    Run this IN THE CONSOLE SESSION, interactively, WHILE a Moonlight client is streaming a seat
    and the console cursor is visibly being driven by the client's mouse.

    The probe samples the console cursor in four phases - idle, before, during and after suspending
    the seat's mstsc process - and compares them:

        during <= idle noise floor    -> mstsc is moving the console cursor
        during >  idle noise floor    -> mstsc is NOT the cause
        anything else                 -> PROBE INVALID, nothing was measured

    The idle phase exists because "no movement" cannot be assumed to mean zero. Measured on the
    reference host, a supposedly quiet 2-second sample read 7 changes, because a Moonlight client
    was streaming the console at the time and was legitimately driving the cursor. A verdict built
    on "the count was zero" would have been wrong there, so the probe measures its own noise floor
    first and compares against that.

    Suspending is deliberate: killing mstsc drops the seat session to Disconnected, which breaks
    Apollo's display calls and ends the stream, so a stopped cursor would prove nothing. A suspended
    process keeps the RDP session connected and simply stops servicing it for a few seconds.

    The probe refuses to report a verdict it did not earn:
      - it proves its own sampler can see a cursor move before trusting a "no movement" reading;
      - it proves the suspend actually took, by reading the target's thread states;
      - it requires movement in the baseline, so a run where the symptom was not happening is
        reported as invalid rather than as a clean result;
      - it requires movement to come back after the resume, so a stream that died mid-probe cannot
        be mistaken for a successful suspend.

    mstsc is always resumed, including on Ctrl-C or an error.

.PARAMETER MstscPid
    Which mstsc to suspend. Only needed when more than one is running.

.PARAMETER Seconds
    Length of each sampling phase. Default 5.

.PARAMETER ListOnly
    Show the mstsc processes and exit without touching anything.

.EXAMPLE
    .\suspend-mstsc-probe.ps1
    .\suspend-mstsc-probe.ps1 -MstscPid 7888 -Seconds 6
#>
[CmdletBinding()]
param(
    [int]$MstscPid = 0,
    [int]$Seconds = 5,
    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

$sig = @'
using System;
using System.Runtime.InteropServices;

public static class Probe {
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("ntdll.dll")]
    public static extern int NtSuspendProcess(IntPtr h);

    [DllImport("ntdll.dll")]
    public static extern int NtResumeProcess(IntPtr h);
}
'@

if (-not ('Probe' -as [type])) { Add-Type -TypeDefinition $sig }

function Get-CursorPoint {
    $p = New-Object Probe+POINT
    [void][Probe]::GetCursorPos([ref]$p)
    return $p
}

function Get-SuspendedThreadCount {
    param([int]$Id)
    try {
        $proc = Get-Process -Id $Id -ErrorAction Stop
        $n = 0
        foreach ($t in $proc.Threads) {
            if ($t.ThreadState -eq 'Wait' -and $t.WaitReason -eq 'Suspended') { $n++ }
        }
        return $n
    } catch {
        return -1
    }
}

# Sample the console cursor for a while and count how many times it changed position.
function Measure-CursorChanges {
    param([int]$DurationSeconds, [string]$Label)

    $last = Get-CursorPoint
    $changes = 0
    $samples = 0
    $deadline = (Get-Date).AddSeconds($DurationSeconds)

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 75
        $now = Get-CursorPoint
        $samples++
        if ($now.X -ne $last.X -or $now.Y -ne $last.Y) {
            $changes++
            $last = $now
        }
    }

    Write-Host ("  {0,-10} {1,4} samples, {2,4} changes" -f $Label, $samples, $changes)
    return $changes
}

Write-Host ''
Write-Host '== suspend-mstsc-probe =='
Write-Host ("session      : {0} (this shell)" -f (Get-Process -Id $PID).SessionId)
Write-Host ''

# ---- find the target -------------------------------------------------------
$all = @(Get-Process mstsc -ErrorAction SilentlyContinue)
if ($all.Count -eq 0) {
    Write-Host 'No mstsc process is running.'
    Write-Host 'PROBE INVALID - there is no keepalive to suspend, so nothing can be measured.'
    exit 2
}

Write-Host 'mstsc processes:'
foreach ($m in $all) {
    Write-Host ("  pid {0,-7} session {1,-4} started {2}" -f $m.Id, $m.SessionId, $m.StartTime)
}
Write-Host ''

if ($ListOnly) { exit 0 }

if ($MstscPid -ne 0) {
    $target = $all | Where-Object { $_.Id -eq $MstscPid }
    if (-not $target) {
        Write-Host ("pid {0} is not one of the mstsc processes above." -f $MstscPid)
        Write-Host 'PROBE INVALID - wrong target.'
        exit 2
    }
} elseif ($all.Count -eq 1) {
    $target = $all[0]
} else {
    Write-Host 'More than one mstsc is running, so the right one cannot be guessed.'
    Write-Host 'Re-run with -MstscPid <pid> naming the keepalive for the seat you are streaming.'
    Write-Host 'PROBE INVALID - no target chosen.'
    exit 2
}

Write-Host ("target       : pid {0}, session {1}" -f $target.Id, $target.SessionId)
Write-Host ''

# ---- prove the sampler works before trusting any silence -------------------
Write-Host 'Self-test (moving the console cursor 4 px to prove this script can see a move):'
$before = Get-CursorPoint
[void][Probe]::SetCursorPos($before.X + 4, $before.Y)
Start-Sleep -Milliseconds 150
$after = Get-CursorPoint
[void][Probe]::SetCursorPos($before.X, $before.Y)

if ($after.X -eq $before.X -and $after.Y -eq $before.Y) {
    Write-Host '  FAIL - the cursor did not move, or this process cannot read it.'
    Write-Host 'PROBE INVALID - a "no movement" reading from this run would mean nothing.'
    exit 2
}
Write-Host '  PASS'
Write-Host ''

Write-Host 'Phase 0 - noise floor. Touch NOTHING for the next few seconds:'
Write-Host '  (not the physical mouse, not the client - this measures what moves on its own)'
Start-Sleep -Seconds 2
$idle = Measure-CursorChanges -DurationSeconds $Seconds -Label 'idle'
Write-Host ''

Write-Host 'Now move the mouse in your Moonlight client CONTINUOUSLY until the probe finishes.'
Write-Host 'Do not touch the physical mouse.'
Write-Host ''
Start-Sleep -Seconds 3

$baseline = 0
$during   = 0
$after2   = 0
$suspendedThreads = -1

try {
    Write-Host 'Phase 1 - baseline, mstsc running:'
    $baseline = Measure-CursorChanges -DurationSeconds $Seconds -Label 'baseline'

    Write-Host ''
    Write-Host ("Phase 2 - suspending mstsc pid {0}:" -f $target.Id)
    $status = [Probe]::NtSuspendProcess($target.Handle)
    Start-Sleep -Milliseconds 300
    $suspendedThreads = Get-SuspendedThreadCount -Id $target.Id
    Write-Host ("  NtSuspendProcess status 0x{0:X8}, suspended threads: {1}" -f $status, $suspendedThreads)

    if ($status -ne 0 -or $suspendedThreads -le 0) {
        Write-Host '  the suspend did not take.'
    } else {
        $during = Measure-CursorChanges -DurationSeconds $Seconds -Label 'suspended'
    }
} finally {
    Write-Host ''
    Write-Host 'Resuming mstsc:'
    try {
        $r = [Probe]::NtResumeProcess($target.Handle)
        Start-Sleep -Milliseconds 300
        $stillSuspended = Get-SuspendedThreadCount -Id $target.Id
        Write-Host ("  NtResumeProcess status 0x{0:X8}, suspended threads now: {1}" -f $r, $stillSuspended)
    } catch {
        Write-Host ("  RESUME FAILED: {0}" -f $_.Exception.Message)
        Write-Host '  Resume it by hand before doing anything else, or tear the seat down.'
    }
}

Write-Host ''
Write-Host 'Phase 3 - control, mstsc running again:'
$after2 = Measure-CursorChanges -DurationSeconds $Seconds -Label 'resumed'

# ---- verdict ---------------------------------------------------------------
Write-Host ''
Write-Host '== counts =='
Write-Host ("  idle (noise floor) : {0}" -f $idle)
Write-Host ("  baseline           : {0}" -f $baseline)
Write-Host ("  mstsc suspended    : {0}" -f $during)
Write-Host ("  resumed            : {0}" -f $after2)
Write-Host ''
Write-Host '== verdict =='

if ($suspendedThreads -le 0) {
    Write-Host 'PROBE INVALID - mstsc was never actually suspended, so phase 2 tested nothing.'
    exit 2
}

if ($baseline -le $idle) {
    Write-Host 'PROBE INVALID - the baseline is no higher than the idle noise floor, so this run'
    Write-Host 'cannot tell your client driving the cursor apart from whatever moves it anyway.'
    Write-Host 'Either the symptom was not happening, or something else is moving the cursor too.'
    exit 2
}

if ($after2 -le $idle) {
    Write-Host 'INCONCLUSIVE - movement did not come back after the resume.'
    Write-Host 'The stream most likely dropped during the suspend, so a quiet phase 2 cannot be'
    Write-Host 'credited to mstsc. Reconnect the client and run it again.'
    exit 2
}

if ($during -le $idle) {
    Write-Host 'RESULT: mstsc IS moving the console cursor.'
    Write-Host 'Movement fell to the noise floor while it was suspended and came back when it was'
    Write-Host 'resumed, with the client connected the whole time.'
    exit 0
}

Write-Host 'RESULT: mstsc is NOT the cause.'
Write-Host 'The console cursor kept moving above the noise floor while mstsc was suspended, so'
Write-Host 'something else is doing it.'
exit 1
