using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using System.Reflection;

namespace MultiSeat.Service.Diagnostics;

/// <summary>
/// Reports which binary is deployed and what it actually resolved each setting to, naming the
/// configuration source that won.
///
/// This exists because "I changed the setting and nothing happened" has three different causes on
/// this project and they are indistinguishable from the outside:
///
///   1. THE BINARY IS STALE. Pulling the source does not rebuild the service — install-service.ps1
///      does. A binary that predates a feature ignores its setting in silence, because the option
///      does not exist to be bound. This is what happened to the reporter of issue #18: they set
///      KeepaliveOnSeparateDesktop, pulled the commit, and got neither the success line nor the
///      fallback warning, because the service they were running had never heard of either.
///   2. A LATER SOURCE OVERRODE IT. appsettings.local.json is loaded last on purpose and outranks
///      the shipped appsettings.json in the same folder.
///   3. THE EDIT WAS REVERTED. The deployed appsettings.json is PreserveNewest, so a host edit
///      survives publishes right up until someone touches the repo's copy — then it goes back.
///
/// Guessing between those costs a round trip with whoever is reporting the problem. This asks the
/// built host instead, so the answer is the deployed one.
///
/// It runs AFTER builder.Build(), for the same reason <see cref="LogFilterInspector"/> does:
/// reconstructing an equivalent configuration here would be free to drift from the one that ships.
/// Building the host does not start it, so this is safe to run on a live machine.
///
/// WHAT A FAILURE LOOKS LIKE: if this prints an unrecognised-argument message or starts the
/// service instead, the deployed binary predates this instrument — and that is itself the answer
/// to cause 1.
///
/// Usage: MultiSeat.Service.exe --config
/// </summary>
public static class ConfigInspector
{
    /// <summary>
    /// Print the report. Exit code 0 when the deployed binary and the configuration it resolved
    /// are self-consistent, 1 when something here would explain a setting not taking effect.
    /// </summary>
    public static int Run(IServiceProvider services, IConfiguration configuration)
    {
        var opts = services.GetRequiredService<IOptions<MultiSeatOptions>>().Value;

        Console.WriteLine();
        Console.WriteLine("== deployed binary ==");

        var exe = Environment.ProcessPath ?? "(unknown)";
        Console.WriteLine($"  path          : {exe}");

        if (File.Exists(exe))
        {
            var info = new FileInfo(exe);
            Console.WriteLine($"  built / copied: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  size          : {info.Length:N0} bytes");
        }

        var asm = Assembly.GetEntryAssembly();
        var informational = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                               ?.InformationalVersion;
        Console.WriteLine($"  version       : {informational ?? "(none)"}");
        Console.WriteLine();
        Console.WriteLine("  NOTE: 'built / copied' is when this exe was last DEPLOYED, not when you");
        Console.WriteLine("        last pulled. If it predates the commit you expect, run");
        Console.WriteLine("        scripts\\install-service.ps1 — restarting the service reruns the");
        Console.WriteLine("        old binary.");

        Console.WriteLine();
        Console.WriteLine("== configuration files beside the exe ==");

        var dir = Path.GetDirectoryName(exe);
        var anyConfig = false;
        if (dir is not null)
        {
            // Listed in the order Program.cs registers them: later wins.
            foreach (var name in new[] { "appsettings.json", "appsettings.local.json" })
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    anyConfig = true;
                    var when = new FileInfo(path).LastWriteTime;
                    Console.WriteLine($"  {name,-24} present, modified {when:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    Console.WriteLine($"  {name,-24} absent");
                }
            }
        }
        Console.WriteLine("  (appsettings.local.json is loaded LAST and outranks appsettings.json)");

        Console.WriteLine();
        Console.WriteLine("== effective MultiSeat settings ==");
        Console.WriteLine("  (the value this binary resolved, then the source that supplied it)");
        Console.WriteLine();

        foreach (var (name, value) in EnumerateOptions(opts))
        {
            var source = DescribeSource(configuration, $"MultiSeat:{name}");
            Console.WriteLine($"  {name,-34} = {value,-26} {source}");
        }

        Console.WriteLine();
        Console.WriteLine("== verdict ==");

        var problems = new List<string>();

        if (!anyConfig)
            problems.Add("No appsettings.json beside the exe — this is not a normal deployment.");

        if (string.IsNullOrEmpty(informational))
            problems.Add(
                "The binary carries no version stamp, so it predates the build that records one. "
                + "Redeploy before trusting anything above.");

        if (problems.Count == 0)
        {
            Console.WriteLine("  Nothing here explains a setting failing to take effect. If one still");
            Console.WriteLine("  is not, compare the value above with what you set — the source column");
            Console.WriteLine("  names the file that won, and '(default)' means no file set it at all.");
            return 0;
        }

        foreach (var p in problems) Console.WriteLine($"  {p}");
        return 1;
    }

    /// <summary>
    /// The settings worth printing, read off the bound options object by reflection so a newly
    /// added option cannot fail to appear. A hand-written list would go stale in silence, which is
    /// the exact class of fault this instrument exists to catch.
    /// </summary>
    internal static IEnumerable<(string Name, string Value)> EnumerateOptions(MultiSeatOptions opts)
    {
        foreach (var prop in typeof(MultiSeatOptions)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            yield return (prop.Name, Describe(prop.Name, prop.GetValue(opts)));
        }
    }

    /// <summary>
    /// Render a value for the report, redacting anything whose NAME says it is a secret. This
    /// output is written to be pasted into bug reports, so an API key or a password must not ride
    /// along with it.
    /// </summary>
    internal static string Describe(string name, object? raw)
    {
        if (raw is null) return "(null)";

        if (IsSecretName(name))
            return raw is string { Length: 0 } ? "(empty)" : "(set, redacted)";

        if (raw is System.Collections.IEnumerable enumerable and not string)
        {
            var count = enumerable.Cast<object?>().Count();
            return count == 0 ? "(empty)" : $"({count} entries)";
        }

        var text = raw.ToString() ?? "(null)";
        return text.Length == 0 ? "(empty)" : text;
    }

    internal static bool IsSecretName(string name) =>
        name.Contains("Password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Token", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Which configuration provider supplied a key. "(default)" means none did and the value
    /// shown is the C# initialiser — the distinction that separates "my file was ignored" from
    /// "my file said that".
    /// </summary>
    internal static string DescribeSource(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
            return string.Empty;

        // Providers run least- to most-significant, so the LAST one holding the key is the winner.
        string? winner = null;
        foreach (var provider in root.Providers)
        {
            if (provider.TryGet(key, out _))
                winner = provider.ToString();
        }

        return winner is null ? "(default — no config file set it)" : $"<- {Shorten(winner)}";
    }

    /// <summary>
    /// JsonConfigurationProvider describes itself as "JsonConfigurationProvider for
    /// 'appsettings.json' (Optional)". The file name is the only part a reader needs.
    /// </summary>
    internal static string Shorten(string providerDescription)
    {
        var start = providerDescription.IndexOf('\'');
        var end = providerDescription.LastIndexOf('\'');
        return start >= 0 && end > start
            ? providerDescription[(start + 1)..end]
            : providerDescription;
    }
}
