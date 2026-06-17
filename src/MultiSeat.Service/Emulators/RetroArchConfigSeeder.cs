using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Storage;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Emulators;

/// <summary>
/// Seeds a seat's <c>retroarch.cfg</c> with its assigned netplay host port and the shared ROM
/// directory, so two seats can netplay over loopback without manual port juggling and both sides
/// load identical content (netplay requires matching core + content CRC).
///
/// Writes into the seat user's profile (mirrors the RustDesk config seed in SeatManager step 2.5).
/// </summary>
public sealed class RetroArchConfigSeeder : IEmulatorConfigSeeder
{
    private readonly ILogger<RetroArchConfigSeeder> _logger;
    private readonly MultiSeatOptions _options;
    private readonly SharedLibraryProvisioner _sharedLibrary;

    public RetroArchConfigSeeder(
        ILogger<RetroArchConfigSeeder> logger,
        IOptions<MultiSeatOptions> options,
        SharedLibraryProvisioner sharedLibrary)
    {
        _logger = logger;
        _options = options.Value;
        _sharedLibrary = sharedLibrary;
    }

    public string EmulatorName => "RetroArch";
    public bool IsEnabled => _options.SeedRetroArchNetplayConfig;

    public async Task SeedAsync(SeatInfo seat, CancellationToken ct)
    {
        if (seat.RetroArchNetplayPort <= 0)
            return;

        try
        {
            var cfgPath = ResolveConfigPath(seat);
            Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);

            var cfg = File.Exists(cfgPath)
                ? await File.ReadAllTextAsync(cfgPath, ct)
                : string.Empty;

            cfg = UpsertCfgKey(cfg, "netplay_ip_port", seat.RetroArchNetplayPort.ToString());
            cfg = UpsertCfgKey(cfg, "netplay_public_announce", "false");
            cfg = UpsertCfgKey(cfg, "netplay_nat_traversal", "false");
            cfg = UpsertCfgKey(cfg, "rgui_browser_directory", _sharedLibrary.RomsDir);

            await File.WriteAllTextAsync(cfgPath, cfg, ct);

            _logger.LogInformation(
                "Seat {Id}: seeded RetroArch netplay port {Port} + ROM dir into {Path}",
                seat.Id, seat.RetroArchNetplayPort, cfgPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Seat {Id}: RetroArch config seed failed (non-critical)", seat.Id);
        }
    }

    private string ResolveConfigPath(SeatInfo seat)
    {
        if (!string.IsNullOrWhiteSpace(_options.RetroArchConfigPath))
            return _options.RetroArchConfigPath;

        return Path.Combine(
            @"C:\Users", seat.AccountName,
            @"AppData\Roaming\RetroArch\retroarch.cfg");
    }

    /// <summary>
    /// Upsert a <c>key = "value"</c> entry into a RetroArch config. Replaces an existing entry for
    /// the key (matched as a whole token, so "netplay_ip_port" does not match "netplay_ip_port_x")
    /// or appends a new one, preserving all other lines.
    /// </summary>
    public static string UpsertCfgKey(string cfg, string key, string value)
    {
        var newLine = $"{key} = \"{value}\"";
        var normalized = cfg.Replace("\r\n", "\n");
        var lines = normalized.Length == 0
            ? new List<string>()
            : normalized.Split('\n').ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith(key, StringComparison.Ordinal) &&
                (t.Length == key.Length || t[key.Length] is ' ' or '\t' or '='))
            {
                lines[i] = newLine;
                return string.Join("\n", lines);
            }
        }

        // Drop a single trailing empty line (from a file ending in newline) before appending,
        // so we don't introduce a blank gap before the new key.
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        lines.Add(newLine);
        return string.Join("\n", lines);
    }
}
