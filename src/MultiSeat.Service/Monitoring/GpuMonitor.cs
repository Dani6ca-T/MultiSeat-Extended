using System.Management;
using System.Runtime.InteropServices;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Monitoring;

/// <summary>
/// Queries GPU status using NVML (NVIDIA), with WMI fallback for non-NVIDIA GPUs.
/// Reports VRAM usage, encoder utilization, and active encoder sessions.
/// </summary>
public sealed class GpuMonitor
{
    private readonly ILogger<GpuMonitor> _logger;
    private bool _nvmlAvailable;
    private bool _initialized;

    public GpuMonitor(ILogger<GpuMonitor> logger)
    {
        _logger = logger;
    }

    public GpuInfo? Query()
    {
        // Try NVML first (NVIDIA GPUs — most common for game streaming)
        var info = TryQueryNvml();
        if (info is not null)
            return info;

        // Fallback to WMI for AMD or other GPUs
        return TryQueryWmi();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  NVML — NVIDIA Management Library
    // ═══════════════════════════════════════════════════════════════════

    private GpuInfo? TryQueryNvml()
    {
        if (!EnsureNvmlInitialized())
            return null;

        try
        {
            if (Nvml.nvmlDeviceGetHandleByIndex(0, out var device) != 0)
                return null;

            // Device name
            var nameBuf = new byte[64];
            string name = "NVIDIA GPU";
            if (Nvml.nvmlDeviceGetName(device, nameBuf, (uint)nameBuf.Length) == 0)
                name = System.Text.Encoding.UTF8.GetString(nameBuf).TrimEnd('\0');

            // Utilization
            int utilization = 0;
            if (Nvml.nvmlDeviceGetUtilizationRates(device, out var util) == 0)
                utilization = (int)util.gpu;

            // VRAM
            long vramTotal = 0, vramUsed = 0;
            if (Nvml.nvmlDeviceGetMemoryInfo(device, out var mem) == 0)
            {
                vramTotal = (long)(mem.total / (1024 * 1024));
                vramUsed = (long)(mem.used / (1024 * 1024));
            }

            // Encoder utilization
            int encoderUtil = 0;
            if (Nvml.nvmlDeviceGetEncoderUtilization(device, out var encUtil, out _) == 0)
                encoderUtil = (int)encUtil;

            // Active encoder sessions
            int encoderSessions = 0;
            if (Nvml.nvmlDeviceGetEncoderSessions(device, out var sessionCount, IntPtr.Zero) == 0)
                encoderSessions = (int)sessionCount;

            return new GpuInfo
            {
                Name = name,
                UtilizationPercent = utilization,
                VramTotalMb = vramTotal,
                VramUsedMb = vramUsed,
                EncoderUtilizationPercent = encoderUtil,
                ActiveEncoderSessions = encoderSessions
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML query failed");
            return null;
        }
    }

    private bool EnsureNvmlInitialized()
    {
        if (_initialized) return _nvmlAvailable;
        _initialized = true;

        try
        {
            var result = Nvml.nvmlInit_v2();
            _nvmlAvailable = result == 0;
            if (_nvmlAvailable)
                _logger.LogInformation("NVML initialized — NVIDIA GPU monitoring active");
            else
                _logger.LogDebug("NVML init returned {Result} — not available", result);
        }
        catch (DllNotFoundException)
        {
            _logger.LogDebug("nvml.dll not found — no NVIDIA GPU or drivers not installed");
            _nvmlAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NVML init failed");
            _nvmlAvailable = false;
        }

        return _nvmlAvailable;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  WMI Fallback — works for AMD, Intel, and NVIDIA
    // ═══════════════════════════════════════════════════════════════════

    private GpuInfo? TryQueryWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, CurrentBitsPerPixel FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRam = Convert.ToInt64(obj["AdapterRAM"] ?? 0);

                // WMI AdapterRAM can overflow at 4GB for 32-bit field.
                // For cards >4GB, we try DXGI or report the WMI value.
                var vramMb = adapterRam > 0 ? adapterRam / (1024 * 1024) : 0;

                // WMI doesn't provide utilization — try performance counters
                var utilization = TryGetGpuUtilizationFromPerfCounter();

                return new GpuInfo
                {
                    Name = name,
                    UtilizationPercent = utilization,
                    VramTotalMb = vramMb,
                    VramUsedMb = 0, // WMI doesn't expose current VRAM usage
                    EncoderUtilizationPercent = 0,
                    ActiveEncoderSessions = 0
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI GPU query failed");
        }

        return null;
    }

    private int TryGetGpuUtilizationFromPerfCounter()
    {
        try
        {
            // Windows 10+ exposes GPU Engine performance counters
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            double maxUtil = 0;
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                // Filter to 3D/Compute engines (skip Copy, Video Decode, etc.)
                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    var util = Convert.ToDouble(obj["UtilizationPercentage"] ?? 0);
                    if (util > maxUtil) maxUtil = util;
                }
            }
            return (int)maxUtil;
        }
        catch
        {
            return 0;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  NVML P/Invoke
    // ═══════════════════════════════════════════════════════════════════

    private static class Nvml
    {
        private const string Lib = "nvml.dll";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlInit_v2();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlShutdown();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetEncoderUtilization(IntPtr device, out uint utilization, out uint samplingPeriodUs);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nvmlDeviceGetEncoderSessions(IntPtr device, out uint sessionCount, IntPtr sessionInfos);

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlMemory
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }
    }
}
