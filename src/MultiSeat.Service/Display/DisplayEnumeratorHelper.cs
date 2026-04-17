using System.Runtime.InteropServices;
using System.Text.Json;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Display;

/// <summary>
/// Runs in --enum-displays helper mode (console session, via CreateProcessAsUser).
/// QueryDisplayConfig is session-scoped — Session 0 (the SYSTEM service) has no
/// displays attached. This helper is launched inside the console session so it sees
/// the full display topology including SudoVDA virtual displays.
/// </summary>
internal static class DisplayEnumeratorHelper
{
    /// <summary>
    /// Entry point for --enum-displays helper mode.
    /// Enumerates all display paths and writes them as JSON to the given file.
    /// </summary>
    internal static int RunAndWriteToFile(string outputPath)
    {
        try
        {
            var records = EnumerateAllPaths();
            var json = JsonSerializer.Serialize(records);
            File.WriteAllText(outputPath, json);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Enumerate every display path from QueryDisplayConfig (QDC_ALL_PATHS).
    /// Should be called from the console session only.
    /// </summary>
    internal static List<DisplayPathRecord> EnumerateAllPaths()
    {
        var results = new List<DisplayPathRecord>();

        var ret = User32.GetDisplayConfigBufferSizes(
            User32.QDC_ALL_PATHS, out var numPaths, out var numModes);
        if (ret != User32.ERROR_SUCCESS)
            return results;

        var paths = new User32.DisplayConfigPathInfo[numPaths];
        var modes = new User32.DisplayConfigModeInfo[numModes];

        ret = User32.QueryDisplayConfig(
            User32.QDC_ALL_PATHS,
            ref numPaths, paths,
            ref numModes, modes,
            IntPtr.Zero);
        if (ret != User32.ERROR_SUCCESS)
            return results;

        foreach (var path in paths.Take((int)numPaths))
        {
            var targetName = new User32.DisplayConfigTargetDeviceName
            {
                header = new User32.DisplayConfigDeviceInfoHeader
                {
                    type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<User32.DisplayConfigTargetDeviceName>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            User32.DisplayConfigGetDeviceInfo(ref targetName);

            var sourceName = new User32.DisplayConfigSourceDeviceName
            {
                header = new User32.DisplayConfigDeviceInfoHeader
                {
                    type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<User32.DisplayConfigSourceDeviceName>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            User32.DisplayConfigGetDeviceInfo(ref sourceName);

            results.Add(new DisplayPathRecord(
                GdiName:         sourceName.viewGdiDeviceName,
                FriendlyName:    targetName.monitorFriendlyDeviceName,
                DevicePath:      targetName.monitorDevicePath,
                TargetAvailable: path.targetInfo.targetAvailable,
                Active:          (path.flags & User32.DISPLAYCONFIG_PATH_ACTIVE) != 0,
                AdapterLow:      path.targetInfo.adapterId.LowPart,
                AdapterHigh:     path.targetInfo.adapterId.HighPart,
                TargetId:        path.targetInfo.id));
        }

        return results;
    }
}

/// <summary>
/// Display path data written by the --enum-displays helper and read back by VirtualDisplayManager.
/// </summary>
internal record DisplayPathRecord(
    string GdiName,
    string FriendlyName,
    string DevicePath,
    bool   TargetAvailable,
    bool   Active,
    uint   AdapterLow,
    int    AdapterHigh,
    uint   TargetId);
