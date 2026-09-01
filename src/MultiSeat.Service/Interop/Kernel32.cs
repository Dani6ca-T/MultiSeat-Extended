using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for kernel32.dll — process creation, handle management, events.
/// </summary>
internal static partial class Kernel32
{
    private const string Lib = "kernel32.dll";

    // ── Creation flags ───────────────────────────────────────────────
    public const uint CREATE_NEW_CONSOLE = 0x00000010;
    public const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const uint DETACHED_PROCESS = 0x00000008;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;

    // ── STARTF flags ─────────────────────────────────────────────────
    public const int STARTF_USESHOWWINDOW = 0x00000001;
    public const int STARTF_USESIZE       = 0x00000002;
    public const int STARTF_USEPOSITION   = 0x00000004;

    // ── SW_* ─────────────────────────────────────────────────────────
    public const short SW_HIDE = 0;
    public const short SW_SHOWNORMAL = 1;
    public const short SW_SHOW = 5;
    public const short SW_SHOWMINNOACTIVE = 7;  // minimized, does not steal focus

    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 258;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport(Lib, SetLastError = true)]
    public static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [LibraryImport(Lib)]
    public static partial uint GetCurrentProcessId();

    [LibraryImport(Lib)]
    public static partial IntPtr GetCurrentProcess();

    [LibraryImport(Lib, SetLastError = true)]
    public static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    // PROCESS_QUERY_INFORMATION
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    /// <summary>
    /// Create a named event for session anchor signaling.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(IntPtr hEvent);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    public const uint STILL_ACTIVE = 259;

    public const short SW_MINIMIZE = 6;

    [LibraryImport(Lib)]
    public static partial uint WTSGetActiveConsoleSessionId();

    /// <summary>
    /// Create a process in the current session (Session 0 when running as a service).
    /// Used for launching Apollo with GPU access since RDP sessions cannot access DXGI.
    /// </summary>
    [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    // ── Job Objects ──────────────────────────────────────────────────
    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    // JOBOBJECTINFOCLASS — only ExtendedLimitInformation is used.
    public const int JobObjectInfoClassExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public IntPtr MinimumWorkingSetSize;
        public IntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr AffinityPriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public IntPtr ProcessMemoryLimit;
        public IntPtr JobMemoryLimit;
        public IntPtr PeakProcessMemoryUsed;
        public IntPtr PeakJobMemoryUsed;
    }

    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}
