using System.Runtime.InteropServices;
using MultiSeat.Service.Display;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Api;

public static class SystemEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        group.MapGet("/health", (SeatManager seats, GpuMonitor gpu, RdpWrapper rdp) =>
        {
            GetPhysicalMemory(out var totalMb, out var availMb);

            var status = new SystemStatus
            {
                ActiveSeats = seats.ActiveSeatCount,
                Gpu = gpu.Query(),
                WindowsBuild = Environment.OSVersion.VersionString,
                RdpWrapperActive = rdp.EnsureMultiSession(),
                SystemMemoryMb = totalMb,
                AvailableMemoryMb = availMb
            };
            return Results.Ok(status);
        });

        // Diagnostic endpoint — dumps all connected display paths from QueryDisplayConfig.
        // Use this to verify SudoVDA virtual displays are visible and check their names.
        // GET /api/system/displays
        group.MapGet("/displays", (VirtualDisplayManager displays) =>
        {
            var allPaths = displays.EnumerateAllConnectedPaths();
            return Results.Ok(new
            {
                totalConnected = allPaths.Count,
                sudoVdaFound = displays.IsDriverAvailable,
                paths = allPaths
            });
        });
    }

    /// <summary>
    /// Query physical memory via GlobalMemoryStatusEx (accurate, unlike GC metrics).
    /// </summary>
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
            // Fallback to GC info (less accurate but always available)
            var gcInfo = GC.GetGCMemoryInfo();
            totalMb = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
            availMb = totalMb; // can't determine available without native call
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
