using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Diagnostics;
using Xunit;

namespace MultiSeat.Tests.Diagnostics;

/// <summary>
/// Guards the one logging behaviour that is invisible until it bites: whether the service's own
/// Information logs reach the Windows Event Log.
///
/// They did not, for the whole life of the project up to 2026-08-14. `AddWindowsService()` adds a
/// provider-specific rule pinning the EventLog provider to Warning+, and provider-specific rules
/// outrank category rules that name no provider — so the `"MultiSeat.Service": "Debug"` entry under
/// `Logging:LogLevel` did nothing for the only destination a service has, and every LogInformation
/// was silently discarded.
/// </summary>
public class LoggingFilterTests
{
    /// <summary>
    /// Stand-in for the rule <c>AddWindowsService()</c> installs. Mimicked rather than invoked so
    /// the test does not need a real service host; the shape is what matters — a rule that names
    /// the EventLog provider and caps it at Warning.
    /// </summary>
    private static void AddWindowsServiceStyleEventLogCap(ILoggingBuilder logging) =>
        logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);

    private static LoggerFilterOptions BuildOptions(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            AddWindowsServiceStyleEventLogCap(logging);
            logging.AddConfiguration(config.GetSection("Logging"));
        });

        return services.BuildServiceProvider()
                       .GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
    }

    private static IConfiguration ConfigFromJson(string json) =>
        new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

    /// <summary>Ask the framework, the way LogFilterInspector does, rather than reason about rules.</summary>
    private static bool EventLogAccepts(LoggerFilterOptions options, string category, LogLevel level)
    {
        using var provider = new EventLogLoggerProvider(new EventLogSettings { SourceName = "MultiSeatTests" });
        using var factory = new LoggerFactory([provider], options);
        return factory.CreateLogger(category).IsEnabled(level);
    }

    // ── The trap ──────────────────────────────────────────────────────

    [Fact]
    public void CategoryRuleAlone_DoesNotReachEventLog()
    {
        // Exactly what appsettings.json held before the fix.
        var options = BuildOptions(ConfigFromJson("""
            { "Logging": { "LogLevel": { "Default": "Information", "MultiSeat.Service": "Debug" } } }
            """));

        Assert.False(EventLogAccepts(options, "MultiSeat.Service.Sessions.SeatManager", LogLevel.Information));
    }

    [Fact]
    public void ProviderScopedRule_ReachesEventLog()
    {
        var options = BuildOptions(ConfigFromJson("""
            {
              "Logging": {
                "LogLevel": { "Default": "Information", "MultiSeat.Service": "Debug" },
                "EventLog": { "LogLevel": { "Default": "Warning", "MultiSeat.Service": "Information" } }
              }
            }
            """));

        Assert.True(EventLogAccepts(options, "MultiSeat.Service.Sessions.SeatManager", LogLevel.Information));
    }

    // ── The shipped config ────────────────────────────────────────────

    [Fact]
    public void ShippedAppSettings_LetsServiceInformationReachTheEventLog()
    {
        var options = BuildOptions(
            new ConfigurationBuilder().AddJsonFile("service-appsettings.json", optional: false).Build());

        Assert.True(EventLogAccepts(options, "MultiSeat.Service", LogLevel.Information));
        Assert.True(EventLogAccepts(options, "MultiSeat.Service.Sessions.SeatManager", LogLevel.Information));
    }

    [Fact]
    public void ShippedAppSettings_KeepsFrameworkChatterOutOfTheEventLog()
    {
        // Raising EventLog:Default to Information would also outrank "Microsoft": "Warning" and
        // pour every ASP.NET Core request log into a machine-wide log. Guard against that edit.
        var options = BuildOptions(
            new ConfigurationBuilder().AddJsonFile("service-appsettings.json", optional: false).Build());

        Assert.False(EventLogAccepts(
            options, "Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information));
    }

    [Fact]
    public void ShippedAppSettings_KeepsDebugOutOfTheEventLog()
    {
        // Health-check chatter would flood the Application log. The corollary, documented in
        // CLAUDE.md, is that LogDebug is effectively write-only on a deployed host.
        var options = BuildOptions(
            new ConfigurationBuilder().AddJsonFile("service-appsettings.json", optional: false).Build());

        Assert.False(EventLogAccepts(options, "MultiSeat.Service.Sessions.SeatManager", LogLevel.Debug));
    }

    // ── The inspector itself ──────────────────────────────────────────

    [Fact]
    public void BuildReport_ShowsRulesAndPerProviderVerdicts()
    {
        var options = BuildOptions(ConfigFromJson("""
            {
              "Logging": {
                "LogLevel": { "Default": "Information" },
                "EventLog": { "LogLevel": { "MultiSeat.Service": "Information" } }
              }
            }
            """));

        using var provider = new EventLogLoggerProvider(new EventLogSettings { SourceName = "MultiSeatTests" });
        var report = string.Join("\n", LogFilterInspector.BuildReport(
            options, [provider], ["MultiSeat.Service"]));

        Assert.Contains("Global MinLevel", report);
        Assert.Contains("EventLogLoggerProvider", report);
        Assert.Contains("MultiSeat.Service", report);
        Assert.Contains("Information=YES", report);
    }
}
