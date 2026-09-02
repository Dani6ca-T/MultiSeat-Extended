using System.ServiceProcess;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Detects whether a multi-session RDP patch is active on this host.
///
/// The current implementation expects TermWrap (llccd/TermWrap, MIT) — an in-memory
/// Zydis-based patcher that redirects TermService\ServiceDll at
/// <c>%ProgramFiles%\RDP Wrapper\TermWrap.dll</c>. TermWrap self-discovers the offsets it
/// needs by disassembling termsrv.dll on every TermService start, so there is no ini to
/// validate, no community release cadence to wait on, and no Windows-update failure mode.
///
/// The legacy stascorp/rdpwrap install (rdpwrap.dll either in System32 or as
/// TermService\ServiceDll, paired with rdpwrap.ini) is also accepted for migration
/// compatibility — those hosts get a warning until they migrate.
///
/// Without any patch, background sessions will fail to create.
/// </summary>
public sealed class RdpWrapper
{
    private readonly ILogger<RdpWrapper> _logger;

    public RdpWrapper(ILogger<RdpWrapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// How long to let TermService finish starting before judging it stopped. Long enough to
    /// cover a cold boot, short enough not to stall service startup on a host where it really
    /// is disabled.
    /// </summary>
    private static readonly TimeSpan TermServiceStartTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Poll a service until it reports Running, or the timeout expires. Returns false without
    /// waiting the full timeout if the service is Disabled — that will never become Running.
    /// </summary>
    private static bool WaitForRunning(ServiceController sc, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running) return true;
            if (sc.StartType == ServiceStartMode.Disabled) return false;
            if (DateTime.UtcNow >= deadline) return false;

            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Verify that multi-session support is available. Checks:
    ///   1. TermService is running
    ///   2. TermService\ServiceDll points at a non-stock DLL that exists on disk
    ///      (TermWrap.dll on a TermWrap install; rdpwrap.dll on a legacy install)
    /// </summary>
    public bool EnsureMultiSession()
    {
        var build = Environment.OSVersion.Version.Build;
        _logger.LogInformation("Windows build: {Build}", build);

        // Check if TermService is running.
        //
        // Give it a moment first. MultiSeat is an auto-start service, so on a cold boot it can
        // reach this line while TermService is still in StartPending — which used to produce
        // "TermService is not running" and "patch not detected" in the same second, on a host
        // where multi-session was working fine seconds later (issue #15).
        try
        {
            using var sc = new ServiceController("TermService");
            if (!WaitForRunning(sc, TermServiceStartTimeout))
            {
                _logger.LogError(
                    "TermService is {Status} after waiting {Seconds}s — multi-session cannot work",
                    sc.Status, TermServiceStartTimeout.TotalSeconds);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot query TermService");
            return false;
        }

        // TermService can be patched in two ways:
        //   1. TermWrap (current): ServiceDll -> %ProgramFiles%\RDP Wrapper\TermWrap.dll.
        //      TermWrap disassembles termsrv.dll in memory at every TermService start using
        //      Zydis, finds the patch sites by pattern, and patches them. There is no ini to
        //      keep current — a Windows update to termsrv.dll no longer takes multi-session
        //      down.
        //   2. Legacy stascorp/rdpwrap: ServiceDll -> %ProgramFiles%\RDP Wrapper\rdpwrap.dll
        //      (or rdpwrap.dll in System32), paired with rdpwrap.ini carrying byte offsets
        //      keyed by termsrv.dll's file version. Accepted for migration; warn the operator.
        //
        // Do NOT require the literal string "rdpwrap" here — the patcher name is not what
        // matters. What matters is that TermService has been redirected away from the stock
        // termsrv.dll. Test for that and let the name be whatever it is.
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var legacyDllSys32 = Path.Combine(sysDir, "rdpwrap.dll");

        string? wrapperDll = null;
        bool isTermWrap = false;
        bool isLegacyRdpWrap = false;

        if (File.Exists(legacyDllSys32))
        {
            wrapperDll = legacyDllSys32;
            isLegacyRdpWrap = true;
        }
        else
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\TermService\Parameters");
                var svcDll = key?.GetValue("ServiceDll") as string;
                if (!string.IsNullOrEmpty(svcDll))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(svcDll);
                    var fileName = Path.GetFileName(expanded);
                    var isStockTermsrv = string.Equals(
                        fileName, "termsrv.dll", StringComparison.OrdinalIgnoreCase);

                    if (!isStockTermsrv && File.Exists(expanded))
                    {
                        wrapperDll = expanded;
                        isTermWrap = string.Equals(
                            fileName, "TermWrap.dll", StringComparison.OrdinalIgnoreCase);
                        isLegacyRdpWrap = !isTermWrap && fileName.EndsWith(
                            "rdpwrap.dll", StringComparison.OrdinalIgnoreCase);
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
                "No multi-session patch found — no rdpwrap.dll in System32, and TermService's " +
                "ServiceDll still points at the stock termsrv.dll. Run " +
                "prerequisites\\install-prerequisites.ps1 to install TermWrap.");
            return false;
        }

        _logger.LogInformation("Multi-session patch detected at {Path}", wrapperDll);

        if (isLegacyRdpWrap)
        {
            _logger.LogWarning(
                "Legacy stascorp/rdpwrap is active. Migrate to TermWrap with " +
                "prerequisites\\install-prerequisites.ps1 — TermWrap is in-memory and does " +
                "not need rdpwrap.ini updated when Windows updates termsrv.dll.");
        }

        return true;
    }
}
