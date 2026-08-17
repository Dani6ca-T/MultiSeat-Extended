using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;

namespace MultiSeat.Service.Api;

public static class SystemEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        group.MapGet("/health", (SeatManager seats, MetricsCollector metrics) =>
            Results.Ok(metrics.Collect(seats)));

        // Triggers a full rebuild and service restart.
        // Spawns a detached PowerShell process that runs install-service.ps1 after a 3s delay,
        // giving this HTTP response time to reach the client before the service stops.
        // Requires SourceDir to be set in appsettings.json.
        group.MapPost("/rebuild", (IOptions<MultiSeatOptions> opts, ILoggerFactory logFactory) =>
        {
            var log = logFactory.CreateLogger("MultiSeat.Rebuild");
            var sourceDir = opts.Value.SourceDir;
            if (string.IsNullOrWhiteSpace(sourceDir))
                return Results.BadRequest(new { error = "SourceDir not configured in appsettings.json" });

            var script = Path.Combine(sourceDir, "scripts", "install-service.ps1");
            if (!File.Exists(script))
                return Results.BadRequest(new { error = $"Script not found: {script}" });

            // Detached PowerShell: wait 3s then run the install script (which stops + restarts the service).
            var ps = $"Start-Sleep 3; & '{script}'";
            Process.Start(new ProcessStartInfo(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            log.LogInformation("Rebuild triggered — service will restart in ~3s");
            return Results.Accepted(value: new { message = "Rebuild started — service will restart shortly" });
        });

        // Diagnostic endpoint — dumps all connected display paths from QueryDisplayConfig.
        // Use this to verify SudoVDA virtual displays are visible and check their names.
        // GET /api/system/displays
        group.MapGet("/displays", (VirtualDisplayManager displays) =>
        {
            var allPaths = displays.EnumerateAllConnectedPaths();
            return Results.Ok(new
            {
                totalConnected = allPaths.Count,
                sudoVdaFound = displays.IsDriverAvailable,
                paths = allPaths
            });
        });

        // GET /api/system/auth — returns current authentication state.
        //
        // Deliberately public: the dashboard has to be able to show the auth toggle before it
        // holds a key. What actually makes it public is the exemption in ApiServer's own auth
        // middleware, which matches this path AND the GET method — NOT the AllowAnonymous() below.
        // There is no UseAuthorization() in this pipeline, so that call is inert; it is kept only
        // to state intent, and to be correct if authorization middleware is ever added.
        group.MapGet("/auth", (ApiAuthState authState) =>
            Results.Ok(new { authEnabled = authState.IsEnabled }))
            .AllowAnonymous();

        // POST /api/system/auth — toggles API key authentication on/off.
        // Takes effect immediately (no restart needed); also persists to appsettings.json.
        //
        // This one REQUIRES the API key whenever auth is enabled, and must keep doing so: it is
        // the endpoint that can turn authentication off, so an unauthenticated caller reaching it
        // would be able to disable the protection for everything else. ApiServer's middleware
        // exempts only GET on this path, so POST is gated — which is why the AllowAnonymous() that
        // used to sit here has been removed rather than commented.
        //
        // It carried the note "must be reachable even when auth is currently disabled", which was
        // confused twice over: when auth is disabled the middleware already lets every request
        // through, so no exemption is needed, and the call could not have granted one anyway
        // (nothing reads endpoint authorization metadata in this pipeline). Left in place it would
        // have become a real hole the moment anyone added UseAuthorization().
        group.MapPost("/auth", async (ApiAuthState authState, AuthToggleRequest body, ILoggerFactory logFactory) =>
        {
            var log = logFactory.CreateLogger("MultiSeat.Auth");
            authState.SetEnabled(body.Enabled);

            // Persist to appsettings.json so the setting survives restarts.
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(settingsPath);
                    var node = JsonNode.Parse(json);
                    if (node?["MultiSeat"] is JsonObject ms)
                    {
                        ms["ApiKey"] = body.Enabled ? "" : "disabled";
                        await File.WriteAllTextAsync(settingsPath,
                            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Could not persist auth setting to appsettings.json");
                }
            }

            log.LogInformation("API authentication {State} via dashboard",
                body.Enabled ? "enabled" : "disabled");

            return Results.Ok(new { authEnabled = authState.IsEnabled });
        });
    }

    private record AuthToggleRequest(bool Enabled);
}
