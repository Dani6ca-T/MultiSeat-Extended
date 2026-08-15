using MultiSeat.Service.Configuration;
using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Default.rdp is the only thing that sets a seat's desktop size — the seat streams its RDP
/// session surface, and that surface's geometry is fixed by mstsc at connect and cannot be
/// changed from inside the session. These assert the file we hand mstsc, which is otherwise
/// only observable by provisioning a seat on a live host.
/// </summary>
public class RdpFileBuilderTests
{
    private static string BuildWith(int width, int height) =>
        RdpFileBuilder.Build(AudioMode.SharedHost, RdpGeometry.ForClient(width, height));

    [Fact]
    public void WritesRequestedGeometry()
    {
        var rdp = BuildWith(1280, 720);

        Assert.Contains("desktopwidth:i:1280", rdp);
        Assert.Contains("desktopheight:i:720", rdp);
    }

    // smart sizing and dynamic resolution let the session's resolution follow the mstsc WINDOW,
    // which is hidden and minimized here. Left on, a window nobody ever sees would dictate the
    // resolution a player streams at.
    [Fact]
    public void PinsTheSessionSoAHiddenWindowCannotResizeIt()
    {
        var rdp = BuildWith(1920, 1080);

        Assert.Contains("smart sizing:i:0", rdp);
        Assert.Contains("dynamic resolution:i:0", rdp);
    }

    [Fact]
    public void OmitsGeometryEntirelyWhenNoneRequested()
    {
        var rdp = RdpFileBuilder.Build(AudioMode.SharedHost, geometry: null);

        Assert.DoesNotContain("desktopwidth", rdp);
        Assert.DoesNotContain("desktopheight", rdp);
    }

    // A nonsense size is silently ignored by mstsc, which would leave the seat inheriting the
    // console's size while the config claimed otherwise. Drop the keys instead of writing junk.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(320, 240)]
    [InlineData(99999, 99999)]
    public void IgnoresGeometryThatMstscWouldReject(int width, int height)
    {
        var rdp = BuildWith(width, height);

        Assert.DoesNotContain("desktopwidth", rdp);
    }

    // Scale exists so a high-resolution client does not render microscopic UI: the seat desktop
    // is viewed on the client's screen at the client's size.
    [Theory]
    [InlineData(1280, 100)]
    [InlineData(1920, 100)]
    [InlineData(2560, 125)]
    [InlineData(3024, 150)]
    [InlineData(3840, 200)]
    public void DerivesScaleFromWidth(int width, int expectedScale)
    {
        Assert.Equal(expectedScale, RdpGeometry.DeriveScaleFactor(width));
        Assert.Contains($"desktopscalefactor:i:{expectedScale}", BuildWith(width, 1080));
    }

    // ── Audio mode must keep working alongside the new keys ───────────

    [Theory]
    [InlineData(AudioMode.SharedHost, "audiomode:i:1")]
    [InlineData(AudioMode.PerSession, "audiomode:i:0")]
    public void KeepsAudioModeIndependentOfGeometry(AudioMode mode, string expected)
    {
        Assert.Contains(expected, RdpFileBuilder.Build(mode, RdpGeometry.ForClient(1920, 1080)));
        Assert.Contains(expected, RdpFileBuilder.Build(mode, geometry: null));
    }

    // audiocapturemode triggers a Windows mic-access dialog the dismisser cannot catch, which
    // hangs the RDP connection outright.
    [Fact]
    public void NeverRequestsMicrophoneRedirection()
    {
        Assert.DoesNotContain("audiocapturemode", BuildWith(1920, 1080));
    }
}
