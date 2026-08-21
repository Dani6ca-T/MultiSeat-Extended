using System.Diagnostics;

namespace MultiSeat.Service.Input;

/// <summary>
/// Result of one HidHideCLI invocation.
///
/// <see cref="Answered"/> is the important field. An empty read from this CLI does NOT mean an
/// empty configuration — see <see cref="HidHideCli"/> — so callers must branch on whether the
/// tool answered at all, not on whether the output had content.
/// </summary>
public sealed record HidHideCliResult(int ExitCode, string Output, bool TimedOut, bool AccessDenied)
{
    public bool Succeeded => !TimedOut && ExitCode == 0 && !AccessDenied;

    /// <summary>
    /// A read is only trustworthy when the CLI reported its cloak state, which it does on every
    /// healthy run that was asked for it. Without that line the output is a failed read, and a
    /// failed read must change nothing.
    /// </summary>
    public bool Answered => HidHideDeviceParser.ParseCloakState(Output) is not null;
}

/// <summary>
/// Transport for HidHideCLI.exe. Everything here exists because of a trap that was measured,
/// not because of a style preference.
///
/// ⚠️ <b>None of these had ever fired on this host.</b> The old parser matched nothing, so
/// <c>ListGamingDevices()</c> always returned empty and the CLI was never invoked at all. Fixing
/// the parser is what makes the tool actually run, which makes every one of these live for the
/// first time. @jmlopezdona lost days to them in issue #19:
///
/// <list type="number">
/// <item><b>It never exits if it inherits stdout/stderr.</b> He measured timeouts at 12 s and
///   45 s with nothing applied. Redirected through <c>cmd.exe</c> into a file: exit 0 in under a
///   second and the configuration lands. This one bites first and presents as a hang, not a bug,
///   which is why this class shells through cmd rather than reading pipes.</item>
/// <item><b>Back-to-back invocations return empty</b>, and empty is indistinguishable from "nothing
///   is configured". Three chained reads once reported an empty blacklist, an empty cloak state
///   AND an empty application list while all three were populated — one step from "restoring"
///   over entries a user wrote by hand. Hence <see cref="MinimumGap"/>, a fresh redirection file
///   per invocation, a retry, and <see cref="HidHideCliResult.Answered"/>.</item>
/// <item><b>The value goes directly after the switch</b> — no <c>--id</c>, no <c>--path</c>.
///   Fixed separately in f23117e; guarded by HidHideArgumentTests.</item>
/// <item><b>The driver's control device takes one caller at a time.</b> A second invocation during
///   a pass returns <c>Error code 0x0005 ... Access denied</c> and silently does nothing — as
///   SYSTEM too, so it is not about elevation. Opening HidHide's own GUI mid-pass can make one of
///   our writes vanish. Hence the process-wide gate and a retry on that specific failure.</item>
/// <item><b>Reads save the configuration on exit</b> unless given <c>--cancel</c>, so a bare
///   listing rewrites the config it was asked to report on.</item>
/// </list>
///
/// The CLI's own help notes that "the above commands can be sequenced reducing the overall
/// overhead involved", and that is not a micro-optimisation here: each invocation costs ~800 ms,
/// and a pad is only confined once its rule lands, so a slow pass is a wrong pass. See
/// <see cref="Sequence"/>.
/// </summary>
public sealed class HidHideCli
{
    /// <summary>
    /// Gap enforced between invocations. Below this the CLI starts returning empty output that
    /// reads exactly like an empty configuration.
    /// </summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(800);

    // Process-wide, because the constraint is the driver's control device, not this object:
    // two HidHideCli instances would collide just as happily as two invocations.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastRun = DateTimeOffset.MinValue;

    private readonly ILogger _logger;
    private readonly string _cliPath;
    private readonly TimeSpan _timeout;

    public HidHideCli(ILogger logger, string cliPath, TimeSpan? timeout = null)
    {
        _logger = logger;
        _cliPath = cliPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsAvailable => File.Exists(_cliPath);

    /// <summary>
    /// Join several commands into one invocation. One pass in one call is the difference between
    /// a rule landing in ~1.7 s and in ~6.9 s, and HidHide filters at open time — so a rule that
    /// lands late is a rule that did not apply to the pad it was written for.
    /// </summary>
    public static string Sequence(params string[] commands) =>
        string.Join(" ", commands.Where(c => !string.IsNullOrWhiteSpace(c)));

    /// <summary>
    /// Run a write. Retries once when the driver's control device was busy.
    /// </summary>
    public HidHideCliResult Write(string arguments) => RunWithRetry(arguments, isRead: false);

    /// <summary>
    /// Run a read. <c>--cloak-state</c> is prepended so the result can tell a genuine empty
    /// configuration from a failed read, and <c>--cancel</c> appended so the listing does not
    /// save over the configuration it was asked to report on.
    /// </summary>
    public HidHideCliResult Read(string arguments) =>
        RunWithRetry(Sequence("--cloak-state", arguments, "--cancel"), isRead: true);

    private HidHideCliResult RunWithRetry(string arguments, bool isRead)
    {
        var result = Run(arguments);

        // Retry once on the two failures that are known to be transient and silent: the busy
        // control device, and a read that came back without answering.
        var worthRetrying = result.AccessDenied || result.TimedOut || (isRead && !result.Answered);
        if (!worthRetrying) return result;

        _logger.LogWarning(
            "HidHide CLI did not answer ({Reason}) for: {Args} — retrying once after {Gap} ms",
            result.AccessDenied ? "control device busy" : result.TimedOut ? "timed out" : "empty read",
            arguments, MinimumGap.TotalMilliseconds);

        return Run(arguments);
    }

    private HidHideCliResult Run(string arguments)
    {
        if (!IsAvailable)
            return new HidHideCliResult(-1, "", TimedOut: false, AccessDenied: false);

        Gate.Wait();
        try
        {
            var since = DateTimeOffset.UtcNow - _lastRun;
            if (since < MinimumGap)
                Thread.Sleep(MinimumGap - since);

            // A fresh file per invocation. Reusing one lets a run that produced nothing hand back
            // the previous run's output, which is a stale answer wearing a fresh timestamp.
            var transcript = Path.Combine(Path.GetTempPath(), $"hidhide-{Guid.NewGuid():N}.txt");

            try
            {
                return RunThroughCmd(arguments, transcript);
            }
            finally
            {
                _lastRun = DateTimeOffset.UtcNow;
                try { File.Delete(transcript); } catch { /* best effort */ }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private HidHideCliResult RunThroughCmd(string arguments, string transcript)
    {
        // ⚠️ Do NOT hand the CLI our stdout/stderr, and do not read it over a pipe either: it is
        // reported not to exit at all when it inherits them. cmd.exe redirects it into a file,
        // which is the form measured to exit in under a second.
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{_cliPath}\" {arguments} > \"{transcript}\" 2>&1\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            return new HidHideCliResult(-1, "", TimedOut: false, AccessDenied: false);

        // Nothing is ever typed at it; leaving stdin open is one more way for it to wait forever.
        try { process.StandardInput.Close(); } catch { /* best effort */ }

        if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
        {
            _logger.LogWarning("HidHide CLI timed out after {Seconds}s for: {Args}",
                _timeout.TotalSeconds, arguments);
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new HidHideCliResult(-1, ReadTranscript(transcript), TimedOut: true, AccessDenied: false);
        }

        var output = ReadTranscript(transcript);

        // "Error code 0x0005 ... Access denied" with exit 0: the control device was busy and the
        // invocation did nothing. Parsing that as an answer is how a monitor concludes the
        // configuration is empty.
        var denied = output.Contains("0x0005", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("Access denied", StringComparison.OrdinalIgnoreCase);

        if (denied)
        {
            _logger.LogWarning(
                "HidHide's control device refused a concurrent caller (0x0005) for: {Args}. " +
                "This is not an elevation problem — something else was mid-pass, possibly HidHide's own GUI.",
                arguments);
        }
        else if (process.ExitCode != 0)
        {
            _logger.LogWarning("HidHide CLI exited {Code} for: {Args}\n{Output}",
                process.ExitCode, arguments, output.Trim());
        }

        return new HidHideCliResult(process.ExitCode, output, TimedOut: false, AccessDenied: denied);
    }

    private static string ReadTranscript(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch (IOException) { Thread.Sleep(50); }
        }
        return "";
    }
}
