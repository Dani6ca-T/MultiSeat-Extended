using System.Security.AccessControl;
using System.Security.Principal;
using MultiSeat.Service.Streaming;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// A seat's credentials directory holds its TLS private key. Left inheriting ProgramData's ACL it
/// carries BUILTIN\Users:(RX), so every standard user on the host — including every other seat —
/// can read that key and impersonate the seat's pairing endpoint.
///
/// These assert the shape of the replacement DACL. The rule that matters is the negative one: no
/// entry for Users, and the directory protected from inheritance.
///
/// Measured while writing these: flipping preserveInheritance to true changes nothing here, because
/// the builder returns a FRESH descriptor that is applied wholesale - there are no inherited entries
/// for it to copy. The shape that would be dangerous is reading the directory's own descriptor with
/// GetAccessControl() and protecting THAT. Worth knowing before someone "simplifies" this to do so.
/// </summary>
public class SeatCredentialsAclTests
{
    // Any resolvable SID works; the process's own is guaranteed to exist on the test host.
    private static SecurityIdentifier SeatSid() =>
        WindowsIdentity.GetCurrent().User!;

    private static List<FileSystemAccessRule> Rules(DirectorySecurity acl) =>
        acl.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
           .Cast<FileSystemAccessRule>()
           .ToList();

    [Fact]
    public void InheritanceIsDisabled_NotPreserved()
    {
        // Protection is what stops ProgramData's entries reaching the key. This asserts the flag;
        // the applied outcome is asserted against a real directory below, which is the reading
        // that actually matters.
        var acl = ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid());

        Assert.True(acl.AreAccessRulesProtected);
    }

    [Fact]
    public void UsersAndEveryoneAreAbsent()
    {
        var acl = ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid());

        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

        var identities = Rules(acl).Select(r => r.IdentityReference).ToList();

        Assert.DoesNotContain(users, identities);
        Assert.DoesNotContain(everyone, identities);
        Assert.DoesNotContain(authenticated, identities);
    }

    [Fact]
    public void SystemAndAdministratorsKeepFullControl()
    {
        // The service runs as SYSTEM and must keep seeding files here; an administrator has to be
        // able to inspect and repair a seat. Locking either out would be a support trap.
        var acl = ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid());
        var rules = Rules(acl);

        foreach (var wellKnown in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            var sid = new SecurityIdentifier(wellKnown, null);
            Assert.Contains(rules, r =>
                r.IdentityReference.Equals(sid)
                && r.AccessControlType == AccessControlType.Allow
                && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        }
    }

    [Fact]
    public void TheSeatCanWriteButCannotRewriteThePermissions()
    {
        // Apollo runs as the seat and has to read, rewrite and create files here. Modify covers
        // that; FullControl would also hand it ChangePermissions, letting a seat undo this.
        var acl = ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid());

        var seat = Assert.Single(Rules(acl), r => r.IdentityReference.Equals(SeatSid()));

        Assert.Equal(AccessControlType.Allow, seat.AccessControlType);
        Assert.True(seat.FileSystemRights.HasFlag(FileSystemRights.Modify));
        Assert.False(seat.FileSystemRights.HasFlag(FileSystemRights.ChangePermissions));
        Assert.False(seat.FileSystemRights.HasFlag(FileSystemRights.TakeOwnership));
    }

    [Fact]
    public void EveryEntryIsInheritedByFilesInTheDirectory()
    {
        // The key itself is a file in this directory. An entry that does not carry ObjectInherit
        // protects the folder and leaves the file exactly as exposed as it was.
        var acl = ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid());

        foreach (var rule in Rules(acl))
        {
            Assert.True(
                rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit),
                $"{rule.IdentityReference} does not reach files in the directory");
            Assert.True(
                rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit),
                $"{rule.IdentityReference} does not reach subdirectories");
        }
    }

    [Fact]
    public void AppliedToARealDirectory_TheInheritedUsersEntryIsGone()
    {
        // The tests above assert the descriptor we build. This one asserts what the filesystem
        // ends up with, which is the only thing an attacker sees: a directory that really did
        // inherit a Users entry, and does not have one after the DACL is applied. The control on
        // the inherited entry is load-bearing - without it this passes on a parent that never
        // granted Users anything, which is most temp directories.
        var parent = Path.Combine(Path.GetTempPath(), $"multiseat-acl-{Guid.NewGuid():N}");
        var child = Path.Combine(parent, "credentials");
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        try
        {
            Directory.CreateDirectory(parent);

            // Give the parent an inheritable Users entry, the way ProgramData has one.
            var parentInfo = new DirectoryInfo(parent);
            var parentAcl = parentInfo.GetAccessControl();
            parentAcl.AddAccessRule(new FileSystemAccessRule(
                users, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                PropagationFlags.None, AccessControlType.Allow));
            parentInfo.SetAccessControl(parentAcl);

            Directory.CreateDirectory(child);

            // Control: the child must actually have inherited it, or this test proves nothing.
            var inherited = new DirectoryInfo(child)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Any(r => r.IdentityReference.Equals(users));
            Assert.True(inherited, "the child did not inherit a Users entry, so this test cannot fail");

            new DirectoryInfo(child).SetAccessControl(
                ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid()));

            var after = new DirectoryInfo(child)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

            Assert.DoesNotContain(after, r => r.IdentityReference.Equals(users));
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ExactlyThreeEntries_NothingElseSlipsIn()
    {
        // Stated as a count so an entry added later has to be justified against this test rather
        // than quietly widening who can read a private key.
        Assert.Equal(3, Rules(ApolloConfigBuilder.BuildSeatCredentialsAcl(SeatSid())).Count);
    }
}
