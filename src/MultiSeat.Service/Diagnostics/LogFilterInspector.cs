using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MultiSeat.Service.Diagnostics;

/// <summary>
/// Reports which log levels actually reach each logging provider — most importantly the
/// Windows Event Log, which is the only destination a Windows Service has.
///
/// This exists because the answer is not readable from appsettings.json. `AddWindowsService()`
/// installs a <b>provider-specific</b> filter rule pinning the EventLog provider to Warning and
/// above, and provider-specific rules outrank every category rule under `Logging:LogLevel`
/// (those match any provider). So a `"MultiSeat.Service": "Debug"` line there can look correct
/// while every LogInformation the service writes is silently discarded — which is exactly what
/// was happening until the `Logging:EventLog` section was added. 300 events sampled on the
/// reference host at the time were 275 Warning + 25 Error and zero Information.
///
/// Rather than reason about rule precedence, this asks the framework: it builds a
/// single-provider <see cref="LoggerFactory"/> over the host's real <see cref="LoggerFilterOptions"/>
/// and calls <c>IsEnabled</c>. What it prints is what that provider will accept.
///
/// Usage: MultiSeat.Service.exe --log-filters
/// </summary>
public static class LogFilterInspector
{
    /// <summary>
    /// Categories worth reporting: the service's own root (every ILogger&lt;T&gt; in the service
    /// lives under it), a couple of representative subsystems, and the framework categories
    /// that would flood a machine-wide log if a rule were too permissive.
    /// </summary>
    private static readonly string[] DefaultCategories =
    [
        "MultiSeat.Service",
        "MultiSeat.Service.Sessions.SeatManager",
        "MultiSeat.Service.Streaming.ApolloManager",
        "Microsoft.Hosting.Lifetime",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
    ];

    private static readonly LogLevel[] ReportedLevels =
        [LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error];

    /// <summary>
    /// Resolve the host's real logging configuration and print the report. Returns a process
    /// exit code: 0 when the service's own Information logs reach the Event Log, 1 when they
    /// do not — so this doubles as a check a script can act on.
    /// </summary>
    public static int Run(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        var providers = services.GetServices<ILoggerProvider>().ToList();

        foreach (var line in BuildReport(options, providers, DefaultCategories))
            Console.WriteLine(line);

        var eventLog = providers.FirstOrDefault(IsEventLogProvider);
        if (eventLog is null)
        {
            Console.WriteLine();
            Console.WriteLine("No Event Log provider is registered — service logs have nowhere to go.");
            return 1;
        }

        var ok = IsEnabled(options, eventLog, "MultiSeat.Service", LogLevel.Information);

        Console.WriteLine();
        Console.WriteLine(ok
            ? "OK: the service's Information logs reach the Event Log."
            : "PROBLEM: the service's Information logs do NOT reach the Event Log. Add a\n" +
              "         Logging:EventLog:LogLevel section to appsettings.json — see CLAUDE.md,\n" +
              "         \"The Logging:EventLog section is load-bearing\".");

        return ok ? 0 : 1;
    }

    /// <summary>
    /// Build the report as lines. Pure apart from the framework's own filter evaluation, so it
    /// can be exercised in tests against synthetic <see cref="LoggerFilterOptions"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildReport(
        LoggerFilterOptions options,
        IReadOnlyCollection<ILoggerProvider> providers,
        IReadOnlyCollection<string> categories)
    {
        var lines = new List<string>
        {
            $"Global MinLevel = {options.MinLevel}",
            "",
            "Filter rules, in the order they were added — at equal specificity the LAST one wins,",
            "and a rule naming a provider outranks one that does not:",
        };

        foreach (var rule in options.Rules)
        {
            var provider = rule.ProviderName ?? "(any provider)";
            var category = rule.CategoryName ?? "(any category)";
            var level = rule.LogLevel?.ToString() ?? "(none)";
            var viaDelegate = rule.Filter is not null ? "  +filter delegate" : "";
            lines.Add($"  provider={provider,-58} category={category,-42} level={level}{viaDelegate}");
        }

        foreach (var provider in providers)
        {
            lines.Add("");
            lines.Add($"Effective levels for {DescribeProvider(provider)}:");

            foreach (var category in categories)
            {
                var states = ReportedLevels.Select(level =>
                    $"{level}={(IsEnabled(options, provider, category, level) ? "YES" : "no ")}");
                lines.Add($"  {category,-46} {string.Join("  ", states)}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Ask the framework whether one provider would accept a given category/level, by building a
    /// LoggerFactory containing only that provider. Isolating it matters: ILogger.IsEnabled on a
    /// normal factory answers for the aggregate, which is how a message that reaches the console
    /// but not the Event Log looks enabled.
    /// </summary>
    private static bool IsEnabled(
        LoggerFilterOptions options, ILoggerProvider provider, string category, LogLevel level)
    {
        using var factory = new LoggerFactory([provider], options);
        return factory.CreateLogger(category).IsEnabled(level);
    }

    private static bool IsEventLogProvider(ILoggerProvider provider) =>
        provider.GetType().Name == "EventLogLoggerProvider";

    private static string DescribeProvider(ILoggerProvider provider)
    {
        var name = provider.GetType().Name;
        return IsEventLogProvider(provider)
            ? $"{name}  <-- the Windows Event Log; the only destination a service has"
            : name;
    }
}
