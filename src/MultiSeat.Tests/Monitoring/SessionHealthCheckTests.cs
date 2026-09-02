using MultiSeat.Service.Monitoring;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Tests for the session-active guard in SessionHealthCheck: the polling loop that
/// waits for a reconnected RDP session to reach Active state before Apollo is started.
/// </summary>
public class SessionHealthCheckTests
{
    // ── WaitForSessionActiveAsync ───────────────────────────────────────

    [Fact]
    public async Task SessionActiveImmediately_ReturnsTrue()
    {
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => true, sessionId: 42, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task SessionNeverActive_ReturnsFalse()
    {
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => false, sessionId: 42, CancellationToken.None,
            pollMs: 50, timeoutMs: 200);

        Assert.False(result);
    }

    [Fact]
    public async Task SessionBecomesActiveAfterSeveralPolls_ReturnsTrue()
    {
        var callCount = 0;
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => ++callCount >= 4,
            sessionId: 7, CancellationToken.None,
            pollMs: 10, timeoutMs: 5000);

        Assert.True(result);
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task CancellationStopsPolling()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await SessionHealthCheck.WaitForSessionActiveAsync(
                _ =>
                {
                    callCount++;
                    if (callCount >= 2) cts.Cancel();
                    return false;
                },
                sessionId: 1, cts.Token,
                pollMs: 10, timeoutMs: 5000);
        });
    }

    [Fact]
    public async Task ActiveCheckOnFinalPollAfterTimeout_ReturnsTrue()
    {
        var polls = 0;
        var result = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => ++polls >= 3,
            sessionId: 99, CancellationToken.None,
            pollMs: 100, timeoutMs: 250);

        Assert.True(result);
        Assert.True(polls <= 4);
    }

    // ── IsWorthChecking (existing, still valid) ─────────────────────────

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    public void ALiveSeatIsChecked(SeatStatus status)
    {
        Assert.True(SessionHealthCheck.IsWorthChecking(status));
    }

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.TearingDown)]
    [InlineData(SeatStatus.Error)]
    public void ANonActiveSeatIsNotChecked(SeatStatus status)
    {
        Assert.False(SessionHealthCheck.IsWorthChecking(status));
    }
}
