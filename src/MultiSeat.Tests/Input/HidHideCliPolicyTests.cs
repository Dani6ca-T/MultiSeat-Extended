using MultiSeat.Service.Input;
using Xunit;

namespace MultiSeat.Tests.Input;

/// <summary>
/// HidHideCLI's transport traps, as policy rather than plumbing. Each of these was measured on a
/// two-seat host by @jmlopezdona (issue #19) and each fails the same way: the tool returns
/// something that looks like a perfectly good answer meaning "nothing is configured", when in fact
/// it did not answer at all. A monitor that believes it goes on to "restore" over entries a user
/// wrote by hand.
///
/// The parsing lives in HidHideParserTests; this is the deciding.
/// </summary>
public class HidHideCliPolicyTests
{
    // A healthy run replays its switches: the cloak state comes back as "--cloak-on" or
    // "--cloak-off" on its own line, BEFORE the JSON. That replay is the tell.
    //
    // ⚠️ The first version of this fixture was invented — "Cloaking: enabled" — and two tests here
    // failed because of it, correctly. This tool does not talk in prose. Anything asserting
    // against it has to use what it actually emits, which is the same lesson that produced
    // ParsesAListingThatIsPrefixedByTheCloakStateTell in HidHideParserTests.
    private const string HealthyRead = """
        --cloak-off
        [
          {
            "friendlyName": "Controller (XBOX 360 For Windows)",
            "present": true
          }
        ]
        """;

    private static HidHideCliResult Result(
        string output, int exitCode = 0, bool timedOut = false, bool denied = false) =>
        new(exitCode, output, timedOut, denied);

    // ── Did the tool actually answer? ─────────────────────────────────

    [Fact]
    public void AnEmptyTranscriptIsNotAnEmptyConfiguration()
    {
        // The whole point. Back-to-back invocations return nothing, and nothing reads exactly like
        // "the blacklist is empty". Three chained reads once reported an empty blacklist, an empty
        // cloak state and an empty application list while all three were populated.
        Assert.False(Result("").Answered);
        Assert.False(Result("   \n  ").Answered);
    }

    [Fact]
    public void ARunThatReplayedItsCloakStateAnswered()
    {
        Assert.True(Result(HealthyRead).Answered);
    }

    [Fact]
    public void SucceededIgnoresWhetherThereWasContent()
    {
        // Succeeded is about the invocation; Answered is about the reading. Conflating them is how
        // an empty read passes for a successful one.
        Assert.True(Result("").Succeeded);
        Assert.False(Result("").Answered);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ANonZeroExitIsNotASuccess(int exitCode)
    {
        Assert.False(Result(HealthyRead, exitCode: exitCode).Succeeded);
    }

    [Fact]
    public void ATimeoutOrADeniedRunIsNotASuccessEvenAtExitZero()
    {
        // The denied case genuinely exits 0, so nothing but this flag distinguishes it.
        Assert.False(Result(HealthyRead, timedOut: true).Succeeded);
        Assert.False(Result(HealthyRead, denied: true).Succeeded);
    }

    // ── The busy control device ───────────────────────────────────────

    [Theory]
    [InlineData("Error code 0x0005 while opening the control device")]
    [InlineData("Access denied")]
    [InlineData("ACCESS DENIED")]                       // casing varies
    [InlineData("...\nerror code 0X0005\n...")]         // and so does the hex casing
    public void TheBusyControlDeviceIsRecognised(string output)
    {
        // It refuses the second caller and does nothing, at exit code 0, so this string is the only
        // evidence. As SYSTEM too — it is not an elevation problem, and treating it as one sends
        // people down the wrong path.
        Assert.True(HidHideCli.LooksAccessDenied(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Cloaking: enabled")]
    [InlineData("HID\\VID_045E&PID_028E&IG_00\\3&130C1E12&0&0000")]
    public void OrdinaryOutputIsNotMistakenForIt(string output)
    {
        Assert.False(HidHideCli.LooksAccessDenied(output));
    }

    // ── When to try again ─────────────────────────────────────────────

    [Fact]
    public void AReadThatDidNotAnswerIsRetried()
    {
        Assert.True(HidHideCli.WorthRetrying(Result(""), isRead: true));
    }

    [Fact]
    public void AWriteIsNotRetriedJustForHavingNoAnswer()
    {
        // Writes are never asked for the cloak state, so they never have one. Retrying on that
        // would double the length of every pass — and a pass that is slow is a pass that is wrong,
        // because HidHide filters at open time and a late rule missed the pad it was written for.
        Assert.False(HidHideCli.WorthRetrying(Result(""), isRead: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABusyControlDeviceIsRetriedForReadsAndWritesAlike(bool isRead)
    {
        Assert.True(HidHideCli.WorthRetrying(Result("", denied: true), isRead));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATimeoutIsRetriedForReadsAndWritesAlike(bool isRead)
    {
        Assert.True(HidHideCli.WorthRetrying(Result("", timedOut: true), isRead));
    }

    [Fact]
    public void AGoodReadIsNotRetried()
    {
        Assert.False(HidHideCli.WorthRetrying(Result(HealthyRead), isRead: true));
    }

    // ── How a read is shaped ──────────────────────────────────────────

    [Fact]
    public void AReadAsksForTheCloakStateFirstAndCancelsLast()
    {
        // --cancel has to come after what it is cancelling, and the cloak state has to be asked
        // for or the result can never tell a failed read from an empty one. Without --cancel a
        // bare listing SAVES the configuration it was asked to report on.
        var args = HidHideCli.ReadArguments("--dev-list");

        Assert.Equal("--cloak-state --dev-list --cancel", args);
    }

    [Fact]
    public void AReadWithNothingToListStillCancels()
    {
        Assert.Equal("--cloak-state --cancel", HidHideCli.ReadArguments(""));
    }

    [Fact]
    public void SequenceDropsEmptyCommandsRatherThanEmittingDoubleSpaces()
    {
        // Commands are joined into one invocation because each one costs ~800 ms and a rule that
        // lands late did not apply to the pad it was written for. A blank in the middle would
        // otherwise produce a double space in the argument string.
        Assert.Equal(
            "--cloak-state --dev-list --cancel",
            HidHideCli.Sequence("--cloak-state", "", "--dev-list", null!, "   ", "--cancel"));
    }

    // ── The measured gap ──────────────────────────────────────────────

    [Fact]
    public void TheMinimumGapIsNotShortenedBelowWhatWasMeasured()
    {
        // 800 ms is not a guess: below it the CLI starts returning empty output that reads exactly
        // like an empty configuration. Anyone tempted to shorten this for speed should have to
        // change a test that says why.
        Assert.True(HidHideCli.MinimumGap >= TimeSpan.FromMilliseconds(800));
    }
}
