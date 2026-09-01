using Microsoft.Extensions.Configuration;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Diagnostics;
using Xunit;

namespace MultiSeat.Tests.Diagnostics;

/// <summary>
/// `--config` exists to answer "I changed the setting and nothing happened" without a round trip,
/// and it is written to be pasted into a bug report. Both halves are tested here: that it tells the
/// truth about where a value came from, and that pasting it does not hand out a secret.
/// </summary>
public class ConfigInspectorTests
{
    private static IConfigurationRoot Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // ── Redaction: this output goes into bug reports ──────────────────

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("SeatPassword")]
    [InlineData("ClientSecret")]
    [InlineData("AuthToken")]
    [InlineData("apikey")]              // casing must not open a hole
    public void SecretsAreRedacted(string name)
    {
        var rendered = ConfigInspector.Describe(name, "hunter2-the-real-value");

        Assert.DoesNotContain("hunter2", rendered);
        Assert.Equal("(set, redacted)", rendered);
    }

    [Fact]
    public void AnEmptySecretSaysSoRatherThanClaimingOneIsSet()
    {
        // "(set, redacted)" on an unset key would send someone hunting for a key that is not
        // there — the opposite of what this instrument is for.
        Assert.Equal("(empty)", ConfigInspector.Describe("ApiKey", ""));
    }

    [Fact]
    public void OrdinarySettingsAreShownInFull()
    {
        // The whole point is to show the operator their real value; over-redacting would make the
        // report useless.
        Assert.Equal("PerSession", ConfigInspector.Describe("AudioMode", "PerSession"));
        Assert.Equal("True", ConfigInspector.Describe("KeepaliveOnSeparateDesktop", true));
    }

    [Fact]
    public void CollectionsAreSummarisedNotDumped()
    {
        Assert.Equal("(empty)", ConfigInspector.Describe("LaunchOnConnect", Array.Empty<string>()));
        Assert.Equal("(2 entries)", ConfigInspector.Describe("SeatPadDevicePaths", new[] { "a", "b" }));
    }

    // ── Provenance: the column that answers the actual question ───────

    [Fact]
    public void AKeyNoFileSetIsReportedAsADefault()
    {
        // This is the exact line that answers issue #18's follow-up: the option resolved true from
        // the C# initialiser because no file mentioned it. Reporting a provider here would send
        // someone editing a file that says nothing about it.
        var source = ConfigInspector.DescribeSource(Config(), "MultiSeat:KeepaliveOnSeparateDesktop");

        Assert.Contains("default", source);
    }

    [Fact]
    public void AKeyAFileSetIsAttributedToThatFile()
    {
        var source = ConfigInspector.DescribeSource(
            Config(("MultiSeat:AudioMode", "SharedHost")), "MultiSeat:AudioMode");

        Assert.DoesNotContain("default", source);
    }

    [Fact]
    public void TheLastProviderToSetAKeyIsTheOneReported()
    {
        // appsettings.local.json is registered last precisely so it outranks appsettings.json.
        // Reporting the first provider instead would name the file that LOST, which is worse than
        // saying nothing.
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("MultiSeat:AudioMode", "loser")])
            .Add(new NamedInMemorySource("appsettings.local.json", "MultiSeat:AudioMode", "winner"))
            .Build();

        Assert.Contains("appsettings.local.json",
            ConfigInspector.DescribeSource(root, "MultiSeat:AudioMode"));
    }

    [Fact]
    public void TheProviderNameIsReducedToTheFileName()
    {
        Assert.Equal("appsettings.local.json", ConfigInspector.Shorten(
            "JsonConfigurationProvider for 'appsettings.local.json' (Optional)"));

        // A provider that names no file must survive unchanged rather than come back empty.
        Assert.Equal("MemoryConfigurationProvider",
            ConfigInspector.Shorten("MemoryConfigurationProvider"));
    }

    // ── The report cannot silently go stale ───────────────────────────

    [Fact]
    public void EveryOptionIsReported()
    {
        // Read by reflection on purpose: a hand-maintained list would omit each newly added option
        // in silence, and an instrument that quietly stops covering things is worse than none.
        var reported = ConfigInspector.EnumerateOptions(new MultiSeatOptions())
            .Select(o => o.Name).ToHashSet(StringComparer.Ordinal);

        var declared = typeof(MultiSeatOptions)
            .GetProperties(System.Reflection.BindingFlags.Public
                           | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.All(declared, name =>
            Assert.True(reported.Contains(name), $"{name} is missing from the --config report"));
    }

    [Fact]
    public void TheOptionBehindIssue18IsAmongThem()
    {
        // Named explicitly: it is the one a reporter will be asked to read back, and a rename that
        // silently drops it from the report would recreate the problem this was built to end.
        var reported = ConfigInspector.EnumerateOptions(new MultiSeatOptions()).ToDictionary(
            o => o.Name, o => o.Value, StringComparer.Ordinal);

        Assert.Equal("True", reported["KeepaliveOnSeparateDesktop"]);
    }

    /// <summary>An in-memory provider that reports a file name, so provider attribution is testable.</summary>
    private sealed class NamedInMemorySource(string name, string key, string value)
        : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            new NamedProvider(name, key, value);

        private sealed class NamedProvider(string name, string key, string value)
            : ConfigurationProvider, IConfigurationProvider
        {
            public override void Load() => Data[key] = value;
            public override string ToString() => $"JsonConfigurationProvider for '{name}' (Optional)";
        }
    }
}
