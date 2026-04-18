using System.Diagnostics;
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
    }

}
