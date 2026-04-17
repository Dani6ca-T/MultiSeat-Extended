using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using MultiSeat.Service.Configuration; // kept for IOptions<MultiSeatOptions> in constructor
using MultiSeat.Service.Interop;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Display;

/// <summary>
/// Tracks SudoVDA (Virtual Display Adapter) state for each seat.
///
/// Apollo manages the virtual display lifecycle directly via always_use_virtual_display=enabled —
/// each Apollo instance creates and owns one SudoVDA virtual monitor when it starts, and releases
/// it when it stops. MultiSeat does not need to enumerate or assign displays.
///
/// Responsibilities of this manager:
///   - Negotiate seat resolution/fps (stored in the Apollo config)
///   - Detect whether the SudoVDA driver adapter is installed (for health checks)
///   - Expose diagnostic display enumeration via the console-session helper
/// </summary>
public sealed class VirtualDisplayManager
{
    private readonly ILogger<VirtualDisplayManager> _logger;
    private readonly ProcessInjector _processInjector;

    // Seat → assigned virtual display
    private readonly ConcurrentDictionary<Guid, VirtualDisplay> _displays = new();

    // Path to the service executable — used to launch the --enum-displays helper
    private static readonly string ServiceExePath =
        Path.Combine(AppContext.BaseDirectory, "MultiSeat.Service.exe");

    public VirtualDisplayManager(
        ILogger<VirtualDisplayManager> logger,
        IOptions<MultiSeatOptions> options,
        ProcessInjector processInjector)
    {
        _logger = logger;
        _processInjector = processInjector;
        _ = options; // kept in constructor signature for DI compatibility
    }

    /// <summary>
    /// True if the SudoVDA driver adapter is present in the system (PnP device exists).
    /// Checks the PnP registry directly — works from Session 0 without any display API calls.
    /// The adapter entry persists as long as the driver is installed, regardless of whether
    /// Apollo is currently running (Apollo creates/destroys the virtual monitors, not the adapter).
    /// </summary>
    public bool IsDriverAvailable => IsSudoVdaAdapterPresent();

    /// <summary>
    /// Diagnostic: enumerate every connected display path and return their names.
    /// Used by GET /api/system/displays to help diagnose SudoVDA detection issues.
    /// Runs via the console-session helper for accurate results.
    /// </summary>
    public IReadOnlyList<object> EnumerateAllConnectedPaths()
    {
        var records = RunEnumDisplaysHelper();
        return records.Select(r => (object)new
        {
            r.GdiName,
            r.FriendlyName,
            r.DevicePath,
            r.Active,
            r.TargetAvailable,
            isSudoVda = IsSudoVdaRecord(r)
        }).ToList();
    }

    /// <summary>
    /// Number of seats with an assigned virtual display.
    /// </summary>
    public int ActiveDisplayCount => _displays.Count;

    /// <summary>
    /// Prepare display settings for a seat.
    /// Apollo owns the SudoVDA virtual display lifecycle via always_use_virtual_display=enabled —
    /// each Apollo instance creates its own virtual monitor when it starts. MultiSeat only needs
    /// to negotiate the resolution/fps so those values are written correctly into the Apollo config.
    /// </summary>
    public Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct)
    {
        var (width, height, fps) = ResolutionNegotiator.Negotiate(
            seat.Width, seat.Height, seat.Fps);

        seat.Width  = width;
        seat.Height = height;
        seat.Fps    = fps;

        // DevicePath is null — Apollo manages the virtual monitor internally.
        // The GDI device name is not needed since output_name is no longer set in the config.
        _displays[seat.Id] = new VirtualDisplay(
            SeatId:    seat.Id,
            DevicePath: null,
            Width:  width,
            Height: height,
            Fps:    fps,
            CreatedAt: DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Seat {Id}: display configured {W}x{H}@{F}Hz — Apollo will create the virtual display",
            seat.Id, width, height, fps);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Release the virtual display assignment for a seat.
    /// Does not destroy the underlying SudoVDA driver display.
    /// </summary>
    public Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct)
    {
        if (_displays.TryRemove(seat.Id, out var display) && display.DevicePath is not null)
        {
            _logger.LogInformation(
                "Seat {Id}: released SudoVDA display {Dev}",
                seat.Id, display.DevicePath);
        }

        seat.DisplayDevicePath = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the display info for a seat.
    /// </summary>
    public VirtualDisplay? GetDisplay(Guid seatId)
    {
        return _displays.GetValueOrDefault(seatId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DISPLAY ENUMERATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Launch MultiSeat.Service.exe --enum-displays in the console session,
    /// wait for it to write the JSON result file, and return the parsed records.
    /// Falls back to empty list on any error.
    /// </summary>
    private IReadOnlyList<DisplayPathRecord> RunEnumDisplaysHelper()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ms_displays_{Guid.NewGuid():N}.json");
        try
        {
            var exitCode = _processInjector
                .RunInConsoleSessionAsync(ServiceExePath, $"--enum-displays \"{tmpFile}\"", 8000, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (exitCode != 0)
            {
                _logger.LogWarning(
                    "--enum-displays helper exited with code {Code}", exitCode);
                return [];
            }

            if (!File.Exists(tmpFile))
            {
                _logger.LogWarning("--enum-displays helper did not write output file");
                return [];
            }

            var json = File.ReadAllText(tmpFile);
            return JsonSerializer.Deserialize<List<DisplayPathRecord>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run --enum-displays helper");
            return [];
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check whether the SudoVDA PnP adapter is installed by reading the registry directly.
    /// SudoVDA registers as ROOT\DISPLAY\0000 (IddCx virtual adapter) with a DeviceDesc
    /// containing "SudoMaker" or "SudoVDA". This key persists as long as the driver is
    /// installed — it does NOT require Apollo to be running.
    /// Registry access is not session-scoped, so this works correctly from Session 0.
    /// </summary>
    private bool IsSudoVdaAdapterPresent()
    {
        // The SudoVDA adapter enumerates under ROOT\DISPLAY. Apollo's fork installs a single
        // adapter at ROOT\DISPLAY\0000. Walk all sub-keys (0000, 0001, …) in case of multi-adapter.
        const string enumRoot = @"SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY";
        try
        {
            using var displayKey = Registry.LocalMachine.OpenSubKey(enumRoot);
            if (displayKey is null) return false;

            foreach (var subName in displayKey.GetSubKeyNames())
            {
                using var devKey = displayKey.OpenSubKey(subName);
                if (devKey is null) continue;

                var deviceDesc = devKey.GetValue("DeviceDesc") as string ?? string.Empty;
                var hardwareIds = devKey.GetValue("HardwareID") as string[] ?? [];

                // DeviceDesc may be "@oem*.inf,%str%;Description" format — check raw string
                if (deviceDesc.Contains("SudoMaker", StringComparison.OrdinalIgnoreCase) ||
                    deviceDesc.Contains("SudoVDA", StringComparison.OrdinalIgnoreCase) ||
                    hardwareIds.Any(id =>
                        id.Contains("SudoMaker", StringComparison.OrdinalIgnoreCase) ||
                        id.Contains("SudoVDA", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug(
                        "SudoVDA adapter found in PnP registry: ROOT\\DISPLAY\\{Key} ({Desc})",
                        subName, deviceDesc);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check SudoVDA PnP registry key");
        }

        return false;
    }

    /// <summary>
    /// Identify SudoVDA virtual displays by their EDID manufacturer ID ("MTT")
    /// or friendly name ("VDD by MTT"). The device path also contains "MTT1337"
    /// (MTT = manufacturer, 1337 = product code baked into SudoVDA's EDID).
    /// If /api/system/displays shows different names, update the patterns here.
    /// </summary>
    private static bool IsSudoVdaRecord(DisplayPathRecord r) =>
        r.DevicePath.Contains("MTT", StringComparison.OrdinalIgnoreCase) ||
        r.FriendlyName.Contains("VDD", StringComparison.OrdinalIgnoreCase) ||
        r.FriendlyName.Contains("SudoVDA", StringComparison.OrdinalIgnoreCase) ||
        r.FriendlyName.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tracks a virtual display assignment for a seat.
/// </summary>
public sealed record VirtualDisplay(
    Guid SeatId,
    string? DevicePath,
    int Width,
    int Height,
    int Fps,
    DateTimeOffset CreatedAt);
