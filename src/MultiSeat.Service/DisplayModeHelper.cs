using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service;

/// <summary>
/// Called when MultiSeat.Service.exe is invoked with --set-display-hz {hz}.
/// Runs inside a seat's RDP session (via CreateProcessAsUser) and calls
/// ChangeDisplaySettingsEx(null, ...) which targets the primary display of
/// the calling session — the "Microsoft Remote Display Adapter".
/// </summary>
internal static class DisplayModeHelper
{
    internal static bool SetPrimaryDisplayHz(int hz)
    {
        return SetDisplayHz(null, hz);
    }

    /// <summary>
    /// Set the refresh rate of a specific GDI display device.
    /// Pass the GDI device name (e.g. \\.\DISPLAY5) from the console session to
    /// target a SudoVDA virtual display. Must be called from within the console
    /// session — IddCx Console displays are only accessible from WinSta0\Default.
    /// </summary>
    internal static bool SetDisplayHz(string? deviceName, int hz)
    {
        var devMode = new User32.DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<User32.DEVMODE>(),
            dmFields = User32.DM_DISPLAYFREQUENCY,
            dmDisplayFrequency = (uint)hz,
        };

        var result = User32.ChangeDisplaySettingsEx(
            deviceName,
            ref devMode,
            IntPtr.Zero,
            User32.CDS_UPDATEREGISTRY,
            IntPtr.Zero);

        return result == User32.DISP_CHANGE_SUCCESSFUL;
    }
}
