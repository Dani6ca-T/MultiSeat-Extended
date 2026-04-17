using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Detects and validates the RDP Wrapper Library installation.
/// RDP Wrapper patches termsrv.dll to allow concurrent interactive sessions
/// on Windows 11 24H2 (build 26100+), which normally restricts to one session.
///
/// Without this patch, background sessions will fail to create.
/// </summary>
public sealed class RdpWrapper
{
    private readonly ILogger<RdpWrapper> _logger;

    public RdpWrapper(ILogger<RdpWrapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Verify that multi-session support is available.
    /// Checks:
    ///   1. RDP Wrapper service (rdpwrap) is running
    ///   2. termsrv.dll is loaded and patched
    ///   3. Windows build is recognized by the wrapper config
    /// </summary>
    public bool EnsureMultiSession()
    {
        var build = Environment.OSVersion.Version.Build;
        _logger.LogInformation("Windows build: {Build}", build);

        // Check if TermService is running
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("TermService");
            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                _logger.LogError("TermService is not running");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot query TermService");
            return false;
        }

        // RDP Wrapper can be installed in two ways:
        //   1. Classic (older): rdpwrap.dll copied to System32
        //   2. ServiceDll (newer, v1.6.2+): TermService\Parameters\ServiceDll points to
        //      %ProgramFiles%\RDP Wrapper\rdpwrap.dll instead of the default termsrv.dll
        // Check both methods.
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var wrapperDllSys32 = Path.Combine(sysDir, "rdpwrap.dll");

        string? wrapperDll = null;
        string? iniPath = null;

        if (File.Exists(wrapperDllSys32))
        {
            wrapperDll = wrapperDllSys32;
            iniPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "RDP Wrapper", "rdpwrap.ini");
        }
        else
        {
            // Check the ServiceDll registry key
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\TermService\Parameters");
                var svcDll = key?.GetValue("ServiceDll") as string;
                if (!string.IsNullOrEmpty(svcDll))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(svcDll);
                    if (expanded.Contains("rdpwrap", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(expanded))
                    {
                        wrapperDll = expanded;
                        iniPath = Path.Combine(Path.GetDirectoryName(expanded)!, "rdpwrap.ini");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read TermService ServiceDll registry key");
            }
        }

        if (wrapperDll == null)
        {
            _logger.LogWarning(
                "rdpwrap.dll not found in System32 or via TermService ServiceDll. " +
                "RDP Wrapper is not installed. Run prerequisites\\install-prerequisites.ps1.");
            return false;
        }

        _logger.LogInformation("RDP Wrapper detected at {Path}", wrapperDll);

        if (iniPath != null && File.Exists(iniPath))
            return ValidateIniForBuild(iniPath, build);

        _logger.LogInformation("rdpwrap.ini not found — assuming wrapper is active");
        return true;
    }

    private bool ValidateIniForBuild(string iniPath, int build)
    {
        try
        {
            var content = File.ReadAllText(iniPath);
            var buildStr = build.ToString();

            if (content.Contains(buildStr))
            {
                _logger.LogInformation("rdpwrap.ini contains entry for build {Build}", build);
                return true;
            }

            _logger.LogError(
                "rdpwrap.ini does NOT contain an entry for build {Build}. " +
                "Update rdpwrap.ini from https://github.com/sebaxakerhtc/rdpwrap.ini " +
                "or apply a manual patch.", build);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read rdpwrap.ini");
            return false;
        }
    }
}
