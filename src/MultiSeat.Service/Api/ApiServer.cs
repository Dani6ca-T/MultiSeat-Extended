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

        // Resolve effective API key before Build() so ApiAuthState can be registered in DI.
        // Key is saved to C:\ProgramData\MultiSeat\api-key.txt so the operator can
        // copy it into the dashboard Settings page. It is never embedded in appsettings.json.
        var apiKey = ResolveApiKey(options.ApiKey);
        var authState = new ApiAuthState(!string.IsNullOrEmpty(apiKey), apiKey);
        builder.Services.AddSingleton(authState);

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

        // API key auth — enforced for /api/ routes unless explicitly disabled.
        // Checks ApiAuthState.IsEnabled on every request so toggling via the dashboard
        // takes effect immediately without a service restart.
        // Static files and WebSocket upgrades are exempt.
        app.Use(async (context, next) =>
        {
            if (!authState.IsEnabled ||
                !context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/ws") ||
                // Auth status endpoint is always public — GET lets the dashboard
                // show the toggle even when the key isn't stored yet.
                (context.Request.Path.Equals("/api/system/auth") && context.Request.Method == "GET"))
            {
                await next();
                return;
            }

            if (!context.Request.Headers.TryGetValue(Constants.ApiKeyHeader, out var key)
                || key != authState.ApiKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                return;
            }

            await next();
        });

        if (!authState.IsEnabled)
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
