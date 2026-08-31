using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Launches the keepalive mstsc onto a desktop of its own, inside the console session.
///
/// WHY THIS EXISTS (issue #18)
///
/// The keepalive mstsc runs in the console session on WinSta0\Default, the console's interactive
/// desktop. An RDP client repositions its local cursor from the server's pointer-position message,
/// so with a Moonlight client driving the seat, the seat's pointer was mirrored onto the console
/// user's desktop. The reporter's measurement was decisive: suspend that mstsc and the console
/// cursor stops dead (55 changes -> 0), resume it and it comes back (64).
///
/// It explains every earlier observation, which is how we know it is the right component: the
/// pointer-position message carries a position and NO buttons (movement crossed, clicks did not),
/// it is a real cursor move (hover highlighted), and mstsc is a normal-privilege process on that
/// desktop (an elevated foreground window blocked it via UIPI).
///
/// A process on a different desktop cannot move the console's pointer. Measured here rather than
/// assumed, with the same mover run on both desktops:
///
///     WinSta0\Default          SetCursorPos succeeded 60/60, console cursor moved 39 times
///     WinSta0\MultiSeatKeepalive   SetCursorPos succeeded  0/60, console cursor moved  0 times
///
/// On a non-input desktop the call does not merely land elsewhere - it fails.
///
/// WHY THE DESKTOP IS CREATED FROM A HELPER
///
/// Window stations are per-session: a service in session 0 cannot open the console session's
/// WinSta0, so it cannot create a desktop there. This runs INSIDE the console session as the
/// console user, where CreateDesktop is a local operation. It creates the desktop, starts mstsc on
/// it, records the PID for the service to track, and exits - the running mstsc is what keeps the
/// desktop alive afterwards.
///
/// The throttling question this was gated on is answered. mstsc is positioned on a non-primary
/// monitor because Windows throttles a minimized or hidden RDP client, which freezes the stream;
/// whether a non-interactive desktop counts as hidden was measured with a real client streaming a
/// seat for several minutes - smooth throughout, console cursor 0 changes in 152 samples. On by
/// default since 2026-08-31.
/// </summary>
internal static class KeepaliveDesktopHelper
{
    /// <summary>Name of the desktop, inside the console session's window station.</summary>
    internal const string DesktopName = "MultiSeatKeepalive";

    internal static string QualifiedDesktop => $@"WinSta0\{DesktopName}";

    /// <summary>
    /// Helper entry point: <c>--keepalive-mstsc &lt;address&gt; &lt;pidFile&gt;</c>.
    /// Returns 0 on success. The PID of the launched mstsc is written to <paramref name="pidFile"/>
    /// because the service needs it to tear the seat down later, and there is no return channel
    /// from a process launched with CreateProcessAsUser.
    /// </summary>
    internal static int Run(string address, string pidFile)
    {
        var desktop = CreateDesktopW(
            DesktopName, IntPtr.Zero, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);

        if (desktop == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"CreateDesktop('{DesktopName}') failed: {err}");
            return err == 0 ? -1 : err;
        }

        try
        {
            var si = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = QualifiedDesktop,
                dwFlags = StartfUseShowWindow,
                // SW_SHOW, not SW_HIDE: nothing on this desktop is visible to anyone, and a hidden
                // window is the state Windows throttles. Showing it here costs nothing.
                wShowWindow = SwShow,
            };

            var cmdLine = $"mstsc.exe /v:{address}";

            if (!CreateProcessW(
                    null, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                    CreateUnicodeEnvironment | NormalPriorityClass,
                    IntPtr.Zero, null, ref si, out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"CreateProcess(mstsc on {QualifiedDesktop}) failed: {err}");
                return err;
            }

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);

            // Hold the desktop handle until mstsc has attached to the desktop itself.
            //
            // A desktop dies when its last handle goes, and a process that has been CREATED for it
            // has not necessarily referenced it yet. Measured on the reference host, same code, two
            // timings:
            //
            //     handle closed immediately   mstsc is dead 1.2s later
            //     handle held for 2s          mstsc is alive
            //
            // Closing it here is not optional either - this process exits straight afterwards,
            // which closes it anyway. So the wait is the whole fix, and without it the service
            // sees "Process with an Id of N is not running" and falls back to the console desktop.
            var settled = WaitUntilSettled(pi.dwProcessId, TimeSpan.FromSeconds(3));
            if (!settled)
            {
                Console.Error.WriteLine(
                    $"mstsc {pi.dwProcessId} exited while starting on {QualifiedDesktop}");
                return -1;
            }

            File.WriteAllText(pidFile, pi.dwProcessId.ToString());
            Console.WriteLine($"mstsc {pi.dwProcessId} started on {QualifiedDesktop}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"keepalive helper failed: {ex.Message}");
            return -1;
        }
        finally
        {
            // Closing our handle is safe: mstsc is on the desktop now and holds it open. Leaving
            // it open would keep this process alive for no reason.
            CloseDesktop(desktop);
        }
    }

    /// <summary>
    /// True once the process has been alive continuously for long enough to have taken its own
    /// reference on the desktop. Returns false the moment it exits, so the caller can report a
    /// failure rather than hand back a PID that is already gone.
    /// </summary>
    internal static bool WaitUntilSettled(int pid, TimeSpan settle)
    {
        var deadline = DateTime.UtcNow + settle;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p.HasExited) return false;
            }
            catch (ArgumentException)
            {
                return false;   // already gone
            }
        }
        return true;
    }

    // ── interop ──────────────────────────────────────────────────────

    private const uint DesktopAllAccess = 0x000F01FF;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwShow = 5;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint NormalPriorityClass = 0x00000020;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDesktopW")]
    private static extern IntPtr CreateDesktopW(
        string desktopName, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")]
    private static extern bool CreateProcessW(
        string? applicationName, string commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
