using Microsoft.Win32.SafeHandles;

namespace MultiSeat.Service.Interop;

/// <summary>
/// RAII wrapper for Windows Job Object handles (HANDLE from CreateJobObjectW).
/// Ensures the handle is closed even if an exception occurs.
/// When the handle is closed, all processes assigned to the job are terminated
/// if JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE was set.
/// </summary>
public sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeJobHandle() : base(ownsHandle: true) { }

    public SafeJobHandle(IntPtr handle) : base(ownsHandle: true)
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
    public static implicit operator IntPtr(SafeJobHandle h) => h.DangerousGetHandle();
}
