using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke declarations for wtsapi32.dll — Windows Terminal Services API.
/// Used for session enumeration, query, token acquisition, and disconnect.
/// Compatible with Windows 11 24H2 (build 26100+).
/// </summary>
internal static partial class WtsApi
{
    private const string Lib = "wtsapi32.dll";

    public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    public enum WtsConnectState
    {
        Active,
        Connected,
        ConnectQuery,
        Shadow,
        Disconnected,
        Idle,
        Listen,
        Reset,
        Down,
        Init
    }

    public enum WtsInfoClass
    {
        WTSInitialProgram = 0,
        WTSApplicationName = 1,
        WTSWorkingDirectory = 2,
        WTSOEMId = 3,
        WTSSessionId = 4,
        WTSUserName = 5,
        WTSWinStationName = 6,
        WTSDomainName = 7,
        WTSConnectState = 8,
        WTSClientBuildNumber = 9,
        WTSClientName = 10,
        WTSClientDirectory = 11,
        WTSClientProductId = 12,
        WTSClientHardwareId = 13,
        WTSClientAddress = 14,
        WTSClientDisplay = 15,
        WTSClientProtocolType = 16,
        WTSIdleTime = 17,
        WTSLogonTime = 18,
        WTSIncomingBytes = 19,
        WTSOutgoingBytes = 20,
        WTSIncomingFrames = 21,
        WTSOutgoingFrames = 22,
        WTSClientInfo = 23,
        WTSSessionInfo = 24,
        WTSSessionInfoEx = 25,
        WTSConfigInfo = 26,
        WTSIsRemoteSession = 29
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public WtsConnectState State;
    }

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSEnumerateSessionsW(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSQuerySessionInformationW(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [LibraryImport(Lib)]
    public static partial void WTSFreeMemory(IntPtr pMemory);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSLogoffSession(
        IntPtr hServer,
        int sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSDisconnectSession(
        IntPtr hServer,
        int sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    /// <summary>
    /// Obtains the primary access token of the logged-on user for the
    /// specified session. Requires the caller to run as LocalSystem.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSQueryUserToken(
        uint sessionId,
        out IntPtr phToken);
}
