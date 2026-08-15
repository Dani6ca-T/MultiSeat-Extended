using System.Runtime.InteropServices;
using System.Text.Json;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Display;

/// <summary>
/// Reports, per display target, what advanced colour (HDR) the session ADVERTISES versus what is
/// actually ACTIVE.
///
/// The distinction is the whole question. Nonary's finding on issue #15 is that inside a terminal
/// session RdpIdd already publishes an HDR10/10-bpc target — Windows reports advanced colour
/// supported — while the active source stays 32-bpp SDR, so nothing downstream ever sees HDR. If
/// that reproduces here, "a seat cannot do HDR" is wrong in a specific and fixable way; if the
/// target does NOT advertise advanced colour on this host, his premise does not hold for us and
/// the rest of the approach is moot.
///
/// Session-scoped like the rest of the display APIs: run it INSIDE the session being asked about.
/// A SYSTEM service in Session 0 sees no displays at all.
///
/// Usage: MultiSeat.Service.exe --advanced-color &lt;output-json-file&gt;
/// </summary>
internal static class AdvancedColorHelper
{
    public static int RunAndWriteToFile(string outputPath)
    {
        try
        {
            var records = Enumerate();
            File.WriteAllText(outputPath,
                JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));

            Console.Out.WriteLine($"[AdvancedColor] Wrote {records.Count} target(s) to {outputPath}");
            foreach (var r in records)
                Console.Out.WriteLine(
                    $"  {r.GdiName} '{r.FriendlyName}' active={r.Active} " +
                    $"supported={r.AdvancedColorSupported} enabled={r.AdvancedColorEnabled} " +
                    $"bpc={r.BitsPerColorChannel} encoding={r.ColorEncoding}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AdvancedColor] Failed: {ex.Message}");
            return 1;
        }
    }

    internal static List<AdvancedColorRecord> Enumerate()
    {
        var results = new List<AdvancedColorRecord>();

        var ret = User32.GetDisplayConfigBufferSizes(
            User32.QDC_ALL_PATHS, out var numPaths, out var numModes);
        if (ret != User32.ERROR_SUCCESS) return results;

        var paths = new User32.DisplayConfigPathInfo[numPaths];
        var modes = new User32.DisplayConfigModeInfo[numModes];

        ret = User32.QueryDisplayConfig(
            User32.QDC_ALL_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
        if (ret != User32.ERROR_SUCCESS) return results;

        foreach (var path in paths.Take((int)numPaths))
        {
            var targetName = new User32.DisplayConfigTargetDeviceName
            {
                header = new User32.DisplayConfigDeviceInfoHeader
                {
                    type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<User32.DisplayConfigTargetDeviceName>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
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
                    id = path.sourceInfo.id,
                }
            };
            User32.DisplayConfigGetDeviceInfo(ref sourceName);

            var color = new User32.DisplayConfigGetAdvancedColorInfo
            {
                header = new User32.DisplayConfigDeviceInfoHeader
                {
                    type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf<User32.DisplayConfigGetAdvancedColorInfo>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                }
            };
            var colorRet = User32.DisplayConfigGetDeviceInfo(ref color);

            results.Add(new AdvancedColorRecord(
                GdiName: sourceName.viewGdiDeviceName,
                FriendlyName: targetName.monitorFriendlyDeviceName,
                Active: (path.flags & User32.DISPLAYCONFIG_PATH_ACTIVE) != 0,
                TargetAvailable: path.targetInfo.targetAvailable,
                // A non-zero return means the query itself failed for this target, which is
                // different from "queried fine and says unsupported".
                Queried: colorRet == User32.ERROR_SUCCESS,
                AdvancedColorSupported: color.AdvancedColorSupported,
                AdvancedColorEnabled: color.AdvancedColorEnabled,
                WideColorEnforced: color.WideColorEnforced,
                AdvancedColorForceDisabled: color.AdvancedColorForceDisabled,
                BitsPerColorChannel: color.bitsPerColorChannel,
                ColorEncoding: color.colorEncoding,
                TargetId: path.targetInfo.id));
        }

        return results;
    }
}

/// <param name="Queried">False when the advanced-colour query failed for this target.</param>
/// <param name="AdvancedColorSupported">The target ADVERTISES HDR capability.</param>
/// <param name="AdvancedColorEnabled">Advanced colour is ACTUALLY active right now.</param>
internal record AdvancedColorRecord(
    string GdiName,
    string FriendlyName,
    bool Active,
    bool TargetAvailable,
    bool Queried,
    bool AdvancedColorSupported,
    bool AdvancedColorEnabled,
    bool WideColorEnforced,
    bool AdvancedColorForceDisabled,
    uint BitsPerColorChannel,
    uint ColorEncoding,
    uint TargetId);
