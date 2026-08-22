using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Guards against launching a seat's process into the wrong Windows session.
///
/// These exist because of issue #18, where a reporter's Moonlight client drove the CONSOLE cursor
/// while the seat's own cursor never moved. <c>SendInput</c> is delivered to the calling process's
/// session and cannot cross one, so a process injecting into the console session IS in the console
/// session — whatever its config, its ports and its log say.
///
/// The audit that followed found the launcher had no guard and no signal:
///
///   * <c>WTSQueryUserToken(sessionId)</c> returns whoever occupies that session, ignoring the
///     account name it was called alongside. Ask for the console session and you get the console
///     user's token — whose session id then MATCHES what was requested, so every downstream check
///     passes.
///   * <c>ProcessIdToSessionId</c> was declared in the interop layer and called from nowhere.
///   * the launch log reported the session that was ASKED for, not the one the process landed in,
///     so seven rounds of "healthy" logs went past the fault.
/// </summary>
public class SessionGuardTests
{
    // The cheapest of the guards, and the one that would have turned #18 into a single loud line
    // at provisioning time instead of seven rounds of correspondence.
    [Fact]
    public void RefusesToLaunchASeatProcessIntoTheConsoleSession()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProcessInjector.EnsureNotConsoleSession(
                sessionId: 1, consoleSessionId: 1, exePath: @"C:\ApolloVibe\sunshine.exe",
                allowConsoleSession: false));

        // The message has to name the fault and the fix, because it will be read in a bug report
        // by someone who has never seen this code.
        Assert.Contains("CONSOLE", ex.Message);
        Assert.Contains("sunshine.exe", ex.Message);
        Assert.Contains("re-provision", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsASeatSessionThatIsNotTheConsole()
    {
        // The normal case: a seat in its own RDP session while the console is session 1.
        ProcessInjector.EnsureNotConsoleSession(
            sessionId: 3, consoleSessionId: 1, exePath: @"C:\ApolloVibe\sunshine.exe",
            allowConsoleSession: false);
    }

    // Deliberate console launches exist - VoiceMeeter has to run in an interactive session - and go
    // through LaunchInConsoleSessionAsync. The opt-out keeps that path possible without weakening
    // the default.
    [Fact]
    public void AllowsTheConsoleSessionWhenItIsAskedForExplicitly()
    {
        ProcessInjector.EnsureNotConsoleSession(
            sessionId: 1, consoleSessionId: 1, exePath: @"C:\Program Files\VB\voicemeeter.exe",
            allowConsoleSession: true);
    }

    // Session 0 is the services session. It is not the console session, so this guard does not
    // catch it - and it should not pretend to. A process there is a different fault with a
    // different signature (no desktop at all, rather than the wrong desktop).
    [Fact]
    public void DoesNotClaimToGuardSessionZero()
    {
        ProcessInjector.EnsureNotConsoleSession(
            sessionId: 0, consoleSessionId: 1, exePath: @"C:\ApolloVibe\sunshine.exe",
            allowConsoleSession: false);
    }

    // A host where the console is not session 1 - it moves after a reboot with fast startup, and on
    // servers it is not 1 at all. The guard has to compare against the queried console id rather
    // than a hardcoded 1, which this pins.
    [Fact]
    public void ComparesAgainstTheQueriedConsoleIdNotAHardcodedOne()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProcessInjector.EnsureNotConsoleSession(
                sessionId: 7, consoleSessionId: 7, exePath: @"C:\ApolloVibe\sunshine.exe",
                allowConsoleSession: false));

        ProcessInjector.EnsureNotConsoleSession(
            sessionId: 1, consoleSessionId: 7, exePath: @"C:\ApolloVibe\sunshine.exe",
            allowConsoleSession: false);
    }
}
