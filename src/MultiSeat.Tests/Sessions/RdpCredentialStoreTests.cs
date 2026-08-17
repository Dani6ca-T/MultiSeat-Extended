using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Interop;
using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Covers the replacement for <c>cmdkey.exe /pass:&lt;password&gt;</c>.
///
/// The seat password used to be passed on a process command line. It is now written straight to
/// the Windows credential store, which means hand-marshalled CREDENTIAL interop — a struct layout
/// or a blob size that is subtly wrong would not fail loudly, it would produce a credential mstsc
/// cannot use, and the only symptom would be a seat that times out waiting for a login prompt
/// nobody can answer.
///
/// So these assert against the real credential store, and read the result back with cmdkey itself:
/// what we write has to be what cmdkey used to write.
/// </summary>
public class RdpCredentialStoreTests
{
    // Deliberately not TERMSRV/127.0.0.2 — that is the live target a seat launch uses, and a test
    // must not delete a credential a real provision is relying on.
    private const string TestTarget = "MultiSeatTest/credential-roundtrip";

    private static SafeTokenHandle CurrentProcessToken()
    {
        Assert.True(AdvApi.OpenProcessToken(
            Kernel32.GetCurrentProcess(), AdvApi.TOKEN_ALL_ACCESS, out var raw),
            "OpenProcessToken failed");
        return new SafeTokenHandle(raw);
    }

    private static string CmdKeyList()
    {
        var psi = new ProcessStartInfo("cmdkey.exe", $"/list:{TestTarget}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    [Fact]
    public void Write_StoresACredentialCmdKeyCanSee_AndDeleteRemovesIt()
    {
        var store = new RdpCredentialStore(NullLogger.Instance);
        using var token = CurrentProcessToken();
        var user = $"{Environment.MachineName}\\multiseat-test-user";

        try
        {
            Assert.True(store.Write(token, TestTarget, user, "not-a-real-password"),
                "CredWrite reported failure");

            var listed = CmdKeyList();

            // cmdkey's output is localised, so assert on the values themselves rather than on any
            // surrounding wording.
            Assert.Contains(TestTarget, listed, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(user, listed, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            store.Delete(token, TestTarget);
        }

        // Assert on the user, not the target: when nothing is stored, cmdkey's "there are no
        // stored credentials for X" message echoes the target name straight back, so looking for
        // the target here passes whether or not the delete worked.
        Assert.DoesNotContain(user, CmdKeyList(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_OverwritesAnExistingCredentialForTheSameTarget()
    {
        // A seat can be provisioned, torn down and provisioned again; a stale credential from a
        // previous launch must not win over the current one.
        var store = new RdpCredentialStore(NullLogger.Instance);
        using var token = CurrentProcessToken();

        try
        {
            Assert.True(store.Write(token, TestTarget, $"{Environment.MachineName}\\first", "pw1"));
            Assert.True(store.Write(token, TestTarget, $"{Environment.MachineName}\\second", "pw2"));

            var listed = CmdKeyList();
            Assert.Contains("second", listed, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("first", listed, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            store.Delete(token, TestTarget);
        }
    }

    [Fact]
    public void Delete_OnAnAbsentCredentialIsHarmless()
    {
        // Runs in a finally block on every launch, including ones that failed before writing
        // anything, so it must not throw.
        var store = new RdpCredentialStore(NullLogger.Instance);
        using var token = CurrentProcessToken();

        store.Delete(token, "MultiSeatTest/never-written");
    }
}
