using System.Diagnostics;
using System.Management;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Interop;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Monitoring;

/// <summary>
/// Reports on the host's own standalone Apollo — the instance MultiSeat coexists with and
/// deliberately never touches (see MultiSeatWorker.KillOrphanedApolloProcesses).
///
/// Detection is the exact inverse of the cleanup rule: an Apollo is OURS if it runs from the
/// configured MultiSeat Apollo install dir or was launched with a per-seat MultiSeat config, so
/// anything else is the host's. Keeping the two definitions mirrored matters — if they ever
/// disagree, MultiSeat would either report the host's Apollo as a seat's or, far worse, treat
/// the host's as an orphan to reap.
/// </summary>
public sealed partial class HostApolloMonitor
{
    private readonly MultiSeatOptions _options;
    private readonly ILogger<HostApolloMonitor> _logger;
    private readonly HttpClient _http;

    /// <summary>Apollo's default <c>port</c> when its config does not say otherwise.</summary>
    private const int DefaultApolloPort = 47989;

    public HostApolloMonitor(IOptions<MultiSeatOptions> options, ILogger<HostApolloMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Short timeout on purpose: this runs behind a dashboard poll, and a hung local query
        // must not stall the page. An unreachable Apollo is a legitimate answer, not an error.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<HostApolloInfo> CollectAsync(CancellationToken ct = default)
    {
        var info = new HostApolloInfo
        {
            ConsoleSessionId = (int)Kernel32.WTSGetActiveConsoleSessionId(),
            ServiceStatus = QueryApolloServiceStatus(),
        };

        var host = FindStandaloneApollo();
        if (host is null)
        {
            info.Note = info.ServiceStatus is null
                ? "No standalone Apollo running. MultiSeat does not require one — this is only " +
                  "the Apollo you would run for the console account itself."
                : $"No standalone Apollo process running (ApolloService is {info.ServiceStatus}).";
            return info;
        }

        info.Detected = true;
        info.ProcessId = host.Value.Pid;
        info.ExecutablePath = host.Value.ExePath;
        info.StartedAt = host.Value.StartedAt;

        var port = ResolvePort(host.Value.ExePath);
        info.Port = port;
        info.WebUiPort = port + 1;

        await QueryServerInfoAsync(info, port, ct);
        return info;
    }

    /// <summary>
    /// The first Apollo process that is not MultiSeat's. Mirrors the ownership test used by
    /// startup cleanup. Returns null when only MultiSeat's own instances are running.
    /// </summary>
    private (int Pid, string? ExePath, DateTimeOffset? StartedAt)? FindStandaloneApollo()
    {
        var exeName = Path.GetFileNameWithoutExtension(_options.ApolloExePath); // "sunshine"
        var managedExeDir = Path.GetDirectoryName(_options.ApolloExePath);
        var managedConfigDir = _options.ApolloConfigDir;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = '{exeName}.exe'");

            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                var pid = Convert.ToInt32(o["ProcessId"]);
                var exePath = o["ExecutablePath"] as string;
                var cmdLine = o["CommandLine"] as string;

                if (IsMultiSeatManaged(exePath, cmdLine, managedExeDir, managedConfigDir))
                    continue;

                DateTimeOffset? started = null;
                try { started = Process.GetProcessById(pid).StartTime; } catch { /* raced or denied */ }

                return (pid, exePath, started);
            }
        }
        catch (Exception ex)
        {
            // Same fail-safe posture as cleanup: report nothing rather than guess.
            _logger.LogDebug(ex, "WMI query for standalone Apollo failed");
        }

        return null;
    }

    private static bool IsMultiSeatManaged(
        string? exePath, string? cmdLine, string? managedExeDir, string? managedConfigDir)
    {
        if (!string.IsNullOrEmpty(exePath) && !string.IsNullOrEmpty(managedExeDir)
            && exePath.StartsWith(managedExeDir, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(cmdLine) && !string.IsNullOrEmpty(managedConfigDir)
            && cmdLine.Contains(managedConfigDir, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Apollo's <c>port</c> from the config next to its executable, or the documented default.
    /// Everything else Moonlight uses is derived from it (web UI is port+1, HTTPS is port-5).
    /// </summary>
    private int ResolvePort(string? exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return DefaultApolloPort;

            var conf = Path.Combine(dir, "config", "sunshine.conf");
            if (!File.Exists(conf)) return DefaultApolloPort;

            foreach (var line in File.ReadLines(conf))
            {
                var m = PortLineRegex().Match(line);
                if (m.Success && int.TryParse(m.Groups["port"].Value, out var p))
                    return p;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the standalone Apollo config; assuming default port");
        }

        return DefaultApolloPort;
    }

    /// <summary>
    /// Ask Apollo the same question Moonlight asks. This is the difference between "a process
    /// exists" and "a client could actually use it", which is the part worth showing.
    /// </summary>
    private async Task QueryServerInfoAsync(HostApolloInfo info, int port, CancellationToken ct)
    {
        try
        {
            var url = $"http://127.0.0.1:{port}/serverinfo?uniqueid=multiseat-dashboard";
            var xml = await _http.GetStringAsync(url, ct);

            info.Reachable = true;
            info.HostName = Tag(xml, "hostname");
            info.AppVersion = Tag(xml, "appversion");

            // state is SUNSHINE_SERVER_FREE when idle; currentgame is 0 when nothing is running.
            var state = Tag(xml, "state");
            var currentGame = Tag(xml, "currentgame");
            info.Streaming =
                (state is not null && !state.EndsWith("FREE", StringComparison.OrdinalIgnoreCase))
                || (currentGame is not null && currentGame != "0");

            info.Paired = Tag(xml, "PairStatus") == "1";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            info.Note =
                $"Apollo is running (PID {info.ProcessId}) but did not answer on port {port}. " +
                "It may still be starting, or be configured on a different port.";
            _logger.LogDebug(ex, "Standalone Apollo serverinfo query failed on port {Port}", port);
        }
    }

    private static string? Tag(string xml, string tag)
    {
        var m = Regex.Match(xml, $"<{Regex.Escape(tag)}>(.*?)</{Regex.Escape(tag)}>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>ApolloService state, or null when the service is not installed.</summary>
    private string? QueryApolloServiceStatus()
    {
        try
        {
            using var sc = new ServiceController("ApolloService");
            return sc.Status.ToString();
        }
        catch
        {
            return null; // not installed — normal on a host that runs Apollo manually
        }
    }

    [GeneratedRegex(@"^\s*port\s*=\s*(?<port>\d{2,5})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortLineRegex();
}
