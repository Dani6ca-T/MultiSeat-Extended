using System.Security.Principal;
using MultiSeat.Service.Accounts;
using Xunit;

namespace MultiSeat.Tests.Accounts;

/// <summary>
/// Covers how seat accounts are placed in Windows groups.
///
/// Two things were wrong here. Seats were made local Administrators, justified by a comment
/// claiming SudoVDA IPC needs it — it does not; the driver's INF grants Everyone the exact access
/// Apollo's client asks for, and an account in Users only opens the interface successfully. And
/// the group names were passed as English literals to an API that takes a name, so on a Spanish or
/// German Windows the membership calls simply failed and the account was left out of the group.
/// </summary>
public class SeatGroupTests
{
    [Theory]
    [InlineData(WellKnownSidType.BuiltinUsersSid)]
    [InlineData(WellKnownSidType.BuiltinAdministratorsSid)]
    [InlineData(WellKnownSidType.BuiltinRemoteDesktopUsersSid)]
    public void ResolveLocalGroupName_ReturnsANameThatMapsBackToTheSameSid(WellKnownSidType sid)
    {
        var name = AccountManager.ResolveLocalGroupName(sid, "fallback-should-not-be-used");

        Assert.NotEqual("fallback-should-not-be-used", name);

        // The real assertion, and the one that holds in any locale: whatever name came back has to
        // identify the very group we asked for. Comparing against "Users" would only pass here.
        var resolved = (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
        Assert.True(resolved.IsWellKnown(sid), $"'{name}' did not resolve back to {sid}");
    }

    [Theory]
    [InlineData(WellKnownSidType.BuiltinUsersSid)]
    [InlineData(WellKnownSidType.BuiltinRemoteDesktopUsersSid)]
    public void ResolveLocalGroupName_StripsTheDomainPrefix(WellKnownSidType sid)
    {
        // Translate() yields "BUILTIN\Users"; NetLocalGroupAddMembers wants the bare group name,
        // and passing the qualified form fails.
        var name = AccountManager.ResolveLocalGroupName(sid, "fallback");

        Assert.DoesNotContain('\\', name);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void ResolveLocalGroupName_FallsBackWhenTheSidCannotBeTranslated()
    {
        // LogonIdsSid cannot be constructed without a domain SID, so this exercises the failure
        // path: resolution must not throw during static initialisation of AccountManager.
        var name = AccountManager.ResolveLocalGroupName(WellKnownSidType.LogonIdsSid, "Users");

        Assert.Equal("Users", name);
    }
}
