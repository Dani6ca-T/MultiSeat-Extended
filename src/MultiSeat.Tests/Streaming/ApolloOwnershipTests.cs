using MultiSeat.Service.Streaming;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// MultiSeat promises it never touches an Apollo it did not launch — that promise is what lets
/// it coexist with a standalone Apollo serving the console player. <c>IsMultiSeatManaged</c> is
/// where the promise is decided, from a process's executable path and command line.
///
/// There were TWO copies of this decision and they had drifted. The reporting path guarded both
/// emptiness checks; the path that KILLS orphaned processes guarded only one, so an empty
/// ApolloConfigDir would have made every Apollo on the host look managed. Consolidated into
/// ApolloOwnership, which is what these now cover for both callers.
///
/// Both ways of being wrong are silent and expensive: classify a foreign Apollo as ours and
/// cleanup kills someone's live stream; classify ours as foreign and orphans accumulate.
/// </summary>
public class ApolloOwnershipTests
{
    private const string ManagedExeDir   = @"C:\Program Files\ApolloVibe";
    private const string ManagedConfigDir = @"C:\ProgramData\MultiSeat\apollo";

    private const string OurExe     = @"C:\Program Files\ApolloVibe\sunshine.exe";
    private const string ConsoleExe = @"C:\Program Files\Apollo\Sunshine.exe";

    [Fact]
    public void ExecutableUnderOurInstallDir_IsOurs()
    {
        Assert.True(ApolloOwnership.IsMultiSeatManaged(
            OurExe, cmdLine: null, ManagedExeDir, ManagedConfigDir));
    }

    [Fact]
    public void SeatConfigOnTheCommandLine_IsOurs()
    {
        // The second signal exists because a seat's Apollo can be launched from elsewhere while
        // still being ours; the per-seat config path is the giveaway.
        Assert.True(ApolloOwnership.IsMultiSeatManaged(
            exePath: @"D:\somewhere\sunshine.exe",
            cmdLine: @"""D:\somewhere\sunshine.exe"" ""C:\ProgramData\MultiSeat\apollo\Gaming\sunshine.conf""",
            ManagedExeDir, ManagedConfigDir));
    }

    [Fact]
    public void StandaloneConsoleApollo_IsNotOurs()
    {
        // The coexistence guarantee, stated as a test. This is the exact process on the
        // reference host: a service-managed Apollo in C:\Program Files\Apollo serving the
        // console. Killing it would end a real person's stream.
        Assert.False(ApolloOwnership.IsMultiSeatManaged(
            ConsoleExe,
            cmdLine: @"""C:\Program Files\Apollo\Sunshine.exe""",
            ManagedExeDir, ManagedConfigDir));
    }

    [Fact]
    public void PathComparisonIgnoresCase()
    {
        // WMI reports whatever casing the process was launched with; Windows paths are
        // case-insensitive, so a lowercase launch must not read as a foreign process.
        Assert.True(ApolloOwnership.IsMultiSeatManaged(
            @"c:\program files\apollovibe\SUNSHINE.EXE", cmdLine: null,
            ManagedExeDir, ManagedConfigDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyManagedExeDir_DoesNotClaimEveryApollo(string? managedExeDir)
    {
        // The dangerous one. "anything".StartsWith("") is TRUE, so without the emptiness guard
        // an unconfigured or unresolved install directory would mark EVERY Apollo on the host
        // as ours — including the console player's — and cleanup would kill it. The guard is
        // load-bearing and invisible; this is the test that notices if it is removed.
        Assert.False(ApolloOwnership.IsMultiSeatManaged(
            ConsoleExe, cmdLine: null, managedExeDir, ManagedConfigDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyManagedConfigDir_DoesNotClaimEveryApollo(string? managedConfigDir)
    {
        // Same hazard on the command-line signal: Contains("") is TRUE for every string.
        Assert.False(ApolloOwnership.IsMultiSeatManaged(
            ConsoleExe, cmdLine: @"""C:\Program Files\Apollo\Sunshine.exe""",
            ManagedExeDir, managedConfigDir));
    }

    [Fact]
    public void NothingKnownAboutTheProcess_IsNotOurs()
    {
        // WMI can return neither path nor command line. Reporting "not ours" then is the
        // fail-safe direction: we skip a process rather than kill a stranger's.
        Assert.False(ApolloOwnership.IsMultiSeatManaged(
            exePath: null, cmdLine: null, ManagedExeDir, ManagedConfigDir));
    }

    [Fact]
    public void SimilarlyNamedNeighbourDirectory_IsNotOurs()
    {
        // "C:\Program Files\ApolloVibe" is a prefix of "C:\Program Files\ApolloVibeOld", so a
        // plain prefix test also claims the neighbour. Documented as current behaviour: this
        // asserts what the code does today, and is the place to change if it ever bites.
        var neighbour = @"C:\Program Files\ApolloVibeOld\sunshine.exe";

        Assert.True(ApolloOwnership.IsMultiSeatManaged(
            neighbour, cmdLine: null, ManagedExeDir, ManagedConfigDir));
    }
}
