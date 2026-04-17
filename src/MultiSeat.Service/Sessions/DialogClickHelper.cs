using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Helper mode: find a dialog window and click a named button inside it.
/// Invoked via CreateProcessAsUser in the console session so it can interact with windows
/// on WinSta0\Default. Polls until the window appears or the timeout elapses.
///
/// Usage (by window title):
///   MultiSeat.Service.exe --click-dialog "Window Title" "Button Text" [timeoutMs]
///
/// Usage (by owner PID — more robust, doesn't depend on window title):
///   MultiSeat.Service.exe --click-dialog-pid &lt;pid&gt; "Button Text" [timeoutMs]
///
/// Exit code: 0 = clicked, 1 = not found within timeout.
/// </summary>
internal static class DialogClickHelper
{
    private const uint BM_CLICK = 0x00F5;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Find a button in any top-level window owned by <paramref name="pid"/> and click it.
    /// More robust than <see cref="Run"/> because it doesn't depend on the window title.
    /// Polls for up to <paramref name="timeoutMs"/> ms.
    /// </summary>
    public static int RunByPid(int pid, string buttonText, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        var targetPid = (uint)pid;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr foundButton = IntPtr.Zero;

            EnumWindowsProc cb = (hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid != targetPid)
                    return true; // not our process — continue

                if (!IsWindowVisible(hWnd))
                    return true; // skip hidden windows (not the dialog we're looking for)

                var button = FindButton(hWnd, buttonText);
                if (button != IntPtr.Zero)
                {
                    foundButton = button;
                    return false; // stop enumeration
                }
                return true;
            };

            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            if (foundButton != IntPtr.Zero)
            {
                SendMessage(foundButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            Thread.Sleep(200);
        }

        return 1; // timeout — dialog never appeared
    }

    /// <summary>
    /// Fallback: find a top-level window by exact title, then click a named child button.
    /// Polls for up to <paramref name="timeoutMs"/> ms.
    /// </summary>
    public static int Run(string windowTitle, string buttonText, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var dialog = FindWindowW(null, windowTitle);
            if (dialog != IntPtr.Zero)
            {
                var button = FindButton(dialog, buttonText);
                if (button != IntPtr.Zero)
                {
                    SendMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
            }

            Thread.Sleep(200);
        }

        return 1; // timeout — dialog never appeared
    }

    private static IntPtr FindButton(IntPtr parent, string text)
    {
        IntPtr found = IntPtr.Zero;

        EnumChildProc cb = (hWnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetWindowTextW(hWnd, sb, 256);
            if (string.Equals(sb.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop
            }
            return true;
        };

        EnumChildWindows(parent, cb, IntPtr.Zero);
        GC.KeepAlive(cb); // prevent delegate GC during enumeration
        return found;
    }
}
