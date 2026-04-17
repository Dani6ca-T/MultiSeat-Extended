using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke bindings for MultiSeatInputHook.dll — the native C++ DLL that provides
/// session-aware low-level keyboard and mouse hook filtering.
/// </summary>
public static partial class MultiSeatInputHookNative
{
    private const string DllName = "MultiSeatInputHook.dll";

    /// <summary>
    /// Install low-level keyboard and mouse hooks that filter by session ID.
    /// Only events from the specified session are passed through; all others are blocked.
    /// </summary>
    /// <param name="targetSessionId">Windows Session ID to allow input from. 0xFFFFFFFF = no filtering.</param>
    /// <returns>0 on success, Win32 error code on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern int MultiSeatHook_Install(uint targetSessionId);

    /// <summary>Remove installed hooks and clean up.</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void MultiSeatHook_Uninstall();

    /// <summary>Check if hooks are currently installed.</summary>
    /// <returns>Non-zero if hooks are active.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MultiSeatHook_IsInstalled();

    /// <summary>Get the session ID the hooks are filtering for.</summary>
    /// <returns>Target session ID, or 0xFFFFFFFF if not filtering.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint MultiSeatHook_GetTargetSession();

    /// <summary>
    /// Register a RawInput filter for the specified window.
    /// Uses RIDEV_INPUTSINK to receive all HID input, then filters by session.
    /// </summary>
    /// <param name="hwnd">Window handle to receive WM_INPUT messages.</param>
    /// <param name="targetSessionId">Session to filter for.</param>
    /// <returns>0 on success, Win32 error code on failure.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern int MultiSeatRawInput_Register(IntPtr hwnd, uint targetSessionId);

    /// <summary>Unregister the RawInput filter.</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void MultiSeatRawInput_Unregister();
}
