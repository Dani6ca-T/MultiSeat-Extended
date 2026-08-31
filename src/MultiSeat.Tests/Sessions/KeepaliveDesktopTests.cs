using System.Diagnostics;
using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// The keepalive mstsc moved to a desktop of its own to fix issue #18 — an RDP client repositions
/// its local cursor from the server's pointer-position message, so on the console's interactive
/// desktop it mirrored the seat's pointer onto the console user's screen.
///
/// The subtle part is not the desktop, it is the HANDLE LIFETIME. A desktop dies when its last
/// handle closes, and a process created for it has not necessarily referenced it yet. Measured on
/// the reference host with identical code:
///
///     handle closed immediately after CreateProcess   mstsc dead 1.2s later
///     handle held for 2s                              mstsc alive
///
/// So the helper waits for the child to settle before letting go. These cover that wait.
/// </summary>
public class KeepaliveDesktopTests
{
    private static readonly TimeSpan ShortSettle = TimeSpan.FromMilliseconds(600);

    [Fact]
    public void TheDesktopIsQualifiedWithTheWindowStation()
    {
        // CreateProcess wants "WinSta0\Name"; a bare name silently resolves elsewhere.
        Assert.Equal(@"WinSta0\MultiSeatKeepalive", KeepaliveDesktopHelper.QualifiedDesktop);
    }

    [Fact]
    public void AProcessThatKeepsRunningIsReportedSettled()
    {
        // NOT "timeout /t 30": it needs a console input handle and exits immediately under
        // CreateNoWindow, so the "long-lived" process was not one - this test failed on its own
        // fixture before it ever tested the code.
        using var proc = Process.Start(new ProcessStartInfo(
            "powershell.exe", "-NoProfile -Command Start-Sleep 30")
        { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.True(KeepaliveDesktopHelper.WaitUntilSettled(proc.Id, ShortSettle));
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AProcessThatExitsDuringTheWaitIsNotSettled()
    {
        // The real failure: mstsc starts, the desktop goes away underneath it, and it dies. The
        // helper has to notice and report failure so the service falls back to the console desktop
        // rather than handing back a PID that no longer exists.
        using var proc = Process.Start(new ProcessStartInfo(
            "cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;

        Assert.False(KeepaliveDesktopHelper.WaitUntilSettled(proc.Id, ShortSettle));
    }

    [Fact]
    public void APidThatNeverExistedIsNotSettled()
    {
        // GetProcessById throws for an unknown id; treating that as "fine" would hand the service
        // a PID it can never track or kill.
        Assert.False(KeepaliveDesktopHelper.WaitUntilSettled(999_999_99, ShortSettle));
    }
}
