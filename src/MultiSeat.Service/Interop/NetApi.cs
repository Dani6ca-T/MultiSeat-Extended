using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for netapi32.dll — local user account management.
/// NetUserAdd/NetUserDel/NetUserEnum for programmatic account CRUD.
/// Uses DllImport for methods taking structs with string fields
/// (not supported by LibraryImport source generator).
/// </summary>
internal static partial class NetApi
{
    private const string Lib = "netapi32.dll";

    // Error codes
    public const int NERR_Success = 0;
    public const int NERR_UserExists = 2224;
    public const int NERR_UserNotFound = 2221;

    // User info levels
    public const int USER_PRIV_USER = 1;
    public const int UF_SCRIPT = 0x0001;
    public const int UF_NORMAL_ACCOUNT = 0x0200;
    public const int UF_DONT_EXPIRE_PASSWD = 0x10000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct UserInfo1
    {
        public string usri1_name;
        public string usri1_password;
        public int usri1_password_age;
        public int usri1_priv;
        public string? usri1_home_dir;
        public string? usri1_comment;
        public int usri1_flags;
        public string? usri1_script_path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LocalGroupMembersInfo3
    {
        public string lgrmi3_domainandname;
    }

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetUserAdd(
        string? servername,
        int level,
        ref UserInfo1 buf,
        out int parm_err);

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetUserDel(
        string? servername,
        string username);

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetUserEnum(
        string? servername,
        int level,
        int filter,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        ref IntPtr resume_handle);

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetLocalGroupAddMembers(
        string? servername,
        string groupname,
        int level,
        ref LocalGroupMembersInfo3 buf,
        int totalentries);

    [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetUserGetInfo(
        string? servername,
        string username,
        int level,
        out IntPtr bufptr);

    [LibraryImport(Lib)]
    public static partial int NetApiBufferFree(IntPtr buffer);
}
