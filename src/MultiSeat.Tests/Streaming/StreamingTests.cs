using Microsoft.Extensions.Logging;
using MultiSeat.Service.Display;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

public class StreamingTests
{
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
            var builder = new ApolloConfigBuilder(logger);

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
                Width = 1920,
                Height = 1080,
                Fps = 60,
                AudioDeviceId = "{0.0.0.00000000}.{test-device-id}",
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            Assert.True(File.Exists(configPath));

            var content = File.ReadAllText(configPath);

            // Verify required configuration keys
            Assert.Contains("sunshine_name = MultiSeat-MultiSeatSeat01", content);
            Assert.Contains($"port = {47984 + Constants.OffsetHttps}", content);
            Assert.Contains("resolutions = [1920x1080]", content);
            Assert.Contains("fps = [60]", content);
            Assert.Contains("encoder = nvenc", content);
            Assert.Contains("controller = enabled", content);
            Assert.Contains("audio_sink = {0.0.0.00000000}.{test-device-id}", content);
            Assert.Contains("min_log_level = info", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_UpdateAudioSink_ModifiesConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger);

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            // Audio should be placeholder initially
            var before = File.ReadAllText(configPath);
            Assert.Contains("# audio_sink = (set after VAC assignment)", before);

            // Update with real device ID
            builder.UpdateAudioSink(configPath, "{0.0.0.00000000}.{vac-cable-1}");

            var after = File.ReadAllText(configPath);
            Assert.Contains("audio_sink = {0.0.0.00000000}.{vac-cable-1}", after);
            Assert.DoesNotContain("# audio_sink = (set after VAC assignment)", after);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_UpdateDisplayOutput_ModifiesConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger);

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
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_CleanupConfig_RemovesDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger);

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);
            Assert.True(File.Exists(configPath));

            builder.CleanupConfig(seat.Id, tempDir);
            Assert.False(File.Exists(configPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ApolloConfigBuilder_SeatConfigDir_UsesGuidFormat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multiseat-test-{Guid.NewGuid():N}");

        try
        {
            var logger = new TestLogger<ApolloConfigBuilder>();
            var builder = new ApolloConfigBuilder(logger);

            var seat = new SeatInfo
            {
                AccountName = "MultiSeatSeat01",
                PortBase = 47984,
            };

            var configPath = builder.BuildConfig(seat, tempDir);

            // Config should be in {tempDir}/{seatId:N}/sunshine.conf
            var expectedDir = Path.Combine(tempDir, seat.Id.ToString("N"));
            Assert.StartsWith(expectedDir, configPath);
            Assert.EndsWith("sunshine.conf", configPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
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
    public void Constants_PortOffsets_AreSequential()
    {
        Assert.Equal(0, Constants.OffsetHttps);
        Assert.Equal(1, Constants.OffsetHttp);
        Assert.Equal(2, Constants.OffsetVideo);
        Assert.Equal(3, Constants.OffsetAudio);
        Assert.Equal(4, Constants.OffsetControl);
    }

    [Fact]
    public void Constants_PortsPerSeat_CoversAllOffsets()
    {
        // PortsPerSeat must be > max offset
        Assert.True(Constants.PortsPerSeat > Constants.OffsetControl);
    }

    [Fact]
    public void Constants_PortBase_Is47984()
    {
        // Sunshine/Moonlight default HTTPS port
        Assert.Equal(47984, Constants.PortBase);
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
