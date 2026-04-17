using MultiSeat.Service.Configuration;
using MultiSeat.Shared;

namespace MultiSeat.Service.Api;

/// <summary>
/// Builds and configures the embedded ASP.NET Core Minimal API server
/// that runs inside the Windows Service process.
/// </summary>
public static class ApiServer
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddEndpointsApiExplorer();
    }

    public static WebApplication Build(IServiceProvider hostServices, MultiSeatOptions options)
    {
        var builder = WebApplication.CreateBuilder();

        // Re-use the host's singleton registrations
        builder.Services.AddSingleton(hostServices.GetRequiredService<Sessions.SeatManager>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Sessions.RdpWrapper>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Accounts.AccountManager>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Monitoring.GpuMonitor>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Monitoring.SessionHealthCheck>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Display.VirtualDisplayManager>());

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(options.ApiPort);
        });

        // Register CORS services (required before UseCors)
        builder.Services.AddCors();

        var app = builder.Build();

        // ── Middleware ───────────────────────────────────────────────
        app.UseWebSockets();

        // Serve dashboard static files from wwwroot/ if present
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        // API key auth middleware (skip if key is empty — dev mode)
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            app.Use(async (context, next) =>
            {
                // Skip auth for static files and WebSocket
                if (context.Request.Path.StartsWithSegments("/ws") ||
                    (!context.Request.Path.StartsWithSegments("/api")))
                {
                    await next();
                    return;
                }

                if (!context.Request.Headers.TryGetValue(Constants.ApiKeyHeader, out var key)
                    || key != options.ApiKey)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
                    return;
                }

                await next();
            });
        }

        // CORS — restrict to configured origins in production, permissive if none set
        if (options.CorsOrigins.Length > 0)
        {
            app.UseCors(policy => policy
                .WithOrigins(options.CorsOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());
        }
        else
        {
            app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
        }

        // ── Map endpoint groups ──────────────────────────────────────
        SeatEndpoints.Map(app);
        AccountEndpoints.Map(app);
        SystemEndpoints.Map(app);
        InputEndpoints.Map(app);
        WebSocketHub.Map(app);

        // Fallback to index.html for SPA routing (dashboard)
        if (Directory.Exists(wwwroot))
        {
            app.MapFallbackToFile("index.html");
        }

        return app;
    }
}
