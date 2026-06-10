using System.Text.Json;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Configuration;

/// <summary>
/// Persists seat presets to C:\ProgramData\MultiSeat\seat-presets.json.
/// Thread-safe; survives service restarts.
/// </summary>
public sealed class SeatPresetStore
{
    private readonly string _filePath = @"C:\ProgramData\MultiSeat\seat-presets.json";
    private readonly ILogger<SeatPresetStore> _logger;
    private readonly object _lock = new();
    private List<SeatPreset> _presets = [];

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SeatPresetStore(ILogger<SeatPresetStore> logger)
    {
        _logger = logger;
        Load();
    }

    public IReadOnlyList<SeatPreset> GetAll()
    {
        lock (_lock) return [.. _presets];
    }

    public IReadOnlyList<SeatPreset> GetAutoStart() =>
        GetAll().Where(p => p.AutoStart).ToList();

    public SeatPreset? GetByAccount(string accountName) =>
        GetAll().FirstOrDefault(p =>
            p.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Create or update a preset (matched by AccountName).
    /// </summary>
    public SeatPreset Upsert(SeatPreset preset)
    {
        lock (_lock)
        {
            var idx = _presets.FindIndex(p =>
                p.AccountName.Equals(preset.AccountName, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                // Preserve original Id and CreatedAt
                preset.Id = _presets[idx].Id;
                preset.CreatedAt = _presets[idx].CreatedAt;
                _presets[idx] = preset;
            }
            else
            {
                _presets.Add(preset);
            }

            Save();
        }
        return preset;
    }

    public bool DeleteByAccount(string accountName)
    {
        lock (_lock)
        {
            var removed = _presets.RemoveAll(p =>
                p.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save();
            return removed;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _presets = JsonSerializer.Deserialize<List<SeatPreset>>(json, _json) ?? [];
            _logger.LogInformation("Loaded {Count} seat preset(s) from {Path}",
                _presets.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load seat presets from {Path} — starting empty", _filePath);
            _presets = [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // Write-then-rename so a crash mid-write can't truncate the existing file.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_presets, _json));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save seat presets to {Path}", _filePath);
        }
    }
}
