using MultiSeat.Service.Monitoring;

namespace MultiSeat.Service.Api;

/// <summary>
/// The host's own Apollo — reported alongside the seats so the console is visible in the
/// dashboard too, rather than being the one instance nobody can see.
/// </summary>
public static class HostEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/host").WithTags("Host");

        group.MapGet("/", async (HostApolloMonitor monitor, CancellationToken ct) =>
            Results.Ok(await monitor.CollectAsync(ct)));
    }
}
