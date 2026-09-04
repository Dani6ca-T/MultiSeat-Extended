using MultiSeat.Service.Monitoring;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// Tests for the gate that stops Apollo being started against a session that is not ACTIVE yet.
///
/// Apollo calls QueryDisplayConfig at startup and a Disconnected session answers
/// ERROR_ACCESS_DENIED, so an early start produces an Apollo with no display, which dies, which
/// the health check restarts into the same state. The old code relied on a fixed 2s delay — a
/// guess about how long the session takes, and losing that race is what produced the loop.
///
/// Ported from @Dani6ca-T's MultiSeat-Extended fork (commit e34f60f).
/// </summary>
public class WaitForSessionActiveTests
{
    [Fact]
    public async Task ReturnsImmediately_WhenAlreadyActive()
    {
        var calls = 0;
        var ok = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => { calls++; return true; }, sessionId: 7, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(1, calls);   // no sleeping when the answer is already yes
    }

    [Fact]
    public async Task ReturnsTrue_WhenTheSessionBecomesActiveWhileWaiting()
    {
        var calls = 0;
        var ok = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => ++calls >= 3, sessionId: 7, CancellationToken.None,
            pollMs: 10, timeoutMs: 1000);

        Assert.True(ok);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ReturnsFalse_WhenItNeverBecomesActive()
    {
        var ok = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ => false, sessionId: 7, CancellationToken.None,
            pollMs: 10, timeoutMs: 50);

        Assert.False(ok);
    }

    [Fact]
    public async Task ChecksOnceMoreAfterTheFinalSleep()
    {
        // The last sleep can be exactly when the session goes Active. Without the re-check
        // after the loop, that transition is thrown away and the seat is failed for nothing.
        var calls = 0;
        var ok = await SessionHealthCheck.WaitForSessionActiveAsync(
            _ =>
            {
                calls++;
                return calls > 2;   // false for every in-loop poll at this timeout
            },
            sessionId: 7, CancellationToken.None, pollMs: 40, timeoutMs: 80);

        Assert.True(ok);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await SessionHealthCheck.WaitForSessionActiveAsync(
                _ => false, sessionId: 7, cts.Token, pollMs: 10, timeoutMs: 1000));
    }

    [Fact]
    public async Task PassesTheSessionIdThrough()
    {
        int? seen = null;
        await SessionHealthCheck.WaitForSessionActiveAsync(
            id => { seen = id; return true; }, sessionId: 42, CancellationToken.None);

        Assert.Equal(42, seen);
    }
}
