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
    private static bool WaitForRunning(System.ServiceProcess.ServiceController sc, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            sc.Refresh();
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) return true;
            if (sc.StartType == System.ServiceProcess.ServiceStartMode.Disabled) return false;
            if (DateTime.UtcNow >= deadline) return false;

            Thread.Sleep(500);
        }
    }

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
            using var sc = new System.ServiceProcess.ServiceController("TermService");
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

            // Set only when present — an absent ini means "nothing to validate", not "invalid".
            var classicIni = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "RDP Wrapper", "rdpwrap.ini");
            if (File.Exists(classicIni)) iniPath = classicIni;
        }
        else
        {
            // Check the ServiceDll registry key.
            //
            // Do NOT require the literal string "rdpwrap" here. RDP Wrapper is not the only
            // patch that works this way — TermWrap (llccd/TermWrap) points ServiceDll at its
            // own DLL too, and issue #15's reporter had this reporting "patch not detected"
            // while three sessions were demonstrably running under it. What actually matters
            // is that TermService has been redirected away from the stock termsrv.dll, so
            // test for that instead and let the name be whatever it is.
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\TermService\Parameters");
                var svcDll = key?.GetValue("ServiceDll") as string;
                if (!string.IsNullOrEmpty(svcDll))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(svcDll);
                    var isStockTermsrv = string.Equals(
                        Path.GetFileName(expanded), "termsrv.dll", StringComparison.OrdinalIgnoreCase);

                    if (!isStockTermsrv && File.Exists(expanded))
                    {
                        wrapperDll = expanded;

                        // Only RDP Wrapper is keyed by an offsets ini. If a sibling rdpwrap.ini
                        // is absent the patch is something else (e.g. TermWrap, which finds its
                        // offsets by disassembling termsrv), and there is nothing to validate.
                        var sibling = Path.Combine(Path.GetDirectoryName(expanded)!, "rdpwrap.ini");
                        if (File.Exists(sibling)) iniPath = sibling;
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
                "prerequisites\\install-prerequisites.ps1 to install RDP Wrapper.");
            return false;
        }

        _logger.LogInformation("Multi-session patch detected at {Path}", wrapperDll);

        if (iniPath != null)
            return ValidateIniForTermsrv(iniPath);

        _logger.LogInformation(
            "No rdpwrap.ini alongside {Path} — patch is not ini-keyed (e.g. TermWrap), " +
            "nothing to validate", wrapperDll);
        return true;
    }

    /// <summary>
    /// The version of <c>termsrv.dll</c> that rdpwrap.ini is keyed by — e.g. "10.0.26100.8737".
    ///
    /// Read the build/revision from the numeric VS_FIXEDFILEINFO fields, NOT from the
    /// FileVersion string. Windows servicing updates the binary version resource but can
    /// leave the localized string block stale, so the two disagree on a patched machine:
    /// this host's string reads "10.0.26100.8115" while the numeric fields (and the file's
    /// actual identity, confirmed by hashing it against the WinSxS copies) are
    /// 10.0.26100.8737. Trusting the string means looking up a termsrv.dll that is not the
    /// one installed.
    ///
    /// The major/minor come from <see cref="Environment.OSVersion"/> instead, because this
    /// process has no application manifest: Windows then applies its compatibility shim and
    /// reports the major/minor of OS files as 6.2 (Windows 8). Observed in production — the
    /// service resolved "6.2.26100.8737" for a file PowerShell reads as "10.0.26100.8737",
    /// and no such ini section exists, so a correctly-patched host was reported broken. The
    /// shim leaves build and revision untouched, and .NET's OSVersion is honest (it calls
    /// RtlGetVersion), so composing the two gives the true version.
    ///
    /// Note OSVersion's own build (26200 here) is the OS build and is NOT interchangeable
    /// with termsrv.dll's build (26100) — only major/minor are taken from it.
    ///
    /// The deeper fix is to ship an app manifest declaring supportedOS, which would remove
    /// the shim; that is a broader change than this check warrants.
    ///
    /// Returns null if the file or its version resource cannot be read.
    /// </summary>
    private string? ResolveTermsrvVersion()
    {
        try
        {
            var termsrv = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "termsrv.dll");
            if (!File.Exists(termsrv)) return null;

            var vi = FileVersionInfo.GetVersionInfo(termsrv);
            var os = Environment.OSVersion.Version;
            return $"{os.Major}.{os.Minor}.{vi.FileBuildPart}.{vi.FilePrivatePart}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read termsrv.dll version");
            return null;
        }
    }

    /// <summary>
    /// Check that rdpwrap.ini carries offsets for the termsrv.dll actually installed.
    ///
    /// The ini is keyed by termsrv.dll's file version (<c>[10.0.26100.8737]</c>), which is a
    /// different identifier from the Windows build number (26200) — different numbering
    /// scheme, different value. This used to substring-search the ini for the OS build, which
    /// was wrong in both directions: it passed on any ini that happened to contain those
    /// digits anywhere, and it would fail a correctly-patched host whose ini simply had no
    /// section from that servicing branch. It only appeared to work here because the current
    /// upstream ini happens to carry an unrelated [10.0.26200.5001] section.
    /// </summary>
    private bool ValidateIniForTermsrv(string iniPath)
    {
        var version = ResolveTermsrvVersion();
        if (version is null)
        {
            // Can't identify the DLL, so we can't judge the ini. Don't claim breakage we
            // haven't established — the wrapper may well be fine.
            _logger.LogWarning(
                "Could not determine termsrv.dll version — skipping rdpwrap.ini validation");
            return true;
        }

        try
        {
            if (IniHasOffsetsFor(File.ReadLines(iniPath), version))
            {
                _logger.LogInformation(
                    "rdpwrap.ini has offsets for termsrv.dll {Version}", version);
                return true;
            }

            var section = $"[{version}]";
            _logger.LogError(
                "rdpwrap.ini has no {Section} section, so it carries no offsets for the " +
                "termsrv.dll installed on this machine — multi-session will not work. " +
                "Update rdpwrap.ini from https://github.com/sebaxakerhtc/rdpwrap.ini " +
                "and restart TermService. (Note: termsrv.dll's FileVersion STRING may report " +
                "a different, stale version — {Version} is from the binary version fields.)",
                section, version);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read rdpwrap.ini");
            return false;
        }
    }

    /// <summary>
    /// True when rdpwrap.ini declares a section for this exact termsrv.dll version.
    ///
    /// Matches the section header <c>[10.0.26100.8737]</c> as a whole line, not as a
    /// substring: the ini is full of version-like numbers (other builds' sections, offset
    /// tables, sub-sections such as <c>[10.0.26100.8737-SLInit]</c>), so a substring test
    /// reports coverage that does not exist.
    ///
    /// Pure and static so it can be tested without a real Windows install — same rationale
    /// as <see cref="Streaming.ApolloManager.ResolveLogPath"/>.
    /// </summary>
    public static bool IniHasOffsetsFor(IEnumerable<string> iniLines, string termsrvVersion)
    {
        var section = $"[{termsrvVersion}]";

        foreach (var line in iniLines)
        {
            if (line.AsSpan().Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
