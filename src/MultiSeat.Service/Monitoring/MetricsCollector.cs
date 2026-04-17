using System.Runtime.InteropServices;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Monitoring;

/// <summary>
/// Aggregates system-wide metrics for the dashboard.
/// Combines GPU stats, seat states, and host resource usage.
/// </summary>
public sealed class MetricsCollector
{
    private readonly GpuMonitor _gpu;
    private readonly RdpWrapper _rdpWrapper;

    public MetricsCollector(GpuMonitor gpu, RdpWrapper rdpWrapper)
    {
        _gpu = gpu;
        _rdpWrapper = rdpWrapper;
    }

    public SystemStatus Collect(SeatManager seatManager)
    {
        GetPhysicalMemory(out var totalMb, out var availMb);

        return new SystemStatus
        {
            ActiveSeats = seatManager.ActiveSeatCount,
            Gpu = _gpu.Query(),
            WindowsBuild = Environment.OSVersion.VersionString,
            RdpWrapperActive = _rdpWrapper.EnsureMultiSession(),
            SystemMemoryMb = totalMb,
            AvailableMemoryMb = availMb
        };
    }

    private static void GetPhysicalMemory(out long totalMb, out long availMb)
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            totalMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
            availMb = (long)(memStatus.ullAvailPhys / (1024 * 1024));
        }
        else
        {
            totalMb = 0;
            availMb = 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
