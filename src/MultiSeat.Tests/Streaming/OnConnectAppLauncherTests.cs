using System.Text;
using MultiSeat.Service.Streaming;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Launch-on-connect fires off edges detected by tailing Apollo's per-seat log. Every way this
/// can be wrong is silent: apps do not launch, launch twice, or launch on the wrong edge, and
/// nothing reports an error — the seat just behaves oddly. These lock the three decisions the
/// feature rests on: which marker is last, what survives a read boundary, and where a fresh
/// watcher starts reading.
/// </summary>
public class OnConnectAppLauncherTests
{
    private const string Connect    = "CLIENT CONNECTED";
    private const string Disconnect = "CLIENT DISCONNECTED";

    private static string Line(string marker) =>
        $"[2026-08-29 10:26:20.078]: Info: {marker}\n";

    private static string TempLog(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"multiseat-onconnect-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void Append(string path, string content) =>
        File.AppendAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    // ── Which marker is last ─────────────────────────────────────────────

    [Fact]
    public void NoMarker_ReportsNothingRatherThanDisconnected()
    {
        // null means "no news", which is what stops an ordinary log line being read as an
        // edge. Returning false here would fire a disconnect on every tick of a quiet log.
        Assert.Null(OnConnectAppLauncher.LastMarkerIsConnected("Info: Client dynamicRange: 0"));
        Assert.Null(OnConnectAppLauncher.LastMarkerIsConnected(string.Empty));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SingleMarker_IsReportedAsItself(bool connected)
    {
        var text = Line(connected ? Connect : Disconnect);
        Assert.Equal(connected, OnConnectAppLauncher.LastMarkerIsConnected(text));
    }

    [Fact]
    public void LastMarkerWins_InBothOrders()
    {
        Assert.False(OnConnectAppLauncher.LastMarkerIsConnected(Line(Connect) + Line(Disconnect)));
        Assert.True(OnConnectAppLauncher.LastMarkerIsConnected(Line(Disconnect) + Line(Connect)));
    }

    [Fact]
    public void DisconnectIsNotReadAsAConnect()
    {
        // The production code notes that "CLIENT DISCONNECTED" does not contain
        // "CLIENT CONNECTED" and relies on it. If either marker is ever reworded so one
        // contains the other, a disconnect starts reading as a connect and apps relaunch
        // on every disconnect. Assert the property rather than trusting the comment.
        Assert.DoesNotContain(Connect, Disconnect, StringComparison.Ordinal);
        Assert.False(OnConnectAppLauncher.LastMarkerIsConnected(Line(Disconnect)));
    }

    [Fact]
    public void RepeatedSameMarker_StillReportsThatState()
    {
        // No edge is computed here — that is ProcessSeat's job — so a repeat must report the
        // state, not null. Reporting null would strand a seat whose log repeats a marker.
        var text = Line(Connect) + Line(Connect);
        Assert.True(OnConnectAppLauncher.LastMarkerIsConnected(text));
    }

    // ── What survives a read boundary ────────────────────────────────────

    [Fact]
    public void MarkerSplitAcrossTwoReads_IsStillDetected()
    {
        // The case the Carry buffer exists for. Apollo writes a marker while we are mid-tick,
        // so the first read ends inside it. Without the carry the marker is lost for good —
        // the second read starts after it, and no later tick ever sees those bytes again.
        var path = TempLog("startup noise\n");
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);
            var whole = Line(Disconnect);
            var split = whole.IndexOf(Disconnect, StringComparison.Ordinal) + 4; // mid-marker

            Append(path, whole[..split]);
            var first = OnConnectAppLauncher.ReadLatestState(path, state);
            Assert.Null(first);   // half a marker is not a marker

            Append(path, whole[split..]);
            var second = OnConnectAppLauncher.ReadLatestState(path, state);
            Assert.False(second); // reassembled across the boundary
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MarkerSplitAtEveryOffset_IsDetected()
    {
        // The carry keeps MaxMarkerLen - 1 characters, which is exactly enough for the worst
        // split. Walking every boundary is what proves the "- 1" is right rather than lucky:
        // a marker of length N split after k characters needs N - k <= carry for k >= 1.
        var whole = Line(Connect);
        var markerStart = whole.IndexOf(Connect, StringComparison.Ordinal);

        for (var k = 1; k < Connect.Length; k++)
        {
            var path = TempLog("x\n");
            try
            {
                var state = OnConnectAppLauncher.SeedState(path);
                Append(path, whole[..(markerStart + k)]);
                OnConnectAppLauncher.ReadLatestState(path, state);
                Append(path, whole[(markerStart + k)..]);

                Assert.True(
                    OnConnectAppLauncher.ReadLatestState(path, state),
                    $"marker split after {k} character(s) was not detected");
            }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public void CarryIsBoundedByTheLongestMarker()
    {
        // The carry is prepended to every read, so an unbounded one would grow without limit
        // and re-scan the whole log every tick.
        var path = TempLog(string.Empty);
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);
            Append(path, new string('z', 4096));
            OnConnectAppLauncher.ReadLatestState(path, state);

            Assert.Equal(OnConnectAppLauncher.MaxMarkerLen - 1, state.Carry.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NothingAppended_ReportsNoNews()
    {
        var path = TempLog(Line(Connect));
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);
            Assert.Null(OnConnectAppLauncher.ReadLatestState(path, state));
            Assert.Null(OnConnectAppLauncher.ReadLatestState(path, state));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TruncatedLog_RewindsInsteadOfReadingPastTheEnd()
    {
        // Apollo restarting in place leaves a shorter file. An offset past the new end would
        // otherwise mean every later read asks for a negative number of bytes.
        var path = TempLog(Line(Connect) + new string('y', 500));
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);
            Assert.True(state.Offset > 0);

            File.WriteAllText(path, Line(Disconnect));
            var after = OnConnectAppLauncher.ReadLatestState(path, state);

            Assert.False(after);                 // read the new short file from the start
            Assert.Equal(new FileInfo(path).Length, state.Offset);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingLog_IsNotAnError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"multiseat-absent-{Guid.NewGuid():N}.log");
        var state = OnConnectAppLauncher.SeedState(path);   // must not throw

        Assert.Equal(0, state.Offset);
        Assert.False(state.Connected);
        Assert.Null(OnConnectAppLauncher.ReadLatestState(path, state));
    }

    // ── Where a fresh watcher starts ─────────────────────────────────────

    [Fact]
    public void SeedingStartsAtEndOfFile_SoHistoryIsNotReplayed()
    {
        // A seat provisioned against an existing log must not launch apps for a connect that
        // happened yesterday. Seeding at end-of-file is what prevents the replay.
        var path = TempLog(Line(Connect) + Line(Disconnect) + Line(Connect));
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);

            Assert.Equal(new FileInfo(path).Length, state.Offset);
            Assert.True(state.Connected);                                   // inferred, not replayed
            Assert.Null(OnConnectAppLauncher.ReadLatestState(path, state)); // nothing to act on
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SeedingAnEmptyLog_StartsDisconnected()
    {
        var path = TempLog(string.Empty);
        try
        {
            var state = OnConnectAppLauncher.SeedState(path);
            Assert.False(state.Connected);
            Assert.Equal(0, state.Offset);
        }
        finally { File.Delete(path); }
    }
}
