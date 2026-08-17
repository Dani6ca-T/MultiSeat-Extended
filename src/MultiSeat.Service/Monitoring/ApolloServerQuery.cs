using System.Text.RegularExpressions;

namespace MultiSeat.Service.Monitoring;

/// <summary>What Apollo says about itself when asked the way Moonlight asks.</summary>
/// <param name="HostName">Server name Apollo advertises to clients.</param>
/// <param name="AppVersion">Apollo's reported version.</param>
/// <param name="Streaming">True when a client is actively streaming.</param>
/// <remarks>
/// Deliberately carries no "paired" flag. serverinfo's PairStatus is answered relative to the
/// uniqueid in the request, so any probe with its own id is told "not paired" no matter how many
/// clients are actually paired. Pairing has to come from Apollo's state file instead.
/// </remarks>
public sealed record ApolloServerInfo(string? HostName, string? AppVersion, bool Streaming);

/// <summary>
/// Asks an Apollo instance the same question a Moonlight client asks — its <c>serverinfo</c>
/// endpoint — and reports whether it answered.
///
/// This exists because "the process is alive" and "a client could actually use it" are different
/// claims, and only the second one is what a user means by "is my seat up?". A process can be
/// running and wedged, or still starting, or listening on a port nobody expects. Shared by the
/// host card and the per-seat service list so both answer that question the same way.
/// </summary>
public sealed class ApolloServerQuery
{
    private readonly HttpClient _http;
    private readonly ILogger<ApolloServerQuery> _logger;

    public ApolloServerQuery(ILogger<ApolloServerQuery> logger)
    {
        _logger = logger;

        // Deliberately short: this runs behind dashboard polls, and an unreachable Apollo is a
        // legitimate answer rather than an error worth waiting on.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    /// <summary>
    /// Query the instance serving <paramref name="port"/>, or null when it did not answer.
    /// Never throws for an unreachable server — that is the result, not a failure.
    /// </summary>
    public async Task<ApolloServerInfo?> QueryAsync(int port, CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync(
                $"http://127.0.0.1:{port}/serverinfo?uniqueid=multiseat-dashboard", ct);

            // state reads SUNSHINE_SERVER_FREE when idle; currentgame is 0 when nothing runs.
            var state = Tag(xml, "state");
            var currentGame = Tag(xml, "currentgame");
            var streaming =
                (state is not null && !state.EndsWith("FREE", StringComparison.OrdinalIgnoreCase))
                || (currentGame is not null && currentGame != "0");

            return new ApolloServerInfo(
                Tag(xml, "hostname"),
                Tag(xml, "appversion"),
                streaming);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Apollo serverinfo query failed on port {Port}", port);
            return null;
        }
    }

    private static string? Tag(string xml, string tag)
    {
        var m = Regex.Match(xml, $"<{Regex.Escape(tag)}>(.*?)</{Regex.Escape(tag)}>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
