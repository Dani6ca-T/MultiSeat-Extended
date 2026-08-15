using System.Text.RegularExpressions;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Reads what a Moonlight client asked for out of Apollo's log.
///
/// Apollo announces the client's requested mode on connect:
///   <c>Info: Display mode for client [living-room] requested to [3024x1890x120]</c>
///
/// The log is the source for this, deliberately, rather than Apollo's
/// <c>SUNSHINE_CLIENT_WIDTH</c>/<c>HEIGHT</c> environment variables: on the RESUME path those
/// still hold the values of the client that originally launched the app, so a second device
/// connecting to a paused session inherits the first device's size. Reported by jmlopezdona in
/// issue #15, who measured it.
/// </summary>
public static partial class ApolloLogParser
{
    [GeneratedRegex(
        @"requested\s+to\s*\[\s*(?<w>\d{3,5})\s*x\s*(?<h>\d{3,5})\s*(?:x\s*(?<r>\d{1,3}))?\s*\]",
        RegexOptions.IgnoreCase)]
    private static partial Regex RequestedModeRegex();

    /// <summary>
    /// The most recent mode a client requested, or null if the log contains none.
    ///
    /// Always the LAST match, never the first: a seat's log accumulates every connect for the
    /// life of the seat, and the first one is whichever device connected first.
    /// </summary>
    public static RequestedMode? ParseLastRequestedMode(string logText)
    {
        if (string.IsNullOrEmpty(logText)) return null;

        Match? last = null;
        foreach (Match m in RequestedModeRegex().Matches(logText))
            last = m;

        if (last is null) return null;

        var width = int.Parse(last.Groups["w"].Value);
        var height = int.Parse(last.Groups["h"].Value);
        int? refresh = last.Groups["r"].Success ? int.Parse(last.Groups["r"].Value) : null;

        return new RequestedMode(width, height, refresh);
    }
}

/// <param name="Width">Requested width in pixels.</param>
/// <param name="Height">Requested height in pixels.</param>
/// <param name="RefreshHz">Requested refresh rate, when Apollo logged one.</param>
public sealed record RequestedMode(int Width, int Height, int? RefreshHz);
