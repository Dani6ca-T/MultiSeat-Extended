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

    /// <summary>
    /// Run inside a seat's RDP session to isolate TermService encoding overhead.
    ///
    /// The seat session has two displays:
    ///   1. Microsoft Remote Display Adapter — the RDP virtual display that mstsc
    ///      renders. TermService software-encodes this display and streams it to mstsc,
    ///      consuming 60–80% CPU when game content is visible on it.
    ///   2. SudoVDA virtual display — the IddCx display Apollo creates and captures.
    ///      Apollo uses NVENC (hardware), so this costs ~0 CPU regardless of content.
    ///
    /// This method:
    ///   1. Finds the seat's SudoVDA by matching <paramref name="sudoVdaIddCxPath"/> (Apollo's
    ///      output_name / SeatInfo.DisplayDevicePath) against each active adapter's monitor
    ///      child DeviceID. Without this anchor, an orphan SudoVDA attached to another session
    ///      (e.g. console) can be matched instead, dragging that session's resolution along
    ///      with the seat's. Bails out early if no match — no broad pattern fallback.
    ///   2. Finds the RDP virtual display by DeviceID prefix (ROOT\RDP_VDD / ROOT\BasicDisplay)
    ///      and DeviceString as a last resort, restricted to the same enumeration pass.
    ///   3. Makes SudoVDA the PRIMARY display (position 0,0).
    ///   4. Shrinks the RDP display to 640×480 and moves it off to the right as a secondary —
    ///      TermService now only encodes 640×480 pixels of a static desktop.
    ///
    /// All changes are applied atomically using the CDS_NORESET / batch-commit pattern.
    /// Returns exit code 0 on success, 1 on failure (non-fatal — logged by SeatManager).
    /// </summary>
    internal static int SetupDisplayIsolation(string? sudoVdaIddCxPath)
    {
        if (string.IsNullOrWhiteSpace(sudoVdaIddCxPath))
        {
            Console.Error.WriteLine(
                "[DisplayIsolation] No SudoVDA device path provided — refusing to guess. " +
                "Pass SeatInfo.DisplayDevicePath as the second argument.");
            return 1;
        }

        // Enumerate active adapters in this session and pin SudoVDA via its monitor child's
        // IddCx DeviceID. EnumDisplayDevices(adapterName, 0, ...) returns the monitor instance
        // attached to that adapter; its DeviceID embeds the IddCx path Apollo logged.
        string rdpDevice = string.Empty;
        string sudoVdaDevice = string.Empty;

        var dd = new User32.DisplayDevice { cb = Marshal.SizeOf<User32.DisplayDevice>() };
        for (uint i = 0; User32.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & User32.DISPLAY_DEVICE_ACTIVE) == 0)
            {
                dd.cb = Marshal.SizeOf<User32.DisplayDevice>();
                continue;
            }

            // RDP virtual display: ROOT\RDP_VDD (seat session) or ROOT\BasicDisplay (fallback).
            // Keep DeviceString match as a safety net for adapter-name variations across builds.
            var deviceId = dd.DeviceID ?? string.Empty;
            var deviceString = dd.DeviceString ?? string.Empty;
            if (deviceId.StartsWith(@"ROOT\RDP_VDD", StringComparison.OrdinalIgnoreCase) ||
                deviceId.Equals(@"ROOT\BasicDisplay", StringComparison.OrdinalIgnoreCase) ||
                deviceString.Contains("Microsoft Remote Display", StringComparison.OrdinalIgnoreCase))
            {
                rdpDevice = dd.DeviceName;
            }
            else if (string.IsNullOrEmpty(sudoVdaDevice) &&
                     deviceId.StartsWith(@"ROOT\SudoMaker", StringComparison.OrdinalIgnoreCase))
            {
                // Multiple SudoMaker adapter entries (1 per virtual monitor slot) can be active
                // across sessions. Disambiguate by checking the monitor child's DeviceID
                // against the seat's known IddCx path. Only set sudoVdaDevice on a real match.
                var mon = new User32.DisplayDevice { cb = Marshal.SizeOf<User32.DisplayDevice>() };
                if (User32.EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0))
                {
                    var monId = mon.DeviceID ?? string.Empty;
                    if (monId.Contains(sudoVdaIddCxPath, StringComparison.OrdinalIgnoreCase) ||
                        sudoVdaIddCxPath.Contains(monId, StringComparison.OrdinalIgnoreCase))
                    {
                        sudoVdaDevice = dd.DeviceName;
                    }
                }
            }
            dd.cb = Marshal.SizeOf<User32.DisplayDevice>();
        }

        if (string.IsNullOrEmpty(sudoVdaDevice) || string.IsNullOrEmpty(rdpDevice))
        {
            Console.Error.WriteLine(
                $"[DisplayIsolation] Could not find both displays. RDP='{rdpDevice}' SudoVDA='{sudoVdaDevice}' " +
                $"(looking for SudoVDA matching '{sudoVdaIddCxPath}')");
            return 1;
        }

        Console.WriteLine($"[DisplayIsolation] RDP display: {rdpDevice}  SudoVDA: {sudoVdaDevice} (matched {sudoVdaIddCxPath})");

        if (!GetCurrentDevMode(sudoVdaDevice, out var sudoMode) ||
            !GetCurrentDevMode(rdpDevice, out var rdpMode))
        {
            Console.Error.WriteLine("[DisplayIsolation] Failed to read current display modes.");
            return 1;
        }

        // Step 1: Set SudoVDA as primary at position (0, 0).
        sudoMode.dmPositionX = 0;
        sudoMode.dmPositionY = 0;
        sudoMode.dmFields |= User32.DM_POSITION;
        var r1 = User32.ChangeDisplaySettingsEx(
            sudoVdaDevice, ref sudoMode, IntPtr.Zero,
            User32.CDS_SET_PRIMARY | User32.CDS_UPDATEREGISTRY | User32.CDS_NORESET,
            IntPtr.Zero);

        // Step 2: Move RDP display to the right of SudoVDA and shrink to 640×480.
        rdpMode.dmPositionX = (int)sudoMode.dmPelsWidth;
        rdpMode.dmPositionY = 0;
        rdpMode.dmPelsWidth  = 640;
        rdpMode.dmPelsHeight = 480;
        rdpMode.dmFields |= User32.DM_POSITION | User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT;
        var r2 = User32.ChangeDisplaySettingsEx(
            rdpDevice, ref rdpMode, IntPtr.Zero,
            User32.CDS_UPDATEREGISTRY | User32.CDS_NORESET,
            IntPtr.Zero);

        // Step 3: Commit all CDS_NORESET changes atomically.
        var r3 = User32.ChangeDisplaySettingsExApply(
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        Console.WriteLine(
            $"[DisplayIsolation] Results: SudoVDA→primary={r1}, RDP→640x480={r2}, apply={r3}");

        return (r1 == User32.DISP_CHANGE_SUCCESSFUL && r2 == User32.DISP_CHANGE_SUCCESSFUL)
            ? 0 : 1;
    }

    private static bool GetCurrentDevMode(string deviceName, out User32.DEVMODE mode)
    {
        mode = new User32.DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName   = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<User32.DEVMODE>(),
        };
        return User32.EnumDisplaySettingsEx(deviceName, User32.ENUM_CURRENT_SETTINGS, ref mode, 0);
    }
}
