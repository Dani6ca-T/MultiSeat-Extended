using System.Diagnostics;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service;

/// <summary>
/// Hides all top-level windows belonging to a process (used to hide the mstsc
/// window that keeps a seat's RDP session Active).
///
/// Must run IN the target Windows session (Session 1 / console session) because
/// EnumWindows only enumerates windows on the caller's window station/desktop.
/// A SYSTEM service in Session 0 (WinSta0\Default of Session 0) cannot see the
/// console user's mstsc window, so hiding it from the service is a silent no-op —
/// the window stays visible to whoever is using the host account (GitHub issue #8).
///
/// Invoked by the service via RunInConsoleSession (CreateProcessAsUser) so the
/// process runs in the console session where mstsc's window actually lives.
/// </summary>
internal static class WindowHideHelper
{
    /// <summary>
    /// The mstsc window that actually covers the screen. Hiding by class rather than hiding
    /// everything the process owns matters: mstsc's security/trust prompt is a plain dialog
    /// (<c>#32770</c>), and hiding that would leave an invisible modal nobody can answer —
    /// the dismisser could not click it and the connection would hang until it timed out.
    /// </summary>
    private const string RdpClientWindowClass = "TscShellContainerClass";

    /// <summary>
    /// Keep the RDP client window hidden for as long as the process lives (or for a bounded
    /// number of seconds).
    ///
    /// A single hide after connecting is not enough. mstsc shows its window again later — on
    /// connect, on reconnect, and when the session's resolution changes — and on a host whose
    /// console someone is actually using, that window covers their screen. Closing it (the only
    /// obvious response) disconnects the seat. So watch instead of hiding once.
    ///
    /// Deliberately narrow: only <see cref="RdpClientWindowClass"/> is touched, and only when it
    /// is genuinely visible and not minimized, so this can never swallow a dialog.
    /// </summary>
    /// <param name="pid">mstsc process to watch.</param>
    /// <param name="seconds">-1 to watch for the process's lifetime; otherwise a bound.</param>
    public static bool WatchAndHide(int pid, int seconds)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"[WindowHide] PID {pid} not found in this session");
            return false;
        }

        var deadline = seconds < 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(seconds);
        var hidden = 0;

        Console.Out.WriteLine(
            $"[WindowHide] Watching PID {pid} ({(seconds < 0 ? "for its lifetime" : seconds + "s")})");

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (process.HasExited) break;
            }
            catch { break; }

            foreach (var hWnd in FindVisibleClientWindows((uint)pid))
            {
                User32.ShowWindow(hWnd, Kernel32.SW_HIDE);
                hidden++;
                Console.Out.WriteLine(
                    $"[WindowHide] Hid a visible {RdpClientWindowClass} window for PID {pid} (total {hidden})");
            }

            Thread.Sleep(250);
        }

        Console.Out.WriteLine($"[WindowHide] Stopped watching PID {pid} after hiding {hidden} window(s)");
        return true;
    }

    /// <summary>
    /// Visible, non-minimized RDP client windows owned by the process. Minimized ones are left
    /// alone — they are already off-screen, and mstsc is started minimized on purpose.
    /// </summary>
    private static List<IntPtr> FindVisibleClientWindows(uint targetPid)
    {
        var matches = new List<IntPtr>();

        User32.EnumWindows((hWnd, _) =>
        {
            User32.GetWindowThreadProcessId(hWnd, out var wndPid);
            if (wndPid != targetPid) return true;
            if (!User32.IsWindowVisible(hWnd) || User32.IsIconic(hWnd)) return true;
            if (GetClassName(hWnd) != RdpClientWindowClass) return true;

            matches.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return matches;
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        var length = User32.GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// Hide every top-level window owned by the given PID. Returns true if the
    /// process was found (whether or not it had visible windows).
    /// </summary>
    public static bool HideByPid(int pid)
    {
        try
        {
            // Verify the target process exists in this session before enumerating.
            try { using var _ = Process.GetProcessById(pid); }
            catch (ArgumentException)
            {
                Console.Error.WriteLine($"[WindowHide] PID {pid} not found in this session");
                return false;
            }

            var targetPid = (uint)pid;
            var hid = 0;
            User32.EnumWindows((hWnd, _) =>
            {
                User32.GetWindowThreadProcessId(hWnd, out var wndPid);
                if (wndPid == targetPid)
                {
                    User32.ShowWindow(hWnd, Kernel32.SW_HIDE);
                    hid++;
                }
                return true; // continue enumeration
            }, IntPtr.Zero);

            Console.Out.WriteLine($"[WindowHide] Hid {hid} window(s) for PID {pid}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WindowHide] Failed for PID {pid}: {ex.Message}");
            return false;
        }
    }
}
