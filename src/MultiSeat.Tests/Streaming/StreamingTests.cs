using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

public class StreamingTests
{
    // BuildConfig creates junction points (assets, tools) inside the seat dir.
    // Directory.Delete(recursive:true) throws on reparse points — strip them first.
    private static void DeleteTestDir(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
        {
            var di = new DirectoryInfo(entry);
            if (di.Exists && di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                di.Delete();
        }
        Directory.Delete(dir, recursive: true);
    }

    // ── PortAllocator tests ───────────────────────────────────────────
    // (existing tests in Sessions/PortAllocatorTests.cs cover allocation;
    //  these test port offset calculations)

    [Fact]
    public void PortAllocator_PortOffsets_MatchConstants()
    {
        var alloc = new PortAllocator();
        var portBase = alloc.Allocate();

        Assert.Equal(portBase + Constants.OffsetHttps, alloc.GetHttpsPort(portBase));
        Assert.Equal(portBase + Constants.OffsetHttp, alloc.GetHttpPort(portBase));
        Assert.Equal(portBase + Constants.OffsetVideo, alloc.GetVideoPort(portBase));
        Assert.Equal(portBase + Constants.OffsetAudio, alloc.GetAudioPort(portBase));
        Assert.Equal(portBase + Constants.OffsetControl, alloc.GetControlPort(portBase));
    }

    [Fact]
    public void PortAllocator_SeatPortRanges_DoNotOverlap()
    {
        var alloc = new PortAllocator();
        var ports = new List<int>();

        for (int i = 0; i < Constants.MaxSeats; i++)
            ports.Add(alloc.Allocate());

        // Verify no two seats share any port in their 10-port block
        for (int i = 0; i < ports.Count; i++)
        {
            for (int j = i + 1; j < ports.Count; j++)
            {
                var rangeI = Enumerable.Range(ports[i], Constants.PortsPerSeat);
                var rangeJ = Enumerable.Range(ports[j], Constants.PortsPerSeat);
                Assert.Empty(rangeI.Intersect(rangeJ));
            }
        }
    }

    // ── ResolutionNegotiator tests ────────────────────────────────────

    [Fact]
    public void ResolutionNegotiator_ClampsToMaxEncoder()
    {
        var (w, h, fps) = ResolutionNegotiator.Negotiate(
            7680, 4320, 240,
            maxEncoderWidth: 3840, maxEncoderHeight: 2160, maxFps: 120);

        Assert.Equal(3840, w);
        Assert.Equal(2160, h);
        Assert.Equal(120, fps);
    }

    [Fact]
    public void ResolutionNegotiator_EnsuresEvenDimensions()
    {
        var (w, h, _) = ResolutionNegotiator.Negotiate(1921, 1081, 60);

        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }

    [Fact]
    public void ResolutionNegotiator_PassthroughForValidResolution()
    {
        var (w, h, fps) = ResolutionNegotiator.Negotiate(1920, 1080, 60);

        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
        Assert.Equal(60, fps);
    }

    [Theory]
    [InlineData(1280, 720, "720p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(3840, 2160, "4K")]
    [InlineData(1920, 1200, "Ally X Native")]
    [InlineData(1280, 800, "Deck Native")]
    public void ResolutionNegotiator_CommonResolutions_AreEvenDimensions(
        int w, int h, string label)
    {
        // All common resolutions should already have even dimensions
        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);

        // Verify they appear in the list
        Assert.Contains(
            ResolutionNegotiator.CommonResolutions,
            r => r.W == w && r.H == h && r.Label == label);
    }

    // ── ApolloConfigBuilder tests ─────────────────────────────────────

    [Fact]
    public void ApolloConfigBuilder_GeneratesConfigWithRequiredFields()
    {
        // Use a temp directory for the test
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            // SharedHost explicitly: this case covers the audio keys that only that mode emits
            // (virtual_sink, stream_mic). It used to rely on SharedHost being the default, which
            // it stopped being on 2026-08-19 — see ApolloConfigBuilder_PerSessionIsTheDefault.
            var options = new MultiSeatOptions { AudioMode = AudioMode.SharedHost };
            var builder = new ApolloConfigBuilder(logger, Options.Create(options));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
                Width = 1920,
                Height = 1080,
                Fps = 60,
                AudioGameRenderFriendlyName = "CABLE In 16ch (VB-Audio Virtual Cable)",
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            Assert.True(File.Exists(configPath));

            var content = File.ReadAllText(configPath);

            // Verify required configuration keys
            Assert.Contains("sunshine_name = MultiSeat-MultiSeatSeat01", content);
            Assert.Contains($"port = {47984 + Constants.OffsetHttps}", content);
            Assert.Contains("resolutions = [1920x1080]", content);
            Assert.Contains("fps = [30, 60]", content);
            // Display-device auto-config so Apollo matches the client's requested mode (issue #11).
            // Without dd_configuration_option != disabled, Apollo never resizes SudoVDA.
            Assert.Contains("dd_configuration_option = ensure_active", content);
            Assert.Contains("dd_resolution_option = auto", content);
            Assert.Contains("dd_refresh_rate_option = auto", content);
            Assert.Contains("encoder = nvenc", content);
            Assert.Contains("controller = enabled", content);
            Assert.Contains("stream_mic = enabled", content);
            Assert.Contains("min_log_level = info", content);
            // Audio: route the game to the seat's virtual device WITHOUT holding the machine-wide
            // default output, so seats don't hijack the console/host audio (issue #10).
            Assert.Contains("virtual_sink = CABLE In 16ch (VB-Audio Virtual Cable)", content);
            Assert.Contains("keep_sink_default = disabled", content);
            Assert.Contains("auto_capture_sink = disabled", content);
            // audio_sink (the "play on host too" device) must NOT be set — it would make Apollo
            // grab the global default at stream start.
            Assert.DoesNotContain("audio_sink =", content);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_PerSessionAudio_NamesNoSinkAndDisablesMic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var options = new MultiSeatOptions { AudioMode = AudioMode.PerSession };
            var builder = new ApolloConfigBuilder(logger, Options.Create(options));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
                // Deliberately populated: a seat may still carry a device from a previous
                // SharedHost run. PerSession must ignore it rather than name it.
                AudioGameRenderFriendlyName = "CABLE In 16ch (VB-Audio Virtual Cable)",
            };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            // The core of per-session audio: name NO sink. Apollo then captures the RDP
            // session's own Remote Audio endpoint, which is already the session default.
            // Naming it via virtual_sink makes Apollo rewrite the endpoint's wave format and
            // breaks loopback capture for everyone including Apollo; audio_sink re-roles it.
            Assert.DoesNotContain("virtual_sink = ", content);
            Assert.DoesNotContain("audio_sink = ", content);

            // Guard specifically against the seat's stale device leaking into the config.
            Assert.DoesNotContain("CABLE In 16ch", content);

            // Apollo must still be barred from taking or re-asserting endpoint ownership.
            Assert.Contains("keep_sink_default = disabled", content);
            Assert.Contains("auto_capture_sink = disabled", content);

            // No mic path exists in this mode — a session that keeps its own audio cannot see
            // the host's Steam Streaming Microphone. Written explicitly, not left to default.
            Assert.Contains("stream_mic = disabled", content);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    // ── Host-settable Apollo values (PR #21) ──────────────────────────
    //
    // MultiSeat:Encoder exists because Apollo's own fallback is not safe everywhere: on an AMD
    // host it lands on AMF, whose startup probe runs against the seat's RDP surface (1000 Hz, no
    // real display) and hangs there BEFORE Apollo opens any port. The seat then reports Ready with
    // nothing listening. MultiSeat:ApolloLogLevel exists because a seat's Apollo log is the only
    // window into a session nobody can watch.
    //
    // Both are written verbatim into sunshine.conf, so these also pin the sanitising.

    [Theory]
    [InlineData("amdvce")]
    [InlineData("software")]
    [InlineData("quicksync")]
    public void ApolloConfigBuilder_EncoderFollowsTheOption(string encoder)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
        try
        {
            var options = new MultiSeatOptions { Encoder = encoder };
            var builder = new ApolloConfigBuilder(new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
            var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            Assert.Contains($"encoder = {encoder}", content);
            Assert.DoesNotContain("encoder = nvenc", content);
        }
        finally { DeleteTestDir(tempDir); }
    }

    [Fact]
    public void ApolloConfigBuilder_EncoderDefaultsToNvenc_WhenUnsetOrBlank()
    {
        // The default has to survive an empty string as well as an unset property: an operator
        // clearing the value in appsettings must not produce "encoder = ", which Apollo reads as
        // an empty encoder rather than as "use the default".
        foreach (var value in new[] { null, "", "   " })
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
            try
            {
                var options = new MultiSeatOptions { Encoder = value! };
                var builder = new ApolloConfigBuilder(new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
                var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

                var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

                Assert.Contains("encoder = nvenc", content);
            }
            finally { DeleteTestDir(tempDir); }
        }
    }

    [Fact]
    public void ApolloConfigBuilder_LogLevelFollowsTheOption_AndDefaultsToInfo()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
        try
        {
            var options = new MultiSeatOptions { ApolloLogLevel = "debug" };
            var builder = new ApolloConfigBuilder(new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
            var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            Assert.Contains("min_log_level = debug", content);
            Assert.DoesNotContain("min_log_level = info", content);
        }
        finally { DeleteTestDir(tempDir); }

        var tempDir2 = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
        try
        {
            var builder = new ApolloConfigBuilder(
                new TestLogger<ApolloConfigBuilder>(), Options.Create(new MultiSeatOptions()));
            var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

            Assert.Contains("min_log_level = info", File.ReadAllText(builder.BuildConfig(seat, tempDir2)));
        }
        finally { DeleteTestDir(tempDir2); }
    }

    [Theory]
    [InlineData("nvenc\nsunshine_name = evil")]   // newline injects a second key
    [InlineData("nvenc\r\nport = 47999")]         // CRLF does the same
    [InlineData("nvenc = x")]                     // a bare '=' splits the line
    public void ApolloConfigBuilder_RejectsValuesThatWouldInjectAnotherKey(string hostile)
    {
        // sunshine.conf is "key = value" lines, so a value carrying a newline writes a SECOND
        // Apollo key nobody chose. Only an administrator can set this today, so it is not a
        // privilege boundary - it is here so a value pasted with a stray newline falls back
        // loudly instead of quietly reconfiguring Apollo.
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
        try
        {
            var options = new MultiSeatOptions { Encoder = hostile };
            var builder = new ApolloConfigBuilder(new TestLogger<ApolloConfigBuilder>(), Options.Create(options));
            var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            // Assert the WHOLE line, not a prefix: "encoder = nvenc = x" contains
            // "encoder = nvenc" and would pass a Contains check while still carrying the
            // injected text. That weaker assertion let the bare-'=' case through.
            Assert.Single(
                content.Split('\n').Select(l => l.TrimEnd('\r')),
                l => l == "encoder = nvenc");
            Assert.DoesNotContain("evil", content);
            Assert.DoesNotContain("port = 47999", content);
            // The seat's real port line must still be the only one.
            Assert.Single(
                content.Split('\n'),
                l => l.TrimStart().StartsWith("port = ", StringComparison.Ordinal));
        }
        finally { DeleteTestDir(tempDir); }
    }

    [Fact]
    public void ApolloConfigBuilder_TlsMaterialLivesUnderTheSeatDir_NotProgramFiles()
    {
        // Apollo generates cakey.pem itself at startup. Left at its default it writes into
        // {exe_dir}/config/credentials under Program Files, which a standard-user seat cannot
        // write - Apollo dies on "HTTP interface failed to initialize" and never opens a port.
        // The seat's own config dir works because ProgramData lets a user CREATE files and makes
        // the creator their owner; it is only files the SERVICE created that a seat cannot write.
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");
        try
        {
            var builder = new ApolloConfigBuilder(
                new TestLogger<ApolloConfigBuilder>(), Options.Create(new MultiSeatOptions()));
            var seat = new SeatInfo { AccountName = "MultiSeatSeat01", PortBase = 47984 };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            var seatCredDir = Path.Combine(tempDir, "MultiSeatSeat01", "config", "credentials")
                .Replace('\\', '/');
            Assert.Contains($"pkey = {seatCredDir}/cakey.pem", content);
            Assert.Contains($"cert = {seatCredDir}/cacert.pem", content);
            Assert.DoesNotContain("Program Files", content.Split('\n')
                .First(l => l.StartsWith("pkey = ", StringComparison.Ordinal)));
        }
        finally { DeleteTestDir(tempDir); }
    }

    // The default flipped to PerSession on 2026-08-19: SharedHost wedges the host's audio endpoint
    // stack on every seat provision (nodes collapse 27 -> 1, measured), which is a worse default
    // than losing the mic path. This asserts the flip in both places it has to hold — the C# default
    // and the emitted Apollo config — because a mismatch between them would be invisible.
    [Fact]
    public void ApolloConfigBuilder_PerSessionIsTheDefault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            // Default options — no AudioMode set anywhere.
            var builder = new ApolloConfigBuilder(logger, Options.Create(new MultiSeatOptions()));

            Assert.Equal(AudioMode.PerSession, new MultiSeatOptions().AudioMode);

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
                // Set deliberately: even when a cable is present on the seat, the default mode must
                // not name it. Naming a sink is what broke loopback for every client in issue #15.
                AudioGameRenderFriendlyName = "CABLE In 16ch (VB-Audio Virtual Cable)",
            };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            // Assert on assignment LINES, not on the substring: the config deliberately explains
            // itself in a comment that says "Both audio_sink and virtual_sink are deliberately
            // UNSET", so a naive DoesNotContain fails against the very comment proving the point.
            var settings = content
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith('#'))
                .ToList();

            Assert.DoesNotContain(settings, l => l.StartsWith("virtual_sink ="));
            Assert.DoesNotContain(settings, l => l.StartsWith("audio_sink ="));
            // And the comment IS expected — it is how a reader knows the omission is deliberate.
            Assert.Contains("deliberately UNSET", content);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    // The old default still has to work for anyone who sets it explicitly to keep the microphone.
    [Fact]
    public void ApolloConfigBuilder_SharedHostStillNamesTheCableWhenAskedFor()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var options = new MultiSeatOptions { AudioMode = AudioMode.SharedHost };
            var builder = new ApolloConfigBuilder(logger, Options.Create(options));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
                AudioGameRenderFriendlyName = "CABLE In 16ch (VB-Audio Virtual Cable)",
            };

            var content = File.ReadAllText(builder.BuildConfig(seat, tempDir));

            Assert.Contains("virtual_sink = CABLE In 16ch (VB-Audio Virtual Cable)", content);
            Assert.Contains("stream_mic = enabled", content);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    // A seat must never inherit "virtual-display" from the console's app list: Apollo would
    // then try to create a virtual display on every client connect, and each attempt leaves a
    // phantom monitor behind in the CONSOLE user's topology (issue #15).
    [Fact]
    public void ApolloConfigBuilder_StripsVirtualDisplayFromSeatAppsJson()
    {
        const string appsJson = """
            {
              "env": { "PATH": "$(PATH)" },
              "apps": [
                { "name": "Desktop", "image-path": "desktop.png", "virtual-display": true },
                { "name": "Steam Big Picture", "cmd": "steam.exe -bigpicture" }
              ]
            }
            """;

        var result = ApolloConfigBuilder.StripConsoleOnlyAppKeys(appsJson, out var removed);

        Assert.Equal(1, removed);
        Assert.DoesNotContain("virtual-display", result);

        // Everything else survives the round-trip.
        Assert.Contains("Desktop", result);
        Assert.Contains("desktop.png", result);
        Assert.Contains("Steam Big Picture", result);
        Assert.Contains("steam.exe -bigpicture", result);
        Assert.Contains("$(PATH)", result);
    }

    // A seat with an unstripped app list still beats a seat with no apps at all, so malformed
    // input is passed through rather than throwing or emptying the file.
    [Fact]
    public void ApolloConfigBuilder_StripConsoleOnlyAppKeys_PassesThroughUnparseableJson()
    {
        const string garbage = "{ this is not json";

        var result = ApolloConfigBuilder.StripConsoleOnlyAppKeys(garbage, out var removed);

        Assert.Equal(garbage, result);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void ApolloConfigBuilder_UpdateDisplayOutput_ModifiesConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger, Options.Create(new MultiSeatOptions()));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            builder.UpdateDisplayOutput(configPath, @"\\.\DISPLAY#SudoVDA#1");

            var content = File.ReadAllText(configPath);
            Assert.Contains(@"output_name = \\.\DISPLAY#SudoVDA#1", content);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_CleanupConfig_PreservesStateFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger, Options.Create(new MultiSeatOptions()));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);
            Assert.True(File.Exists(configPath));

            var statePath = Path.Combine(tempDir, seat.AccountName, "config", "sunshine_state.json");
            Assert.True(File.Exists(statePath));

            // CleanupConfig preserves config/ (sunshine_state.json + certs) so Moonlight
            // pairing survives teardown and re-provision. Only junctions are removed.
            builder.CleanupConfig(seat.AccountName, tempDir);
            Assert.True(File.Exists(configPath),   "sunshine.conf should be preserved (re-provision overwrites it)");
            Assert.True(File.Exists(statePath),     "sunshine_state.json must survive teardown to keep Moonlight pairing");
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_SeatConfigDir_UsesAccountName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger, Options.Create(new MultiSeatOptions()));

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            // Config should be in {tempDir}/{accountName}/sunshine.conf so the same
            // account always gets the same dir and pairing survives re-provision.
            var expectedDir = Path.Combine(tempDir, seat.AccountName);
            Assert.StartsWith(expectedDir, configPath);
            Assert.EndsWith("sunshine.conf", configPath);
        }
        finally
        {
            DeleteTestDir(tempDir);
        }
    }

    // ── ApolloManager constants tests ─────────────────────────────────

    [Fact]
    public void ApolloManager_MaxRestartAttempts_Is3()
    {
        Assert.Equal(3, ApolloManager.MaxRestartAttempts);
    }

    // ── SeatInfo model tests ──────────────────────────────────────────

    [Fact]
    public void SeatInfo_DisplayDevicePath_DefaultsToNull()
    {
        var seat = new SeatInfo { AccountName = "MultiSeatSeat01" };
        Assert.Null(seat.DisplayDevicePath);
    }

    // ── VirtualDisplay record tests ───────────────────────────────────

    [Fact]
    public void VirtualDisplay_Record_StoresAllFields()
    {
        var seatId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var display = new VirtualDisplay(
            SeatId: seatId,
            DevicePath: @"\\.\DISPLAY#SudoVDA#1",
            Width: 1920,
            Height: 1080,
            Fps: 60,
            CreatedAt: now);

        Assert.Equal(seatId, display.SeatId);
        Assert.Equal(@"\\.\DISPLAY#SudoVDA#1", display.DevicePath);
        Assert.Equal(1920, display.Width);
        Assert.Equal(1080, display.Height);
        Assert.Equal(60, display.Fps);
        Assert.Equal(now, display.CreatedAt);
    }

    [Fact]
    public void VirtualDisplay_NullDevicePath_IndicatesDegradedMode()
    {
        var display = new VirtualDisplay(
            Guid.NewGuid(), null, 1920, 1080, 60, DateTimeOffset.UtcNow);

        Assert.Null(display.DevicePath);
    }

    // ── Constants tests ───────────────────────────────────────────────

    [Fact]
    public void Constants_PortOffsets_MatchApolloMapPort()
    {
        // Values must match Apollo's map_port(N) constants exactly
        Assert.Equal(-5, Constants.OffsetGfeHttps);
        Assert.Equal(0,  Constants.OffsetGfeHttp);
        Assert.Equal(1,  Constants.OffsetWebUi);
        Assert.Equal(9,  Constants.OffsetVideo);
        Assert.Equal(10, Constants.OffsetControl);
        Assert.Equal(11, Constants.OffsetAudio);
        Assert.Equal(12, Constants.OffsetMic);
        Assert.Equal(26, Constants.OffsetRtsp);

        // Legacy aliases
        Assert.Equal(Constants.OffsetGfeHttp, Constants.OffsetHttps);
        Assert.Equal(Constants.OffsetWebUi,   Constants.OffsetHttp);
    }

    [Fact]
    public void Constants_PortsPerSeat_NoUsedPortCollision()
    {
        // A seat's full offset span (-5..+26) exceeds PortsPerSeat (30), but only these offsets
        // are actually used. The real invariant the system relies on is that no two seats' USED
        // ports collide at PortsPerSeat spacing — not that a block covers the whole raw span.
        int[] usedOffsets =
        {
            Constants.OffsetGfeHttps, Constants.OffsetGfeHttp, Constants.OffsetWebUi,
            Constants.OffsetVideo, Constants.OffsetControl, Constants.OffsetAudio,
            Constants.OffsetMic, Constants.OffsetRtsp, Constants.OffsetRetroArchNetplay,
        };

        var allUsedPorts = new HashSet<int>();
        for (int seat = 0; seat < Constants.MaxSeats; seat++)
        {
            var seatBase = Constants.PortBase + seat * Constants.PortsPerSeat;
            foreach (var off in usedOffsets)
            {
                var port = seatBase + off;
                Assert.True(allUsedPorts.Add(port),
                    $"Port {port} is used by more than one seat — PortsPerSeat={Constants.PortsPerSeat} " +
                    $"causes a cross-seat collision at offset {off}.");
            }
        }
    }

    [Fact]
    public void Constants_PortBase_Is48100()
    {
        // Sits above a stock Apollo's port block (centered on the Sunshine/Moonlight
        // default 47984) so MultiSeat seats coexist with a standalone Apollo.
        Assert.Equal(48100, Constants.PortBase);
    }

    [Fact]
    public void Constants_MaxSeats_Is8()
    {
        Assert.Equal(8, Constants.MaxSeats);
    }

    // ── Integration tests (skipped — require real infrastructure) ─────

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires Apollo installed")]
    public void ApolloManager_StartsAndStopsApollo()
    {
        // Tests real Apollo process launch in a session
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SudoVDA driver")]
    public void VirtualDisplayManager_CreatesAndDestroysDisplay()
    {
        // Tests real SudoVDA virtual display creation
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires admin/SYSTEM privileges")]
    public void FirewallManager_OpensAndClosesPorts()
    {
        // Tests real netsh firewall rule management
    }

    // ── ApolloManager.ResolveLogPath ─────────────────────────────────────
    // The streaming binary does not necessarily honour the log_path we request:
    // Vibepollo writes <seatDir>\logs\apollo-<stamp>.log instead. Reading the
    // requested name silently disabled SudoVDA display detection and
    // launch-on-connect, so resolution is by inspection.

    private static string NewSeatDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms-logpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveLogPath_FindsTimestampedLogInLogsSubdir()
    {
        var seatDir = NewSeatDir();
        try
        {
            var logsDir = Path.Combine(seatDir, "logs");
            Directory.CreateDirectory(logsDir);
            var actual = Path.Combine(logsDir, "apollo-20260805-163944-038.log");
            File.WriteAllText(actual, "Info: started");

            Assert.Equal(actual, ApolloManager.ResolveLogPath(seatDir));
        }
        finally { DeleteTestDir(seatDir); }
    }

    [Fact]
    public void ResolveLogPath_SkipsTheZeroByteDecoyInSeatRoot()
    {
        // Vibepollo leaves an empty file in the seat root and writes the real log under logs\.
        var seatDir = NewSeatDir();
        try
        {
            File.WriteAllText(Path.Combine(seatDir, "apollo-20260805-163944-038.log"), "");
            var logsDir = Path.Combine(seatDir, "logs");
            Directory.CreateDirectory(logsDir);
            var real = Path.Combine(logsDir, "apollo-20260805-163944-038.log");
            File.WriteAllText(real, "Info: started");

            Assert.Equal(real, ApolloManager.ResolveLogPath(seatDir));
        }
        finally { DeleteTestDir(seatDir); }
    }

    [Fact]
    public void ResolveLogPath_PicksTheLiveLogWhileItsWriterStillHoldsItOpen()
    {
        // Production shape: the streaming binary holds the current log open for the whole
        // run, and Windows does not refresh the cached directory entry on every write — so
        // FileInfo.Length can read 0 on a file that already holds thousands of bytes. That
        // made the resolver skip the live log and return the previous run's stale one, which
        // is what display detection and launch-on-connect then read.
        //
        // We cannot force the cache to go stale on demand, so this pins the requirement
        // rather than the timing: with a writer still holding the newest log open, that log
        // must win over an older closed one regardless of what the directory entry says.
        var seatDir = NewSeatDir();
        try
        {
            var stale = Path.Combine(seatDir, "apollo-20260806-210336-237.log");
            File.WriteAllText(stale, "Info: previous run");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            var logsDir = Path.Combine(seatDir, "logs");
            Directory.CreateDirectory(logsDir);
            var live = Path.Combine(logsDir, "apollo-20260807-082449-555.log");

            using (var writer = new FileStream(
                live, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write(Encoding.UTF8.GetBytes("Info: current run"));
                writer.Flush();

                Assert.Equal(live, ApolloManager.ResolveLogPath(seatDir));
            }
        }
        finally { DeleteTestDir(seatDir); }
    }

    // ── ApolloManager.ParseSudoVdaDisplayIdFromLogText ───────────────────
    // Inside an RDP-loopback seat the Microsoft RDP indirect display reports 1000Hz with
    // edid=null and friendly_name="" — identical to SudoVDA on those fields. The old
    // "first 1000Hz display" fallback therefore returned the RDP surface as output_name,
    // so seats streamed the host desktop at its own size while reporting success.

    private static string DisplayLogJson(params string[] entries) =>
        "Info: Currently available display devices:\n[\n" +
        string.Join(",\n", entries) + "\n]\n";

    private static string DisplayEntry(
        string deviceId, string friendlyName, bool primary, int refreshNumerator,
        int width, int height) =>
        $$"""
            {
              "device_id": "{{deviceId}}",
              "display_name": "\\\\.\\DISPLAY1",
              "edid": null,
              "friendly_name": "{{friendlyName}}",
              "info": {
                "hdr_state": "Disabled",
                "origin_point": { "x": 0, "y": 0 },
                "primary": {{(primary ? "true" : "false")}},
                "refresh_rate": {
                  "type": "rational",
                  "value": { "denominator": 1, "numerator": {{refreshNumerator}} }
                },
                "resolution": { "height": {{height}}, "width": {{width}} },
                "resolution_scale": {
                  "type": "rational",
                  "value": { "denominator": 100, "numerator": 100 }
                }
              }
            }
        """;

    [Fact]
    public void ParseSudoVda_DoesNotMistakeTheLoneRdpSurfaceForSudoVda()
    {
        // Real shape from GitHub issue #14: a seat with NO virtual display at all. The only
        // display is the RDP surface — primary, 1000Hz, empty friendly_name, 3440x1440.
        // The old fallback returned this device_id and the seat streamed 3440x1440.
        var log = DisplayLogJson(DisplayEntry(
            "{f96a9834-1d18-5ee4-83e0-0964152a1577}", "", primary: true,
            refreshNumerator: 1000, width: 3440, height: 1440));

        var result = ApolloManager.ParseSudoVdaDisplayIdFromLogText(log);

        Assert.Null(result.DeviceId);
        Assert.Equal(1, result.DisplayCount);
        Assert.True(result.RejectedPrimaryOnly);
    }

    [Fact]
    public void ParseSudoVda_PicksTheNonPrimary1000HzDisplayAlongsideTheRdpSurface()
    {
        // The case the fallback exists for: SudoVDA attached alongside the RDP desktop,
        // friendly_name empty because SetupDi descriptions aren't available in-session.
        var log = DisplayLogJson(
            DisplayEntry("{rdp-surface}", "", primary: true,
                refreshNumerator: 1000, width: 3440, height: 1440),
            DisplayEntry("{sudovda}", "", primary: false,
                refreshNumerator: 1000, width: 1920, height: 1080));

        var result = ApolloManager.ParseSudoVdaDisplayIdFromLogText(log);

        Assert.Equal("{sudovda}", result.DeviceId);
        Assert.Equal(2, result.DisplayCount);
    }

    [Fact]
    public void ParseSudoVda_PrefersAnExplicitFriendlyNameOverTheFallback()
    {
        var log = DisplayLogJson(
            DisplayEntry("{rdp-surface}", "", primary: true,
                refreshNumerator: 1000, width: 3440, height: 1440),
            DisplayEntry("{sudovda}", "VDD by MTT", primary: false,
                refreshNumerator: 60, width: 1920, height: 1080));

        var result = ApolloManager.ParseSudoVdaDisplayIdFromLogText(log);

        Assert.Equal("{sudovda}", result.DeviceId);
        Assert.Equal("VDD by MTT", result.FriendlyName);
    }

    [Fact]
    public void ParseSudoVda_IgnoresTheResolutionScaleNumerator()
    {
        // resolution_scale also carries a "numerator"; only refresh_rate's counts.
        // A 100-scale display at 60Hz must not be read as a 1000Hz match.
        var log = DisplayLogJson(
            DisplayEntry("{a}", "", primary: true,
                refreshNumerator: 60, width: 1920, height: 1080),
            DisplayEntry("{b}", "", primary: false,
                refreshNumerator: 60, width: 1920, height: 1080));

        var result = ApolloManager.ParseSudoVdaDisplayIdFromLogText(log);

        Assert.Null(result.DeviceId);
        Assert.False(result.RejectedPrimaryOnly);
    }

    [Fact]
    public void ParseSudoVda_ReturnsNothingWhenTheLogHasNoDisplayBlock()
    {
        var result = ApolloManager.ParseSudoVdaDisplayIdFromLogText("Info: started\n");

        Assert.Null(result.DeviceId);
        Assert.Equal(0, result.DisplayCount);
    }

    [Fact]
    public void ResolveLogPath_HonoursPlainApolloLogWhenTheBinaryRespectsLogPath()
    {
        var seatDir = NewSeatDir();
        try
        {
            var plain = Path.Combine(seatDir, "apollo.log");
            File.WriteAllText(plain, "Info: started");

            Assert.Equal(plain, ApolloManager.ResolveLogPath(seatDir));
        }
        finally { DeleteTestDir(seatDir); }
    }

    [Fact]
    public void ResolveLogPath_PrefersNewestAcrossBothLayouts()
    {
        var seatDir = NewSeatDir();
        try
        {
            var plain = Path.Combine(seatDir, "apollo.log");
            File.WriteAllText(plain, "old");
            File.SetLastWriteTimeUtc(plain, DateTime.UtcNow.AddHours(-2));

            var logsDir = Path.Combine(seatDir, "logs");
            Directory.CreateDirectory(logsDir);
            var newer = Path.Combine(logsDir, "apollo-20260805-163944-038.log");
            File.WriteAllText(newer, "new");
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

            Assert.Equal(newer, ApolloManager.ResolveLogPath(seatDir));
        }
        finally { DeleteTestDir(seatDir); }
    }

    [Fact]
    public void ResolveLogPath_FallsBackToRequestedPathWhenNothingExists()
    {
        var seatDir = NewSeatDir();
        try
        {
            Assert.Equal(
                Path.Combine(seatDir, "apollo.log"),
                ApolloManager.ResolveLogPath(seatDir));
        }
        finally { DeleteTestDir(seatDir); }
    }
}

/// <summary>
/// Minimal ILogger implementation for unit tests.
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    { }
}
