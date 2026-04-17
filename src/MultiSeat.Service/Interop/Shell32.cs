using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for shell32.dll — shell folder path queries.
/// </summary>
internal static partial class Shell32
{
    /// <summary>
    /// Retrieve the full path of a known folder identified by the folder's KNOWNFOLDERID.
    /// Pass an access token to get the folder for a specific user (e.g., the console user
    /// from a SYSTEM service). The caller must free ppszPath via Marshal.FreeCoTaskMem.
    /// Returns S_OK (0) on success; negative HRESULT on failure.
    /// </summary>
    [LibraryImport("shell32.dll")]
    internal static partial int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    // KNOWNFOLDERID constants
    // {FDD39AD0-238F-46AF-ADB4-6C85480369C7} — user's Documents folder
    public static readonly Guid FOLDERID_Documents =
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
}
