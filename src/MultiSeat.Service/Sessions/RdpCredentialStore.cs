using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MultiSeat.Service.Interop;
using System.Security.Principal;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Writes and removes the transient RDP credential that lets mstsc connect to the loopback
/// address without prompting.
///
/// Replaces <c>cmdkey.exe /generic:… /user:… /pass:&lt;password&gt;</c>. That worked, but it put a
/// seat's Windows password into a process command line. On current Windows a standard user cannot
/// read another user's command line — measured, not assumed: a non-admin querying Win32_Process
/// saw the process and its owner but an empty CommandLine, while an administrator saw the password
/// in full. So the exposure was to administrators and to anything that records command lines, which
/// is one Group Policy setting (ProcessCreationIncludeCmdLine_Enabled) or any EDR agent away from
/// including this password in logs that leave the machine.
///
/// Calling the credential API directly keeps the password in memory, and removes a process launch
/// from the provisioning path as a bonus.
///
/// The credential has to land in the credential set of the account mstsc runs as, which is the
/// console user — or SYSTEM when nobody is logged in, exactly as before. The service itself runs as
/// SYSTEM, so each call is made while impersonating the console token the caller already holds.
/// </summary>
internal sealed class RdpCredentialStore(ILogger logger)
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Store the seat credential for <paramref name="targetName"/> (e.g. TERMSRV/127.0.0.2).
    /// Returns false if it could not be written — the caller should expect mstsc to prompt.
    /// </summary>
    public bool Write(SafeTokenHandle consoleToken, string targetName, string userName, string password)
    {
        return RunAsConsoleUser(consoleToken, () =>
        {
            var target = IntPtr.Zero;
            var user = IntPtr.Zero;
            var blob = IntPtr.Zero;

            // CredentialBlob is the password as UTF-16 WITHOUT a terminating null counted in the
            // size — that is what cmdkey writes and what mstsc expects to read back.
            var passwordBytes = System.Text.Encoding.Unicode.GetByteCount(password);

            try
            {
                target = Marshal.StringToCoTaskMemUni(targetName);
                user = Marshal.StringToCoTaskMemUni(userName);
                blob = Marshal.StringToCoTaskMemUni(password);

                var cred = new CredApi.CREDENTIAL
                {
                    Type = CredApi.CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlobSize = (uint)passwordBytes,
                    CredentialBlob = blob,
                    Persist = CredApi.CRED_PERSIST_LOCAL_MACHINE,
                    UserName = user,
                };

                if (CredApi.CredWrite(ref cred, 0))
                    return true;

                _logger.LogWarning(
                    "CredWrite failed for {Target} (error {Err}) — mstsc will show a login prompt " +
                    "that no one can click, causing the session to time out. Verify the account " +
                    "password is correct in the MultiSeat dashboard.",
                    targetName, Marshal.GetLastWin32Error());
                return false;
            }
            finally
            {
                // Overwrite the password buffer before releasing it rather than just freeing it.
                if (blob != IntPtr.Zero)
                {
                    for (var i = 0; i < passwordBytes; i++)
                        Marshal.WriteByte(blob, i, 0);
                    Marshal.FreeCoTaskMem(blob);
                }

                if (target != IntPtr.Zero) Marshal.FreeCoTaskMem(target);
                if (user != IntPtr.Zero) Marshal.FreeCoTaskMem(user);
            }
        });
    }

    /// <summary>
    /// Remove the credential again. Best effort — a leftover credential is not fatal, and the next
    /// launch overwrites it.
    /// </summary>
    public void Delete(SafeTokenHandle consoleToken, string targetName)
    {
        RunAsConsoleUser(consoleToken, () =>
        {
            if (!CredApi.CredDelete(targetName, CredApi.CRED_TYPE_GENERIC, 0))
            {
                _logger.LogDebug("CredDelete for {Target} returned error {Err} (non-critical)",
                    targetName, Marshal.GetLastWin32Error());
            }

            return true;
        });
    }

    /// <summary>
    /// Run <paramref name="action"/> impersonating the console token, so the credential is written
    /// to that account's credential set rather than to SYSTEM's.
    /// </summary>
    private bool RunAsConsoleUser(SafeTokenHandle consoleToken, Func<bool> action)
    {
        // The caller's handle is a PRIMARY token it still owns, so duplicate rather than adopt it:
        // SafeAccessTokenHandle closes what it is given, which would leave the caller holding a
        // closed handle for the rest of the launch.
        if (!AdvApi.DuplicateTokenEx(
                consoleToken.DangerousGetHandle(),
                AdvApi.MAXIMUM_ALLOWED,
                IntPtr.Zero,
                AdvApi.SecurityImpersonationLevel.SecurityImpersonation,
                AdvApi.TokenType.TokenImpersonation,
                out var impersonationToken))
        {
            _logger.LogWarning(
                "Could not duplicate the console token for credential storage (error {Err})",
                Marshal.GetLastWin32Error());
            return false;
        }

        using var handle = new SafeAccessTokenHandle(impersonationToken);

        try
        {
            return WindowsIdentity.RunImpersonated(handle, action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impersonation failed while updating the RDP credential.");
            return false;
        }
    }
}
