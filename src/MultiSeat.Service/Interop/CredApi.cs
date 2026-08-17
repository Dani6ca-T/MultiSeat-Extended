using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// Windows Credential Manager (advapi32) — the API behind cmdkey.exe.
///
/// MultiSeat writes one credential per seat launch so mstsc can authenticate to the RDP loopback
/// address without a prompt no one is there to answer. It used to do that by running
/// <c>cmdkey.exe /pass:&lt;password&gt;</c>, which put the seat's password in a process command
/// line; calling the API directly keeps it in memory.
/// </summary>
internal static partial class CredApi
{
    private const string Lib = "advapi32.dll";

    /// <summary>Generic credential — what <c>cmdkey /generic:</c> creates.</summary>
    public const uint CRED_TYPE_GENERIC = 1;

    /// <summary>
    /// Persist for this user on this machine. Matches what <c>cmdkey /generic</c> wrote, and is
    /// kept deliberately: the credential is deleted again as soon as the session exists, and a
    /// narrower lifetime is not worth changing a path that only a live seat launch can exercise.
    /// </summary>
    public const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    // DllImport rather than LibraryImport: the source generator cannot marshal CREDENTIAL
    // (SYSLIB1051 — the FILETIME field needs runtime marshalling), and disabling runtime
    // marshalling assembly-wide to satisfy it would affect every other P/Invoke here.
    [DllImport(Lib, EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [LibraryImport(Lib, EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16,
                   SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredDelete(string targetName, uint type, uint flags);
}
