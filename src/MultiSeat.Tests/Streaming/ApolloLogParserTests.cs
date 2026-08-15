using MultiSeat.Service.Streaming;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// The client's requested mode is read from Apollo's log because the environment variables
/// Apollo exports are wrong on the resume path. These lock the parsing that decision rests on.
/// </summary>
public class ApolloLogParserTests
{
    private const string ConnectLine =
        "[2026-08-15 09:12:03.114]: Info: Display mode for client [living-room] requested to [1920x1080x60]";

    [Fact]
    public void ParsesWidthHeightAndRefresh()
    {
        var mode = ApolloLogParser.ParseLastRequestedMode(ConnectLine);

        Assert.NotNull(mode);
        Assert.Equal(1920, mode!.Width);
        Assert.Equal(1080, mode.Height);
        Assert.Equal(60, mode.RefreshHz);
    }

    // A seat's log accumulates every connect for the life of the seat. Taking the first match
    // would pin the seat to whichever device connected first — exactly the "second device
    // inherits the first device's size" bug this feature exists to avoid.
    [Fact]
    public void TakesTheLastRequestNotTheFirst()
    {
        var log = string.Join('\n',
            "Info: Display mode for client [handheld] requested to [1280x720x60]",
            "Info: CLIENT CONNECTED",
            "Info: CLIENT DISCONNECTED",
            "Info: Display mode for client [macbook] requested to [3024x1890x120]",
            "Info: CLIENT CONNECTED");

        var mode = ApolloLogParser.ParseLastRequestedMode(log);

        Assert.Equal(3024, mode!.Width);
        Assert.Equal(1890, mode.Height);
        Assert.Equal(120, mode.RefreshHz);
    }

    [Fact]
    public void HandlesAModeWithNoRefreshRate()
    {
        var mode = ApolloLogParser.ParseLastRequestedMode("requested to [2560x1440]");

        Assert.Equal(2560, mode!.Width);
        Assert.Equal(1440, mode.Height);
        Assert.Null(mode.RefreshHz);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Info: CLIENT CONNECTED")]
    [InlineData("Info: Desktop resolution [1920x1080]")]   // a different line that also has a size
    [InlineData("Warning: Virtual Display creation failed")]
    public void ReturnsNullWhenNoModeWasRequested(string log)
    {
        Assert.Null(ApolloLogParser.ParseLastRequestedMode(log));
    }

    [Fact]
    public void ToleratesWhitespaceVariations()
    {
        var mode = ApolloLogParser.ParseLastRequestedMode("requested to [ 1600 x 900 x 144 ]");

        Assert.Equal(1600, mode!.Width);
        Assert.Equal(900, mode.Height);
        Assert.Equal(144, mode.RefreshHz);
    }

    // Real logs are large; make sure scanning one is not accidentally quadratic or fragile.
    [Fact]
    public void FindsTheLastRequestInALargeLog()
    {
        var noise = string.Join('\n', Enumerable.Repeat("Info: some unrelated apollo chatter", 20_000));
        var log = noise + "\nInfo: Display mode for client [tv] requested to [3840x2160x60]\n" + noise;

        var mode = ApolloLogParser.ParseLastRequestedMode(log);

        Assert.Equal(3840, mode!.Width);
        Assert.Equal(2160, mode.Height);
    }
}
