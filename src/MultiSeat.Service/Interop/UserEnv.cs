using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for userenv.dll — user profile and environment block management.
/// Required for CreateProcessAsUser to have a valid environment.
/// </summary>
internal static partial class UserEnv
{
    private const string Lib = "userenv.dll";

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateEnvironmentBlock(
        out IntPtr lpEnvironment,
        IntPtr hToken,
        [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetUserProfileDirectoryW(
        IntPtr hToken,
        [Out] char[] lpProfileDir,
        ref int lpcchSize);

    // Creates the user profile directory for a given SID without requiring an interactive logon.
    // Returns S_OK (0) on success or HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS) if already created.
    [DllImport(Lib, CharSet = CharSet.Unicode)]
    public static extern int CreateProfile(
        string pszUserSid,
        string pszUserName,
        System.Text.StringBuilder pszProfilePath,
        uint cchProfilePath);
}
