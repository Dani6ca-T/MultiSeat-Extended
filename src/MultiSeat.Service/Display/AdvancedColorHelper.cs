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
    /// <summary>
    /// Ask Windows to turn Advanced Color ON for every ACTIVE target, then report what actually
    /// happened.
    ///
    /// Per Nonary on issue #15 this is expected to be insufficient by itself — the target flips
    /// nothing because the VidPN SOURCE mode stays SDR — but "we asked and Windows refused" is a
    /// materially different claim from "we never asked", and until now nobody had asked: the
    /// SetAdvancedColorState interop existed in User32 with no callers at all, so the old
    /// EnableHdr option never invoked it.
    /// </summary>
    /// <summary>
    /// Return code from SetAdvancedColorState per target id, kept so the RESULT of asking is
    /// reported and not just the state afterwards. "Windows refused" and "Windows accepted and
    /// changed nothing" are different findings, and the helper's stdout goes nowhere when it is
    /// launched into a session with CreateProcessAsUser.
    /// </summary>
    private static readonly Dictionary<uint, uint> SetResults = [];

    private static void TryEnableOnActiveTargets(bool enable = true)
    {
        var ret = User32.GetDisplayConfigBufferSizes(
            User32.QDC_ALL_PATHS, out var numPaths, out var numModes);
        if (ret != User32.ERROR_SUCCESS) return;

        var paths = new User32.DisplayConfigPathInfo[numPaths];
        var modes = new User32.DisplayConfigModeInfo[numModes];
        if (User32.QueryDisplayConfig(User32.QDC_ALL_PATHS, ref numPaths, paths,
                ref numModes, modes, IntPtr.Zero) != User32.ERROR_SUCCESS)
            return;

        foreach (var path in paths.Take((int)numPaths))
        {
            if ((path.flags & User32.DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;

            var set = new User32.DisplayConfigSetAdvancedColorState
            {
                header = new User32.DisplayConfigDeviceInfoHeader
                {
                    type = User32.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                    size = (uint)Marshal.SizeOf<User32.DisplayConfigSetAdvancedColorState>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                },
                value = enable ? 1u : 0u, // bit 0 = advanced colour on/off
            };

            var rc = User32.DisplayConfigSetDeviceInfo(ref set);
            SetResults[path.targetInfo.id] = rc;
            Console.Out.WriteLine(
                $"[AdvancedColor] SetAdvancedColorState(target {path.targetInfo.id}, " +
                $"{(enable ? "enable" : "disable")}) returned {rc}" +
                (rc == User32.ERROR_SUCCESS ? " (success)" : " (failed)"));
        }
    }

    /// <param name="setState">
    /// null = read only; true = ask Windows to enable advanced colour on active targets first;
    /// false = ask it to disable. Disable exists so a probe that succeeds can be undone — the
    /// console control here really does switch a display to 10-bit.
    /// </param>
    public static int RunAndWriteToFile(string outputPath, bool? setState = null)
    {
        try
        {
            if (setState is not null)
            {
                Console.Out.WriteLine(
                    $"[AdvancedColor] Asking Windows to {(setState.Value ? "enable" : "disable")} " +
                    "Advanced Color on active targets...");
                TryEnableOnActiveTargets(setState.Value);
                Thread.Sleep(1500); // let any mode change settle before re-reading
            }

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
                TargetId: path.targetInfo.id,
                SetAdvancedColorResult: SetResults.TryGetValue(path.targetInfo.id, out var sr)
                    ? sr
                    : null));
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
    uint TargetId,
    /// <summary>
    /// Win32 return of SetAdvancedColorState when an enable was attempted for this target
    /// (0 = ERROR_SUCCESS), or null when no attempt was made.
    /// </summary>
    uint? SetAdvancedColorResult = null);
