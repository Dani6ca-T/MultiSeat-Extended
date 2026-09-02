using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiSeat.Service.Interop;
using MultiSeat.Shared;

namespace MultiSeat.Service.ProcessTracking;

/// <summary>
/// Windows implementation of <see cref="IProcessGroup"/> using a Job Object
/// with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
///
/// When this object is disposed, Windows terminates all successfully assigned processes.
/// AssignProcess is best-effort: if the target process is already in another Job Object,
/// assignment fails silently (ERROR_ACCESS_DENIED) and cleanup relies on the explicit
/// termination path (e.g. Process.Kill in the provider manager).
///
/// Thread-safety: AssignProcess is safe to call from multiple threads concurrently.
/// Dispose is safe to call from any thread, and is idempotent.
/// </summary>
public sealed class WindowsProcessGroup : IProcessGroup
{
    private readonly ILogger<WindowsProcessGroup> _logger;
    private readonly SafeJobHandle _jobHandle;
    private bool _disposed;

    /// <summary>
    /// Create a new Job Object with KILL_ON_JOB_CLOSE configured.
    /// </summary>
    /// <exception cref="Win32Exception">
    /// Thrown when CreateJobObjectW or SetInformationJobObject fails.
    /// </exception>
    public WindowsProcessGroup(ILogger<WindowsProcessGroup> logger)
    {
        _logger = logger;
        // Create the Job Object
        var handle = Kernel32.CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "CreateJobObjectW failed — cannot create process group");
        }

        _jobHandle = new SafeJobHandle(handle);

        // Configure KILL_ON_JOB_CLOSE
        ConfigureKillOnClose();
    }

    /// <summary>
    /// Assign a process to this job by its PID.
    /// The process will be terminated when the job handle is closed (disposed).
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the group has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when AssignProcessToJobObject fails (process already in another job, process has exited, etc.).
    /// </exception>
    public void AssignProcess(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId,
                "Process ID must be positive.");

        // Open the process with JOB_OBJECT_ASSIGN_PROCESS and PROCESS_TERMINATE access
        const uint access = 0x0010 /* JOB_OBJECT_ASSIGN_PROCESS */ | 0x0001 /* PROCESS_TERMINATE */;
        var processHandle = Kernel32.OpenProcess(access, false, (uint)processId);
        if (processHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            // ERROR_INVALID_PARAMETER (87) often means the process has already exited
            if (error == 87)
                return; // Process already gone — no-op, cleanup not needed

            throw new Win32Exception(error,
                $"OpenProcess(pid={processId}) failed — cannot assign to job object");
        }

        try
        {
            if (!Kernel32.AssignProcessToJobObject(_jobHandle, processHandle))
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_ACCESS_DENIED (5) can mean the process is already in a job
                // with nested jobs disabled — process remains outside this Job Object,
                // cleanup will rely on the explicit termination path (Process.Kill).
                if (error == 5)
                {
                    _logger.LogWarning(
                        "AssignProcessToJobObject(pid={Pid}) returned ERROR_ACCESS_DENIED — " +
                        "process is already in another Job Object and remains outside this group. " +
                        "Cleanup will rely on the explicit termination path (Process.Kill).",
                        processId);
                    return;
                }

                throw new Win32Exception(error,
                    $"AssignProcessToJobObject(pid={processId}) failed");
            }
        }
        finally
        {
            Kernel32.CloseHandle(processHandle);
        }
    }

    private void ConfigureKillOnClose()
    {
        var info = new Kernel32.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new Kernel32.JobObjectBasicLimitInformation
            {
                LimitFlags = Kernel32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        if (!Kernel32.SetInformationJobObject(
                _jobHandle,
                Kernel32.JobObjectInfoClassExtendedLimitInformation,
                ref info,
                (uint)Marshal.SizeOf<Kernel32.JobObjectExtendedLimitInformation>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SetInformationJobObject failed — cannot configure KILL_ON_JOB_CLOSE");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _jobHandle.Dispose();
    }
}
