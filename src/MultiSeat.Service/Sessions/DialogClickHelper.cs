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
    private const uint BM_CLICK       = 0x00F5;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN    = 0x0D;

    /// <summary>Control id of a dialog's affirmative button — language-independent.</summary>
    private const int IDOK = 1;

    /// <summary>Window class of a standard Win32 dialog. The mstsc security warning is one.</summary>
    private const string DialogClass = "#32770";

    // x64 INPUT: type(4) + padding(4) + union(32) = 40 bytes
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Find a dialog window owned by <paramref name="pid"/> and dismiss it.
    ///
    /// Two-pass strategy to handle both classic Win32 and Windows 11 WinUI dialogs:
    ///   1. EnumChildWindows + BM_CLICK on the named button (works on Win10 / classic mstsc).
    ///   2. SetForegroundWindow + SendInput(VK_RETURN) (works on Win11 WinUI where
    ///      EnumChildWindows cannot enumerate XAML-rendered buttons).
    ///
    /// Skips the main mstsc window (title contains the server address) so we only act
    /// on the security/trust dialog that appears before the connection is established.
    /// </summary>
    public static int RunByPid(int pid, string buttonText, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        var targetPid = (uint)pid;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr foundButton = IntPtr.Zero;
            IntPtr dialogWindow = IntPtr.Zero;

            EnumWindowsProc cb = (hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid != targetPid) return true;
                if (!IsWindowVisible(hWnd)) return true;

                // Skip the main mstsc connection window — its title includes the server IP
                // once the connection screen is shown ("<file> - 127.0.0.2 - Remote Desktop
                // Connection"). The security warning is a separate top-level window titled
                // "Remote Desktop Connection security warning".
                var titleBuf = new StringBuilder(256);
                GetWindowTextW(hWnd, titleBuf, 256);
                if (titleBuf.ToString().Contains("127.0.0.2")) return true;

                var button = FindButton(hWnd, buttonText);
                if (button != IntPtr.Zero)
                {
                    foundButton = button;
                    return false; // stop enumeration
                }

                // No clickable button here. Keep it for the keyboard fallback only if it is
                // a real dialog: mstsc also shows a visible TSC_POPUP_PARENT_WNDCLASS window
                // with the generic title "Remote Desktop Connection", and this used to keep
                // the LAST window enumerated — so the fallback could send Enter to that
                // instead of to the warning.
                var clsBuf = new StringBuilder(64);
                GetClassNameW(hWnd, clsBuf, 64);
                if (string.Equals(clsBuf.ToString(), DialogClass, StringComparison.Ordinal))
                    dialogWindow = hWnd;

                return true;
            };

            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            if (foundButton != IntPtr.Zero)
            {
                // Classic Win32 button: click it directly
                SendMessage(foundButton, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            if (dialogWindow != IntPtr.Zero)
            {
                // Win11 WinUI dialog: EnumChildWindows cannot see XAML buttons.
                // Bring the dialog to foreground and inject VK_RETURN — the XAML
                // framework routes Enter to the focused default button ("Connect").
                SetForegroundWindow(dialogWindow);
                Thread.Sleep(50); // let the window gain focus
                var inputs = new INPUT[]
                {
                    new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_RETURN } },
                    new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_RETURN, dwFlags = KEYEVENTF_KEYUP } }
                };
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                return 0;
            }

            Thread.Sleep(200);
        }

        return 1; // timeout — dialog never appeared
    }

    /// <summary>
    /// Fallback: find a top-level window by exact title, then click a named child button.
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

        return 1;
    }

    /// <summary>
    /// Find a push button by its visible caption, then fall back to the dialog's default
    /// button (IDOK).
    ///
    /// Captions carry keyboard accelerators, so an exact string compare does not work: the
    /// mstsc security warning's affirmative button reports its text as <c>"Co&amp;nnect"</c>,
    /// not "Connect", and Cancel reports <c>"&amp;Cancel"</c>. Comparing raw text meant this
    /// never matched anything, the caller fell through to the keyboard fallback, and the
    /// dialog stayed on screen for the user to click. Accelerators are stripped from both
    /// sides before comparing.
    ///
    /// The IDOK fallback matters for non-English Windows, where the caption is a translated
    /// string we cannot know in advance. Control id 1 is the affirmative button regardless of
    /// language, so it stays correct where a caption match cannot.
    /// </summary>
    private static IntPtr FindButton(IntPtr parent, string text)
    {
        IntPtr found = IntPtr.Zero;
        IntPtr defaultButton = IntPtr.Zero;
        var wanted = StripAccelerators(text);

        EnumChildProc cb = (hWnd, _) =>
        {
            var cls = new StringBuilder(64);
            GetClassNameW(hWnd, cls, 64);
            if (!string.Equals(cls.ToString(), "Button", StringComparison.OrdinalIgnoreCase))
                return true; // a Static can carry the same words; only real buttons are clickable

            var sb = new StringBuilder(256);
            GetWindowTextW(hWnd, sb, 256);

            if (string.Equals(StripAccelerators(sb.ToString()), wanted, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }

            if (GetDlgCtrlID(hWnd) == IDOK && IsWindowVisible(hWnd))
                defaultButton = hWnd;

            return true;
        };

        EnumChildWindows(parent, cb, IntPtr.Zero);
        GC.KeepAlive(cb);

        return found != IntPtr.Zero ? found : defaultButton;
    }

    /// <summary>
    /// Remove keyboard-accelerator markers from a control caption: a single <c>&amp;</c>
    /// marks the next character, and <c>&amp;&amp;</c> escapes a literal ampersand.
    /// "Co&amp;nnect" becomes "Connect"; "R&amp;&amp;D" becomes "R&amp;D".
    /// </summary>
    public static string StripAccelerators(string caption)
    {
        if (string.IsNullOrEmpty(caption) || !caption.Contains('&')) return caption;

        var sb = new StringBuilder(caption.Length);

        for (int i = 0; i < caption.Length; i++)
        {
            if (caption[i] != '&') { sb.Append(caption[i]); continue; }

            // "&&" is a literal ampersand; a trailing lone "&" is dropped.
            if (i + 1 < caption.Length && caption[i + 1] == '&') { sb.Append('&'); i++; }
        }

        return sb.ToString();
    }
}
