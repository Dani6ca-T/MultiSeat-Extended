using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Firewall rules are opened when a seat is provisioned and deleted when it is torn down. Both
/// halves fail quietly: open the wrong ports and the seat simply refuses connections with nothing
/// logged, and if the delete cannot find the rules it made, every seat ever provisioned leaves its
/// rules behind for good.
///
/// The rule name used to be written out three times — open, close, and the existence check. These
/// cover the single definition that replaced them, and the port sets that decide what is exposed.
/// </summary>
public class FirewallManagerTests
{
    private const int SeatBase = 48100;   // seat 0 under the shipped PortBase

    // ── The name all three operations have to agree on ────────────────

    [Fact]
    public void RuleNameIsExactlyThePrefixAndTheSeatId()
    {
        // Pinned to the exact string on purpose. This name is a persisted external identifier:
        // rules created by one version are deleted by whatever version runs next, so changing the
        // format silently orphans every rule on every host that upgrades. A change here should
        // have to be deliberate enough to update a test.
        var id = Guid.Parse("fe02c5ac-e180-4d23-8cc5-7b460a8d6030");

        Assert.Equal("MultiSeat-Seat-fe02c5ace1804d238cc57b460a8d6030", FirewallManager.RuleName(id));
    }

    [Fact]
    public void RuleNameDoesNotDependOnWhenItIsCalled()
    {
        // Close and RulesExist recompute the name rather than remembering it, so anything
        // time-based in it orphans the rules it just created.
        //
        // Comparing two back-to-back calls does NOT test this: a tick counter returns the same
        // value twice in the same microsecond, so that version of this test passed while the name
        // contained Environment.TickCount. Straddle a tick boundary instead.
        var id = Guid.NewGuid();

        var first = FirewallManager.RuleName(id);
        Thread.Sleep(40);

        Assert.Equal(first, FirewallManager.RuleName(id));
    }

    [Fact]
    public void RuleNamesAreDistinctPerSeat()
    {
        Assert.NotEqual(FirewallManager.RuleName(Guid.NewGuid()), FirewallManager.RuleName(Guid.NewGuid()));
    }

    [Fact]
    public void RuleNameCarriesNoCharacterThatWouldBreakTheNetshArgument()
    {
        // The name goes into netsh inside double quotes. A quote or a stray brace in it would
        // truncate the argument, and a delete built from a truncated name matches nothing.
        var name = FirewallManager.RuleName(Guid.NewGuid());

        Assert.StartsWith("MultiSeat-Seat-", name, StringComparison.Ordinal);
        Assert.DoesNotContain('"', name);
        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain('{', name);   // "N" format, so no braces or dashes from the Guid
    }

    // ── What actually gets exposed ────────────────────────────────────

    [Fact]
    public void TcpPortsAreTheOnesMoonlightNeedsToPairAndLaunch()
    {
        var ports = FirewallManager.TcpPorts(SeatBase, emulatorNetplay: false);

        Assert.Equal(
            new[]
            {
                SeatBase + Constants.OffsetGfeHttps,   // 48095 — pairing/launch
                SeatBase + Constants.OffsetGfeHttp,    // 48100 — plaintext fallback
                SeatBase + Constants.OffsetWebUi,      // 48101 — Apollo web UI
                SeatBase + Constants.OffsetRtsp,       // 48126 — session setup
            },
            ports);
    }

    [Fact]
    public void UdpPortsAreTheMediaStreamsAndTheControlChannel()
    {
        var ports = FirewallManager.UdpPorts(SeatBase);

        Assert.Equal(
            new[]
            {
                SeatBase + Constants.OffsetVideo,
                SeatBase + Constants.OffsetControl,
                SeatBase + Constants.OffsetAudio,
                SeatBase + Constants.OffsetMic,
            },
            ports);
    }

    [Fact]
    public void NetplayPortIsOpenedOnlyWhenThatFeatureIsOn()
    {
        var netplayPort = SeatBase + Constants.OffsetRetroArchNetplay;

        Assert.DoesNotContain(netplayPort, FirewallManager.TcpPorts(SeatBase, emulatorNetplay: false));
        Assert.Contains(netplayPort, FirewallManager.TcpPorts(SeatBase, emulatorNetplay: true));
    }

    [Fact]
    public void TurningNetplayOnAddsThatPortAndNothingElse()
    {
        // A feature flag that quietly widened the exposed surface by more than its own port would
        // be hard to notice from either the code or the firewall UI.
        var off = FirewallManager.TcpPorts(SeatBase, emulatorNetplay: false);
        var on = FirewallManager.TcpPorts(SeatBase, emulatorNetplay: true);

        Assert.Equal(
            new[] { SeatBase + Constants.OffsetRetroArchNetplay },
            on.Except(off).ToArray());
        Assert.Empty(off.Except(on));
    }

    // ── Cross-checks against the rest of the port layout ──────────────

    [Fact]
    public void EveryUsedOffsetIsOpenedOnExactlyOneProtocol()
    {
        // The port layout lives in Constants and is asserted for collisions by
        // Constants_PortsPerSeat_NoUsedPortCollision. This is the other half of that pact: a port
        // the system uses but the firewall never opens means a seat that works over loopback and
        // refuses every LAN client, which looks like a network problem rather than a missing rule.
        int[] usedOffsets =
        [
            Constants.OffsetGfeHttps, Constants.OffsetGfeHttp, Constants.OffsetWebUi,
            Constants.OffsetVideo, Constants.OffsetControl, Constants.OffsetAudio,
            Constants.OffsetMic, Constants.OffsetRtsp, Constants.OffsetRetroArchNetplay,
        ];

        var tcp = FirewallManager.TcpPorts(SeatBase, emulatorNetplay: true).ToHashSet();
        var udp = FirewallManager.UdpPorts(SeatBase).ToHashSet();

        foreach (var off in usedOffsets)
        {
            var port = SeatBase + off;
            Assert.True(
                tcp.Contains(port) ^ udp.Contains(port),
                $"offset {off} (port {port}) is not opened on exactly one protocol");
        }
    }

    [Fact]
    public void NoPortOutsideTheSeatsOwnBlockIsOpened()
    {
        // Every opened port must belong to this seat. Opening one from a neighbouring block would
        // hand a seat's traffic to another seat, and the firewall would look perfectly normal.
        var all = FirewallManager.TcpPorts(SeatBase, emulatorNetplay: true)
            .Concat(FirewallManager.UdpPorts(SeatBase));

        var low = SeatBase + Constants.OffsetGfeHttps;          // the block starts below the base
        var high = SeatBase + Constants.OffsetRtsp;

        Assert.All(all, p => Assert.InRange(p, low, high));
    }

    [Fact]
    public void TwoSeatsNeverShareAnOpenedPort()
    {
        var seat0 = FirewallManager.TcpPorts(Constants.PortBase, true)
            .Concat(FirewallManager.UdpPorts(Constants.PortBase));
        var seat1Base = Constants.PortBase + Constants.PortsPerSeat;
        var seat1 = FirewallManager.TcpPorts(seat1Base, true)
            .Concat(FirewallManager.UdpPorts(seat1Base));

        Assert.Empty(seat0.Intersect(seat1));
    }
}
