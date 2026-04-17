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
}
