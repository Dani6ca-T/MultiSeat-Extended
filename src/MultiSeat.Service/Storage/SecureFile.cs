using System.Security.AccessControl;
using System.Security.Principal;

namespace MultiSeat.Service.Storage;

/// <summary>
/// Locks a file down to SYSTEM and Administrators only.
///
/// Files under C:\ProgramData inherit a DACL that grants BUILTIN\Users read (and, on this
/// directory, write) — which is correct for the bulk of what MultiSeat keeps there, since seat
/// accounts legitimately read and write their own Apollo config dirs and staging files. It is not
/// correct for the two files that hold secrets: accounts.json (the seat password store) and
/// api-key.txt (the dashboard API key). Both were readable by every local account on the machine,
/// which includes every seat account.
///
/// So the fix is per-file, not per-directory: leave the folder as it is and strip inheritance from
/// the two files that need it. Applied on every write AND on every read, because an existing
/// install has already-created files that no write path would otherwise revisit.
/// </summary>
internal static class SecureFile
{
    /// <summary>
    /// Replaces the file's DACL with exactly two entries — SYSTEM and Administrators, both
    /// FullControl — and disables inheritance so the ProgramData grant to BUILTIN\Users does not
    /// come back. Idempotent; safe to call on a file that is already restricted.
    /// </summary>
    /// <remarks>
    /// Throws on failure (missing file, no permission to change the DACL) so the caller decides
    /// whether that is fatal. Callers here log and continue: failing to tighten an ACL should not
    /// stop the service from persisting credentials it needs to provision seats.
    /// </remarks>
    public static void RestrictToSystemAndAdmins(string path)
    {
        // Well-known SIDs, not names: "SYSTEM" and "Administrators" are localised, and this has to
        // work on a non-English Windows.
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        var info = new FileInfo(path);
        var acl = info.GetAccessControl();

        // Drop the inherited ACEs first (preserveInheritance: false), then clear whatever explicit
        // ACEs remain, so what is left is only what we add below.
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in
                 acl.GetAccessRules(includeExplicit: true, includeInherited: false,
                                    typeof(SecurityIdentifier)))
        {
            acl.RemoveAccessRuleSpecific(rule);
        }

        acl.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, AccessControlType.Allow));

        info.SetAccessControl(acl);
    }

    /// <summary>
    /// Best-effort variant: applies the restrictive DACL and reports failure through
    /// <paramref name="onError"/> rather than throwing. Returns true when the ACL was applied.
    /// </summary>
    public static bool TryRestrictToSystemAndAdmins(string path, Action<Exception> onError)
    {
        try
        {
            if (!File.Exists(path)) return false;
            RestrictToSystemAndAdmins(path);
            return true;
        }
        catch (Exception ex)
        {
            onError(ex);
            return false;
        }
    }
}
