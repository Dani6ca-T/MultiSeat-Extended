using Microsoft.Win32.SafeHandles;

namespace MultiSeat.Service.Interop;

/// <summary>
/// RAII wrapper for Windows token handles (HANDLE from LogonUser,
/// DuplicateTokenEx, WTSQueryUserToken, etc.).
/// Ensures the handle is closed even if an exception occurs.
/// </summary>
public sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeTokenHandle() : base(ownsHandle: true) { }

    public SafeTokenHandle(IntPtr handle) : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return Kernel32.CloseHandle(handle);
    }

    /// <summary>
    /// Implicit conversion to IntPtr for P/Invoke calls that expect raw HANDLE.
    /// </summary>
    public static implicit operator IntPtr(SafeTokenHandle h) => h.DangerousGetHandle();
}
