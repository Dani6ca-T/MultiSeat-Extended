using MultiSeat.Service.Monitoring;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Tests for the startup-failure classification in SessionHealthCheck.
///
/// A seat whose Apollo died within <see cref="SessionHealthCheck.ApolloStartupWindow"/>
/// of start is treated as a startup failure (the process likely never finished
/// initializing), while a death after that window is a normal runtime crash. Null
/// uptime — no instance record — is never a startup failure.
/// </summary>
public class ApolloStartupFailureTests
{
    // ── IsStartupFailure ─────────────────────────────────────────────

    [Fact]
    public void NullUptime_IsNotAStartupFailure()
    {
        Assert.False(SessionHealthCheck.IsStartupFailure(null));
    }

    [Fact]
    public void ZeroUptime_IsAStartupFailure()
    {
        Assert.True(SessionHealthCheck.IsStartupFailure(TimeSpan.Zero));
    }

    [Fact]
    public void UptimeWellBelowThirtySeconds_IsAStartupFailure()
    {
        Assert.True(SessionHealthCheck.IsStartupFailure(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void UptimeExactlyThirtySeconds_IsAStartupFailure()
    {
        Assert.True(SessionHealthCheck.IsStartupFailure(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void UptimeJustAboveThirtySeconds_IsNotAStartupFailure()
    {
        var uptime = SessionHealthCheck.ApolloStartupWindow + TimeSpan.FromMilliseconds(1);
        Assert.False(SessionHealthCheck.IsStartupFailure(uptime));
    }

    [Fact]
    public void UptimeWellAboveThirtySeconds_IsNotAStartupFailure()
    {
        Assert.False(SessionHealthCheck.IsStartupFailure(TimeSpan.FromMinutes(5)));
    }
}