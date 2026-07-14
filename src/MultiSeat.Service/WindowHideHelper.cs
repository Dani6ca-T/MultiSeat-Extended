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
