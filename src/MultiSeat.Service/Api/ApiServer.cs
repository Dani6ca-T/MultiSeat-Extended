using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        // Set ContentRootPath explicitly — WebApplication.CreateBuilder() defaults to
        // Directory.GetCurrentDirectory() which is C:\Windows\system32 for a Windows Service,
        // causing UseStaticFiles / MapFallbackToFile to fail to find the wwwroot folder.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });

        // Re-use the host's singleton registrations
        builder.Services.AddSingleton(hostServices.GetRequiredService<Sessions.SeatManager>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Sessions.RdpWrapper>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Sessions.SessionLauncher>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Accounts.AccountManager>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Monitoring.GpuMonitor>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Monitoring.MetricsCollector>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Monitoring.SessionHealthCheck>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Display.VirtualDisplayManager>());
        builder.Services.AddSingleton(hostServices.GetRequiredService<Configuration.SeatPresetStore>());

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(options.ApiPort);
        });

        builder.Services.ConfigureHttpJsonOptions(opts =>
        {
            opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        // Register CORS services (required before UseCors)
        builder.Services.AddCors();

        var app = builder.Build();

        // Resolve effective API key — generate and persist if not configured.
        // Key is saved to C:\ProgramData\MultiSeat\api-key.txt so the operator can
        // copy it into the dashboard Settings page. It is never embedded in appsettings.json.
        var apiKey = ResolveApiKey(options.ApiKey);

        // ── Middleware ───────────────────────────────────────────────
        app.UseWebSockets();

        // Serve dashboard static files from wwwroot/ if present
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        // API key auth — enforced for /api/ routes unless explicitly disabled.
        // Static files and WebSocket upgrade are exempt.
        // Set ApiKey = "disabled" in appsettings.json to turn off auth on trusted networks.
        if (!string.IsNullOrEmpty(apiKey))
        {
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/api") ||
                    context.Request.Path.StartsWithSegments("/ws"))
                {
                    await next();
                    return;
                }

                if (!context.Request.Headers.TryGetValue(Constants.ApiKeyHeader, out var key)
                    || key != apiKey)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                    return;
                }

                await next();
            });
        }

        if (options.ApiKey.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogWarning(
                "API authentication is DISABLED — the dashboard is open to anyone on the network.");
        }
        else if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            app.Logger.LogWarning(
                "No ApiKey set in appsettings.json — a key was auto-generated. " +
                "Copy it from C:\\ProgramData\\MultiSeat\\api-key.txt into the dashboard Settings page.");
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

    /// <summary>
    /// Returns the configured API key, or generates+persists a random one if none is set.
    /// Reads an existing persisted key so the same key survives service restarts.
    /// </summary>
    private static string ResolveApiKey(string configured)
    {
        // "disabled" is an explicit opt-out — return empty so the auth middleware is skipped.
        if (configured.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var keyFile = Path.Combine(@"C:\ProgramData\MultiSeat", "api-key.txt");

        if (File.Exists(keyFile))
        {
            var persisted = File.ReadAllText(keyFile).Trim();
            if (!string.IsNullOrWhiteSpace(persisted))
                return persisted;
        }

        // Generate a URL-safe 32-char random key
        var raw = RandomNumberGenerator.GetBytes(24);
        var generated = Convert.ToBase64String(raw)
            .Replace('+', 'A').Replace('/', 'B').Replace('=', 'C');

        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        File.WriteAllText(keyFile, generated);
        return generated;
    }
}
