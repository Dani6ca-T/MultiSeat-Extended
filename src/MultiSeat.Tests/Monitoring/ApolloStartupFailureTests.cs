using MultiSeat.Service.Monitoring;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Telling "failed to start" apart from "crashed mid-stream" decides whether the health check
/// prints the hint about Apollo's log level.
///
/// It matters because an Apollo that dies seconds after launch usually failed to open a video
/// encoder, and Apollo discards the FFmpeg error explaining why unless its level is exactly
/// `verbose` — so the seat log ends at "Creating encoder [...]" with no reason given (issue #24).
/// </summary>
public class ApolloStartupFailureTests
{
    [Fact]
    public void NoInstanceRecord_IsNotAStartupFailure()
    {
        // The one that would misfire: null means "never launched", not "died instantly". Treating
        // it as a failure would print the hint for every seat the manager has no record of.
        Assert.False(SessionHealthCheck.IsStartupFailure(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(29)]
    public void DyingWithinTheStartupWindow_IsAStartupFailure(int seconds)
    {
        Assert.True(SessionHealthCheck.IsStartupFailure(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(120)]
    [InlineData(3600)]
    public void DyingLater_IsARealCrash(int seconds)
    {
        // A seat that streamed for a while and then died is a different problem, and the encoder
        // hint would be misleading there.
        Assert.False(SessionHealthCheck.IsStartupFailure(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void TheBoundaryItselfCountsAsAStartupFailure()
    {
        Assert.True(SessionHealthCheck.IsStartupFailure(SessionHealthCheck.ApolloStartupWindow));
    }
}
