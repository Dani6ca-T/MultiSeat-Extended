using MultiSeat.Service.Monitoring;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Monitoring;

/// <summary>
/// The two health judgements the service makes about itself: the score the dashboard shows, and
/// which seats the health check is willing to look at.
///
/// Both are quiet when wrong. A score that reads Healthy while the box is thrashing means nobody
/// looks; one that reads Critical on an idle host means the number stops being read at all. And a
/// seat the check refuses to look at is a seat nothing will ever repair.
/// </summary>
public class HealthTests
{
    private static HealthScore Health(
        int cpu = 10, int mem = 20, int gpu = 5, int seats = 1, bool rdp = true) =>
        MetricsCollector.ComputeHealth(cpu, mem, gpu, seats, rdp);

    // ── The score ─────────────────────────────────────────────────────

    [Fact]
    public void AnIdleHostIsHealthyAndSaysNothingIsWrong()
    {
        var h = Health();

        Assert.Equal(HealthStatus.Healthy, h.Status);
        Assert.Equal(100, h.Score);
        Assert.Empty(h.Issues);
    }

    [Theory]
    [InlineData(74, 100)]   // below the "high" threshold — untouched
    [InlineData(75, 85)]    // high
    [InlineData(89, 85)]
    [InlineData(90, 70)]    // critical
    [InlineData(100, 70)]
    public void CpuIsScoredAtItsThresholds(int cpuPct, int expectedScore)
    {
        // The boundaries are the whole content of this rule, and an off-by-one here shifts every
        // reading the dashboard has ever shown.
        Assert.Equal(expectedScore, Health(cpu: cpuPct).Score);
    }

    [Theory]
    [InlineData(79, 100)]
    [InlineData(80, 90)]
    [InlineData(90, 75)]
    public void MemoryIsScoredAtItsThresholds(int memPct, int expectedScore)
    {
        Assert.Equal(expectedScore, Health(mem: memPct).Score);
    }

    [Theory]
    [InlineData(94, 100)]
    [InlineData(95, 85)]
    public void GpuOnlyCountsWhenItIsNearCapacity(int gpuPct, int expectedScore)
    {
        // A GPU at 80% is a GPU doing its job — this is a streaming host. Only near-capacity is
        // worth saying anything about.
        Assert.Equal(expectedScore, Health(gpu: gpuPct).Score);
    }

    [Fact]
    public void TermWrapBeingInactiveIsTheHeaviestSinglePenalty()
    {
        // Without TermWrap there is no multi-session, so no seat can exist at all. It is weighted
        // like a critical CPU because the consequence is total, not gradual.
        var h = Health(rdp: false);

        Assert.Equal(70, h.Score);
        Assert.Contains(h.Issues, i => i.Contains("TermWrap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryPenaltyContributesAnIssueLine()
    {
        // The score alone does not tell anyone what to do. If a penalty is applied without an
        // issue line, the dashboard shows a number nobody can act on.
        var h = Health(cpu: 95, mem: 95, gpu: 99, rdp: false);

        Assert.Equal(4, h.Issues.Count);
        Assert.Equal(HealthStatus.Critical, h.Status);
    }

    [Fact]
    public void TheWorstCaseIsExactlyZero_SoTheFloorIsCurrentlyUnreachable()
    {
        // Every penalty at once: 30 (cpu) + 25 (mem) + 15 (gpu) + 30 (no RDP) = exactly 100.
        //
        // Which means Math.Max(0, score) in ComputeHealth cannot fire with today's weights - it is
        // defensive code for a case that does not exist yet. Stated here rather than covered by a
        // test that could not fail: removing the floor changes nothing while the penalties sum to
        // 100 or less, so a test asserting "never negative" proves nothing at all.
        //
        // Add a penalty and this assertion is what tells you the floor has started to matter.
        var worst = Health(cpu: 100, mem: 100, gpu: 100, rdp: false);

        Assert.Equal(0, worst.Score);
        Assert.Equal(HealthStatus.Critical, worst.Status);
    }

    [Fact]
    public void StatusBandsAreInclusiveAtTheirLowerEdge()
    {
        // Driven through real inputs rather than synthesised scores, so these assert the function
        // rather than restate the arithmetic. Penalties: cpu>=90 is -30, mem>=90 is -25,
        // gpu>=95 is -15, no-RDP is -30.

        // 100 — nothing wrong
        Assert.Equal(HealthStatus.Healthy, Health().Status);

        // exactly 70 — the lower edge of Healthy, and the one an off-by-one would move
        var atSeventy = Health(cpu: 90);
        Assert.Equal(70, atSeventy.Score);
        Assert.Equal(HealthStatus.Healthy, atSeventy.Status);

        // 60 - inside Warning, and the case that catches the Healthy edge being widened. Without
        // a score in the 60s, moving the threshold from 70 to 60 changes no assertion at all.
        var sixty = Health(cpu: 90, mem: 80);
        Assert.Equal(60, sixty.Score);
        Assert.Equal(HealthStatus.Warning, sixty.Status);

        // exactly 40 — the lower edge of Warning
        var atForty = Health(cpu: 90, rdp: false);
        Assert.Equal(40, atForty.Score);
        Assert.Equal(HealthStatus.Warning, atForty.Status);

        // below it — Critical
        var below = Health(cpu: 90, mem: 90, gpu: 95);
        Assert.Equal(30, below.Score);
        Assert.Equal(HealthStatus.Critical, below.Status);
    }

    // ── Which seats get checked ───────────────────────────────────────

    [Theory]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    public void ALiveSeatIsChecked(SeatStatus status)
    {
        Assert.True(SessionHealthCheck.IsWorthChecking(status));
    }

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.TearingDown)]
    public void ASeatInFluxIsLeftAlone(SeatStatus status)
    {
        // Provisioning and teardown are already moving the seat; checking it would fight them.
        Assert.False(SessionHealthCheck.IsWorthChecking(status));
    }

    [Fact]
    public void ASeatInErrorIsNotChecked_WhichIsWhyItCannotRepairItself()
    {
        // Pinned deliberately, with the consequence stated: nothing in the health check takes a
        // seat out of Error, and the Apollo restart only runs for seats it admits. So a seat in
        // Error stays broken until something outside hands it back — which is exactly the bug
        // PR #22 fixed by having session-reconnect return the seat to Ready.
        //
        // If someone later "fixes" this by admitting Error here, this test should fail and make
        // them explain why the check will not fight a teardown.
        Assert.False(SessionHealthCheck.IsWorthChecking(SeatStatus.Error));
    }
}
