using System.Diagnostics;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Scans for orphaned Apollo processes on service startup.
///
/// After a service crash or unclean shutdown, the previous instance's Apollo
/// processes may still be running. This detector identifies them so they can be
/// adopted or terminated.
///
/// DETECTION METHOD:
///   1. Scan all processes matching the Apollo executable name
///   2. For each, try to correlate to a known seat by config directory path
///   3. Log findings for operator review
///
/// SAFETY:
///   - Only identifies orphans, does NOT kill them
///   - Killing requires operator confirmation (future: auto-kill with confirmation)
///   - Never kills processes belonging to standalone Apollo instances
///
/// CORRELATION:
///   The config path in the command line (e.g. --config "C:\...\sunshine.conf")
///   identifies which seat the process belongs to. If the config directory matches
///   a known seat's config directory, the process is a candidate orphan for that seat.
/// </summary>
public sealed class StartupOrphanDetector
{
    private readonly ILogger<StartupOrphanDetector> _logger;
    private readonly MultiSeatOptions _options;

    public StartupOrphanDetector(
        ILogger<StartupOrphanDetector> logger,
        IOptions<MultiSeatOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Scan for orphaned provider processes and return a list of candidates.
    /// Does NOT kill any processes — returns information for the caller to decide.
    /// </summary>
    public IReadOnlyList<OrphanCandidate> DetectOrphans(IEnumerable<SeatInfo> knownSeats)
    {
        var candidates = new List<OrphanCandidate>();

        try
        {
            var exeName = Path.GetFileNameWithoutExtension(_options.ApolloExePath);
            var processes = Process.GetProcessesByName(exeName);

            foreach (var proc in processes)
            {
                try
                {
                    if (proc.HasExited) continue;

                    var commandLine = GetCommandLine(proc.Id);
                    var configDir = ExtractConfigDirectory(commandLine);

                    // Try to correlate to a known seat by matching config directory paths.
                    // Uses full path comparison (not substring of AccountName) to reduce
                    // false positives from orphan detection (L4 fix).
                    Guid? matchedSeatId = null;
                    if (configDir != null)
                    {
                        var normalizedConfigDir = Path.GetFullPath(configDir)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var matched = knownSeats
                            .FirstOrDefault(s =>
                                normalizedConfigDir.EndsWith(
                                    $@"{Path.DirectorySeparatorChar}{s.AccountName}",
                                    StringComparison.OrdinalIgnoreCase) ||
                                normalizedConfigDir.EndsWith(
                                    $@"{Path.AltDirectorySeparatorChar}{s.AccountName}",
                                    StringComparison.OrdinalIgnoreCase));
                        if (matched != null && matched.Id != Guid.Empty)
                            matchedSeatId = matched.Id;
                    }

                    var candidate = new OrphanCandidate
                    {
                        ProcessId = proc.Id,
                        ProcessName = proc.ProcessName,
                        StartedAt = proc.StartTime.ToUniversalTime(),
                        CommandLine = commandLine,
                        ConfigDirectory = configDir,
                        MatchedSeatId = matchedSeatId,
                        MatchedSeatAccount = matchedSeatId.HasValue
                            ? knownSeats.FirstOrDefault(s => s.Id == matchedSeatId)?.AccountName
                            : null
                    };

                    candidates.Add(candidate);

                    if (matchedSeatId.HasValue)
                    {
                        _logger.LogWarning(
                            "Orphan candidate: PID {Pid} ({Name}) started at {Time} — " +
                            "matches seat {SeatId} (account {Account}) via config path {Config}",
                            proc.Id, proc.ProcessName, candidate.StartedAt,
                            matchedSeatId, candidate.MatchedSeatAccount, configDir);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Orphan candidate: PID {Pid} ({Name}) started at {Time} — " +
                            "no seat match (standalone instance?)",
                            proc.Id, proc.ProcessName, candidate.StartedAt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Could not inspect orphan candidate PID {Pid}", proc.Id);
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan for orphan processes");
        }

        return candidates.AsReadOnly();
    }

    /// <summary>
    /// Try to read the command line of a process.
    /// Returns null if not accessible (access denied).
    /// </summary>
    private string? GetCommandLine(int pid)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString();
            }
        }
        catch
        {
            // WMI access denied or process gone
        }
        return null;
    }

    /// <summary>
    /// Extract the config directory from an Apollo command line.
    /// Apollo uses --config or -c followed by a path.
    /// </summary>
    private static string? ExtractConfigDirectory(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        // Look for --config or -c followed by a path
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-c", StringComparison.OrdinalIgnoreCase))
            {
                var configPath = args[i + 1].Trim('"');
                return Path.GetDirectoryName(configPath);
            }
        }
        return null;
    }
}

/// <summary>
/// Information about a candidate orphan process found during startup scan.
/// </summary>
public sealed class OrphanCandidate
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public string? CommandLine { get; init; }
    public string? ConfigDirectory { get; init; }
    public Guid? MatchedSeatId { get; init; }
    public string? MatchedSeatAccount { get; init; }
}
