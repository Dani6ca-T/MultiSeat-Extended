using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Storage;
using Xunit;

namespace MultiSeat.Tests.Storage;

/// <summary>
/// Guards the two protections on MultiSeat's secret files — the seat credential store
/// (accounts.json) and the dashboard API key (api-key.txt).
///
/// Both lived under C:\ProgramData\MultiSeat with the folder's inherited DACL, which grants
/// BUILTIN\Users read. Every local account on the host — including every seat account MultiSeat
/// itself creates — could read the API key, and could read the encrypted passwords. The passwords
/// were then DPAPI-protected at LocalMachine scope, which any process on the machine can decrypt
/// regardless of user, so the encryption added nothing against a local reader.
///
/// These tests pin the fix: an explicit two-entry DACL with inheritance off, and a credential
/// format that only SYSTEM can decrypt while still reading the old machine-scoped blobs so
/// existing installs survive the upgrade.
/// </summary>
public class SecureFileTests
{
    private static string NewTempFile(string contents = "secret")
    {
        var path = Path.Combine(Path.GetTempPath(), $"multiseat-sec-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void RestrictToSystemAndAdmins_RemovesInheritanceAndGrantsOnlySystemAndAdmins()
    {
        var path = NewTempFile();
        try
        {
            SecureFile.RestrictToSystemAndAdmins(path);

            var acl = new FileInfo(path).GetAccessControl();
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
                           .Cast<FileSystemAccessRule>()
                           .ToList();

            // Inheritance off is the load-bearing part: without it the ProgramData grant to
            // BUILTIN\Users comes straight back and the explicit ACEs below are just additions.
            Assert.True(acl.AreAccessRulesProtected);

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            Assert.Contains(rules, r => r.IdentityReference.Equals(system)
                                        && r.FileSystemRights.HasFlag(FileSystemRights.Read));
            Assert.Contains(rules, r => r.IdentityReference.Equals(admins)
                                        && r.FileSystemRights.HasFlag(FileSystemRights.Read));

            // The whole point: no Users ACE, and nothing else besides the two we granted.
            Assert.DoesNotContain(rules, r => r.IdentityReference.Equals(users));
            Assert.All(rules, r => Assert.True(
                r.IdentityReference.Equals(system) || r.IdentityReference.Equals(admins),
                $"Unexpected principal on the restricted file: {r.IdentityReference}"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RestrictToSystemAndAdmins_IsIdempotent()
    {
        // Called on every read and every write, so applying it twice must not accumulate ACEs
        // or throw.
        var path = NewTempFile();
        try
        {
            SecureFile.RestrictToSystemAndAdmins(path);
            SecureFile.RestrictToSystemAndAdmins(path);

            var rules = new FileInfo(path).GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier));

            Assert.Equal(2, rules.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRestrict_ReportsFailureInsteadOfThrowing()
    {
        // Hardening is best-effort at the call sites: failing to tighten an ACL must not stop the
        // service from persisting credentials it needs to provision seats.
        var missing = Path.Combine(Path.GetTempPath(), $"multiseat-absent-{Guid.NewGuid():N}.txt");

        var applied = SecureFile.TryRestrictToSystemAndAdmins(missing, _ => { });

        Assert.False(applied);
    }

    [Fact]
    public void DecryptPassword_RoundTripsCurrentScope()
    {
        var blob = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes("s3at-p4ss"), null, DataProtectionScope.CurrentUser));

        Assert.Equal("s3at-p4ss", AccountManager.DecryptPassword(blob));
    }

    [Fact]
    public void DecryptPassword_StillReadsLegacyMachineScopedBlobs()
    {
        // The upgrade path that matters. A host with seats already provisioned has a store full of
        // LocalMachine blobs; if those stopped decrypting, every seat would fail to launch with
        // "No stored credential" and would have to be re-provisioned by hand.
        var legacy = Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes("legacy-p4ss"), null, DataProtectionScope.LocalMachine));

        Assert.Equal("legacy-p4ss", AccountManager.DecryptPassword(legacy));
    }

    [Fact]
    public void DecryptPassword_ScopeArgumentDoesNotSelectTheKey()
    {
        // Documents the DPAPI behaviour the migration design depends on, and the reason the scope
        // is recorded in the JSON rather than inferred: Unprotect recovers the scope from the blob
        // and ignores the argument, so asking for CurrentUser on a machine-scoped blob SUCCEEDS.
        // The first version of this migration detected "legacy" by catching a CryptographicException
        // here — an exception that never comes — so it would have re-encrypted nothing, forever.
        var machineBlob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes("either-way"), null, DataProtectionScope.LocalMachine);

        var viaCurrentUser = ProtectedData.Unprotect(machineBlob, null, DataProtectionScope.CurrentUser);

        Assert.Equal("either-way", Encoding.UTF8.GetString(viaCurrentUser));
    }

    [Theory]
    [InlineData(null, true)]      // store written before this change — the case that must migrate
    [InlineData("", true)]
    [InlineData("machine", true)] // any unrecognised tag is treated as needing a rewrite
    [InlineData("user", false)]
    [InlineData("USER", false)]   // tag comparison is case-insensitive
    public void IsLegacyScope_TreatsAnythingButTheCurrentTagAsNeedingRewrite(
        string? tag, bool expectedLegacy)
    {
        Assert.Equal(expectedLegacy, AccountManager.IsLegacyScope(tag));
    }
}
