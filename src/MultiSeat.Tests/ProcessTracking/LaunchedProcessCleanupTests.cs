using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.ProcessTracking;

/// <summary>
/// Regression tests for <see cref="LaunchedProcessCleanup"/> — the identity-safe helper seat
/// teardown uses to terminate launched apps explicitly instead of relying on session logoff.
///
/// The safety contract under test: a process is killed ONLY while its PID still denotes the
/// exact process instance that was launched (PID + start time match). Already-exited and
/// recycled-PID candidates are never touched — killing a recycled PID would terminate an
/// unrelated process. Per-process failures are isolated so one bad kill never aborts the rest
/// of teardown.
/// </summary>
public class LaunchedProcessCleanupTests
{
    /// <summary>A durable local process (ping -t runs until killed).</summary>
    private static Process SpawnSleeper()
    {
        var psi = new ProcessStartInfo("ping.exe", "-t 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        return Process.Start(psi)!;
    }

    private static ProcessIdentity IdentityOf(Process p) =>
        new(p.Id, p.StartTime.ToUniversalTime());

    private static void KillQuietly(Process p)
    {
        try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { /* best effort */ }
        p.Dispose();
    }

    [Fact]
    public void TerminateAll_MatchingIdentity_TerminatesProcessTree()
    {
        var sleeper = SpawnSleeper();
        try
        {
            var identity = IdentityOf(sleeper);
            Assert.False(sleeper.HasExited);

            LaunchedProcessCleanup.TerminateAll([identity], NullLogger.Instance);

            Assert.True(sleeper.WaitForExit(5000),
                "The launched process matching its identity must be terminated");
        }
        finally
        {
            KillQuietly(sleeper);
        }
    }

    [Fact]
    public void TerminateAll_AlreadyExited_IsSuccessfulCleanup()
    {
        // The app exited before teardown: GetProcessById finds nothing, and cleanup must treat
        // that as done — no throw, no kill of anything else.
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        var identity = IdentityOf(gone); // capture while alive
        Assert.True(gone.WaitForExit(5000));

        LaunchedProcessCleanup.TerminateAll([identity], NullLogger.Instance); // must not throw
    }

    [Fact]
    public void TerminateAll_RecycledPid_DoesNotTerminateUnrelatedProcess()
    {
        // The PID is occupied by a DIFFERENT process (recorded start time differs): the
        // original app already exited and Windows recycled the PID. The unrelated process
        // must be left running — this is the exact PID-reuse hazard the identity check exists
        // to prevent.
        var other = SpawnSleeper();
        try
        {
            var staleIdentity = new ProcessIdentity(other.Id, other.StartTime.ToUniversalTime().AddHours(-1));
            Assert.False(other.HasExited);

            LaunchedProcessCleanup.TerminateAll([staleIdentity], NullLogger.Instance);

            // Give any (wrong) kill attempt a moment to happen, then require the process alive.
            Thread.Sleep(300);
            Assert.False(other.HasExited,
                "A recycled PID must never cause an unrelated process to be terminated");
        }
        finally
        {
            KillQuietly(other);
        }
    }

    [Fact]
    public void TerminateAll_MultipleProcesses_AttemptsEachMatchingIdentity()
    {
        var a = SpawnSleeper();
        var b = SpawnSleeper();
        try
        {
            LaunchedProcessCleanup.TerminateAll([IdentityOf(a), IdentityOf(b)], NullLogger.Instance);

            Assert.True(a.WaitForExit(5000));
            Assert.True(b.WaitForExit(5000));
        }
        finally
        {
            KillQuietly(a);
            KillQuietly(b);
        }
    }

    [Fact]
    public void TerminateAll_OneFailure_DoesNotAbortRemainingCandidates()
    {
        // Failure isolation (injected delegates, no real processes): a kill that throws for
        // the first candidate must not stop the second from being attempted, and nothing
        // propagates to the teardown caller.
        var attempts = 0;
        var secondAttempted = false;

        LaunchedProcessCleanup.TerminateAll(
            [new ProcessIdentity(111, DateTimeOffset.UtcNow),
             new ProcessIdentity(222, DateTimeOffset.UtcNow)],
            isAliveAndSame: _ => true,
            killTree: _ =>
            {
                attempts++;
                if (attempts == 1)
                    throw new InvalidOperationException("simulated kill failure");
                secondAttempted = true;
            },
            NullLogger.Instance);

        Assert.Equal(2, attempts);
        Assert.True(secondAttempted);
    }

    [Fact]
    public void TerminateAll_NonMatchingIdentity_IsSkippedWithoutKill()
    {
        // When the identity no longer matches the OS (recycled or exited), the kill action is
        // never invoked at all.
        var killInvoked = false;
        LaunchedProcessCleanup.TerminateAll(
            [new ProcessIdentity(333, DateTimeOffset.UtcNow)],
            isAliveAndSame: _ => false,
            killTree: _ => killInvoked = true,
            NullLogger.Instance);

        Assert.False(killInvoked);
    }
}
