using System.Security.Principal;
using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for advapi32.dll — LogonUser, CreateProcessAsUser, token operations.
/// Uses DllImport for methods taking structs with string fields.
/// </summary>
internal static partial class AdvApi
{
    private const string Lib = "advapi32.dll";

    // ── Logon types ──────────────────────────────────────────────────
    public const int LOGON32_LOGON_INTERACTIVE = 2;
    public const int LOGON32_LOGON_NETWORK = 3;
    public const int LOGON32_LOGON_BATCH = 4;
    public const int LOGON32_LOGON_SERVICE = 5;
    public const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
    public const int LOGON32_PROVIDER_DEFAULT = 0;

    // ── Token access rights ──────────────────────────────────────────
    public const uint TOKEN_ALL_ACCESS = 0x000F01FF;
    public const uint TOKEN_DUPLICATE = 0x0002;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    public const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    public const uint MAXIMUM_ALLOWED = 0x02000000;

    // ── TOKEN_INFORMATION_CLASS ──────────────────────────────────────
    public enum TokenInformationClass
    {
        TokenUser = 1,
        TokenGroups = 2,
        TokenPrivileges = 3,
        TokenOwner = 4,
        TokenPrimaryGroup = 5,
        TokenDefaultDacl = 6,
        TokenSource = 7,
        TokenType = 8,
        TokenImpersonationLevel = 9,
        TokenStatistics = 10,
        TokenRestrictedSids = 11,
        TokenSessionId = 12,          // <-- used to set session ID on token
        TokenGroupsAndPrivileges = 13,
        TokenSessionReference = 14,
        TokenSandBoxInert = 15,
        TokenLinkedToken = 19,
        TokenElevation = 20,
        TokenUIAccess = 26,
    }

    // ── Security impersonation level ─────────────────────────────────
    public enum SecurityImpersonationLevel
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    public enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    // ── Privilege constants ──────────────────────────────────────────
    public const string SE_TCB_NAME = "SeTcbPrivilege";
    public const string SE_ASSIGNPRIMARYTOKEN_NAME = "SeAssignPrimaryTokenPrivilege";
    public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges; // single element; for >1, use array marshal
    }

    // ── Functions ────────────────────────────────────────────────────

    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LogonUserW(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SecurityImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr phNewToken);

    /// <summary>
    /// Set information on a token — used to stamp a target session ID
    /// onto a duplicated primary token before CreateProcessAsUser.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        ref int tokenInformation,
        int tokenInformationLength);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValueW(
        string? lpSystemName,
        string lpName,
        out LUID lpLuid);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    /// <summary>
    /// GetTokenInformation for pointer-sized values (e.g. TokenLinkedToken).
    /// TOKEN_LINKED_TOKEN is a struct containing a single HANDLE field.
    /// </summary>
    public static bool GetTokenInformationHandle(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out IntPtr tokenInfo)
    {
        tokenInfo = IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (!GetTokenInformationRaw(tokenHandle, tokenInformationClass,
                    buffer, IntPtr.Size, out _))
                return false;
            tokenInfo = Marshal.ReadIntPtr(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport(Lib, EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformationRaw(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    /// <summary>
    /// The SID of the user a token belongs to, or null if it cannot be read.
    ///
    /// Exists because <c>WTSQueryUserToken(sessionId)</c> hands back whoever occupies that session,
    /// with no reference to the account we meant. If the session id is wrong, the caller silently
    /// gets a stranger's token — one whose session id matches what was asked for, so every
    /// downstream check passes. See <c>SessionLauncher.GetSessionToken</c>.
    ///
    /// TOKEN_USER is variable length (it ends in a SID), so this is the two-call form: ask for the
    /// size, then read it.
    /// </summary>
    public static SecurityIdentifier? TryGetTokenUserSid(IntPtr tokenHandle)
    {
        GetTokenInformationRaw(tokenHandle, TokenInformationClass.TokenUser,
            IntPtr.Zero, 0, out var needed);

        if (needed <= 0) return null;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformationRaw(tokenHandle, TokenInformationClass.TokenUser,
                    buffer, needed, out _))
                return null;

            // TOKEN_USER is a SID_AND_ATTRIBUTES: the first pointer-sized field points at the SID.
            var sidPtr = Marshal.ReadIntPtr(buffer);
            return sidPtr == IntPtr.Zero ? null : new SecurityIdentifier(sidPtr);
        }
        catch (ArgumentException)
        {
            // Malformed SID — treat as unreadable rather than throwing out of a check.
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    /// <summary>
    /// Impersonate a logged-on user. The calling thread then runs in the user's
    /// security context — required for GDI calls like ChangeDisplaySettingsEx
    /// that are scoped to the calling session's window station.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImpersonateLoggedOnUser(IntPtr hToken);

    /// <summary>
    /// Revert the calling thread back to its own security context
    /// after a call to ImpersonateLoggedOnUser.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RevertToSelf();

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessAsUserW(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref Kernel32.StartupInfo lpStartupInfo,
        out Kernel32.ProcessInformation lpProcessInformation);
}
