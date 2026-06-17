using MultiSeat.Service.Emulators;
using Xunit;

namespace MultiSeat.Tests.Emulators;

public class RetroArchConfigSeederTests
{
    [Fact]
    public void UpsertCfgKey_InsertsIntoEmptyConfig()
    {
        var result = RetroArchConfigSeeder.UpsertCfgKey("", "netplay_ip_port", "48113");
        Assert.Equal("netplay_ip_port = \"48113\"", result);
    }

    [Fact]
    public void UpsertCfgKey_ReplacesExistingKey_PreservingOtherLines()
    {
        var cfg = "video_fullscreen = \"true\"\nnetplay_ip_port = \"55435\"\naudio_enable = \"true\"\n";

        var result = RetroArchConfigSeeder.UpsertCfgKey(cfg, "netplay_ip_port", "48113");

        Assert.Contains("netplay_ip_port = \"48113\"", result);
        Assert.DoesNotContain("55435", result);
        // Untouched lines preserved
        Assert.Contains("video_fullscreen = \"true\"", result);
        Assert.Contains("audio_enable = \"true\"", result);
    }

    [Fact]
    public void UpsertCfgKey_AppendsWhenMissing()
    {
        var cfg = "video_fullscreen = \"true\"\n";

        var result = RetroArchConfigSeeder.UpsertCfgKey(cfg, "netplay_ip_port", "48113");

        Assert.Equal("video_fullscreen = \"true\"\nnetplay_ip_port = \"48113\"", result);
    }

    [Fact]
    public void UpsertCfgKey_DoesNotMatchKeyPrefix()
    {
        // "netplay_ip_port" must not be treated as matching "netplay_ip_port_range".
        var cfg = "netplay_ip_port_range = \"100\"\n";

        var result = RetroArchConfigSeeder.UpsertCfgKey(cfg, "netplay_ip_port", "48113");

        Assert.Contains("netplay_ip_port_range = \"100\"", result);
        Assert.Contains("netplay_ip_port = \"48113\"", result);
    }

    [Fact]
    public void UpsertCfgKey_PreservesBackslashPaths()
    {
        var result = RetroArchConfigSeeder.UpsertCfgKey(
            "", "rgui_browser_directory", @"C:\MultiSeatGames\ROMs");
        Assert.Equal("rgui_browser_directory = \"C:\\MultiSeatGames\\ROMs\"", result);
    }
}
