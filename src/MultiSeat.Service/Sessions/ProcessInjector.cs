using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Sessions;

/// <summary>
/// Launches arbitrary executables inside a target Windows session.
///
/// Flow:
///   1. Acquire a primary token for the target session
///      (via WTSQueryUserToken or LogonUser + SetTokenInformation)
///   2. CreateEnvironmentBlock for the user's environment
///   3. CreateProcessAsUser targeting the session's desktop (WinSta0\Default)
///   4. Return the process ID and optionally wait for startup
///
/// Requirements:
///   - MultiSeat service must run as SYSTEM (for WTSQueryUserToken + SeTcbPrivilege)
///   - Target session must already exist (created by SessionLauncher)
/// </summary>
public sealed class ProcessInjector
{
    private readonly ILogger<ProcessInjector> _logger;
    private readonly MultiSeatOptions _options;
    private readonly SessionLauncher _sessionLauncher;

    public ProcessInjector(
        ILogger<ProcessInjector> logger,
        IOptions<MultiSeatOptions> options,
        SessionLauncher sessionLauncher)
    {
        _logger = logger;
        _options = options.Value;
        _sessionLauncher = sessionLauncher;
    }

    /// <summary>
    /// Launch a process inside the specified Windows session.
    /// Returns the PID of the launched process.
    /// </summary>
    public async Task<int> LaunchInSessionAsync(
        int sessionId,
        string accountName,
        string exePath,
        string? arguments = null,
        string? workingDir = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Launching '{Exe}' in session {Sid} (account: {Account})",
            exePath, sessionId, accountName);

        // Validate the executable exists and is accessible
        if (!File.Exists(exePath))
        {
            // It might be in PATH — only warn, don't block
            _logger.LogDebug("Executable not found at absolute path: {Exe} — " +
                "assuming it's in PATH or will resolve at runtime", exePath);
        }

        // Step 1: Get a primary token for the target session
        using var token = _sessionLauncher.GetSessionToken(sessionId, accountName);

        // Verify the token is stamped with the correct session ID
        if (AdvApi.GetTokenInformation(
                token, AdvApi.TokenInformationClass.TokenSessionId,
                out var tokenSid, sizeof(int), out _))
        {
            if (tokenSid != sessionId)
            {
                _logger.LogWarning(
                    "Token session ID mismatch: token has {TokenSid}, expected {Expected}. " +
                    "Re-stamping token.", tokenSid, sessionId);

                var targetSid = sessionId;
                if (!AdvApi.SetTokenInformation(
                        token, AdvApi.TokenInformationClass.TokenSessionId,
                        ref targetSid, sizeof(int)))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        $"SetTokenInformation(TokenSessionId={sessionId}) failed");
                }
            }
        }

        // Step 2: Create the environment block for the user
        if (!UserEnv.CreateEnvironmentBlock(out var envBlock, token, false))
        {
            _logger.LogWarning("CreateEnvironmentBlock failed: {Err}, using NULL env",
                Marshal.GetLastWin32Error());
            envBlock = IntPtr.Zero;
        }

        try
        {
            // Step 3: Build the command line
            var cmdLine = FormatCommandLine(exePath, arguments);

            var si = new Kernel32.StartupInfo
            {
                cb = Marshal.SizeOf<Kernel32.StartupInfo>(),
                lpDesktop = @"WinSta0\Default",
                dwFlags = Kernel32.STARTF_USESHOWWINDOW,
                wShowWindow = Kernel32.SW_SHOW  // games should be visible
            };

            var creationFlags =
                Kernel32.CREATE_UNICODE_ENVIRONMENT |
                Kernel32.CREATE_NEW_CONSOLE |
                Kernel32.NORMAL_PRIORITY_CLASS;

            // Step 4: Launch the process
            if (!AdvApi.CreateProcessAsUserW(
                    token,
                    null,          // use cmdLine for both app + args
                    cmdLine,
                    IntPtr.Zero,   // default process security
                    IntPtr.Zero,   // default thread security
                    false,         // don't inherit handles
                    creationFlags,
                    envBlock,
                    workingDir,    // working directory (null = inherit)
                    ref si,
                    out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CreateProcessAsUser failed for '{exePath}' in session {sessionId}: " +
                    $"Win32 error {err}. Ensure the service runs as SYSTEM.");
            }

            // Close handles we don't need
            Kernel32.CloseHandle(pi.hThread);

            _logger.LogInformation(
                "Process launched: '{Exe}' PID {Pid} in session {Sid}",
                exePath, pi.dwProcessId, sessionId);

            try
            {
                // Step 5: Wait briefly for the process to initialize
                await WaitForProcessStartAsync(pi, ct);
            }
            finally
            {
                // Close the process handle even if the wait is cancelled, so it never leaks.
                Kernel32.CloseHandle(pi.hProcess);
            }
            return pi.dwProcessId;
        }
        finally
        {
            if (envBlock != IntPtr.Zero)
                UserEnv.DestroyEnvironmentBlock(envBlock);
        }
    }

    /// <summary>
    /// Launch Apollo streaming server inside a target session.
    /// Special handling: Apollo needs specific env vars and the config path.
    /// </summary>
    public Task<int> LaunchApolloInSessionAsync(
        int sessionId,
        string accountName,
        string apolloExePath,
        string configPath,
        CancellationToken ct)
    {
        var arguments = $"\"{configPath}\"";

        // Working dir = per-seat config dir (not the Apollo install dir).
        // Apollo resolves sunshine_state.json from {workingDir}/config/sunshine_state.json.
        // Each seat must have its own working dir so they get distinct UUIDs and
        // Moonlight can list them as separate servers.
        return LaunchInSessionAsync(
            sessionId, accountName,
            apolloExePath, arguments,
            Path.GetDirectoryName(configPath),
            ct);
    }

    /// <summary>
    /// Launch any executable in the active console session using the current
    /// console user's token. For GUI apps (e.g. VoiceMeeter) that must run in
    /// an interactive session — Process.Start from SYSTEM targets Session 0,
    /// which is non-interactive and prevents GUI apps from functioning.
    /// </summary>
    public async Task<int> LaunchInConsoleSessionAsync(
        string exePath,
        string? arguments,
        CancellationToken ct)
    {
        var consoleSessionId = Kernel32.WTSGetActiveConsoleSessionId();
        _logger.LogInformation(
            "Launching '{Exe}' in console session {Sid}", exePath, consoleSessionId);

        if (!WtsApi.WTSQueryUserToken(consoleSessionId, out var userToken))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err,
                $"WTSQueryUserToken failed for console session {consoleSessionId}");
        }

        using var token = new SafeTokenHandle(userToken);

        if (!UserEnv.CreateEnvironmentBlock(out var envBlock, token, false))
            envBlock = IntPtr.Zero;

        try
        {
            var cmdLine = FormatCommandLine(exePath, arguments);
            var workingDir = Path.GetDirectoryName(exePath);

            var si = new Kernel32.StartupInfo
            {
                cb = Marshal.SizeOf<Kernel32.StartupInfo>(),
                lpDesktop = @"WinSta0\Default",
                dwFlags = Kernel32.STARTF_USESHOWWINDOW,
                wShowWindow = Kernel32.SW_SHOWMINNOACTIVE,
            };

            var creationFlags =
                Kernel32.CREATE_UNICODE_ENVIRONMENT |
                Kernel32.CREATE_NEW_CONSOLE |
                Kernel32.NORMAL_PRIORITY_CLASS;

            if (!AdvApi.CreateProcessAsUserW(
                    token,
                    null,
                    cmdLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    envBlock,
                    workingDir,
                    ref si,
                    out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CreateProcessAsUser failed for '{exePath}' in console session: Win32 error {err}");
            }

            Kernel32.CloseHandle(pi.hThread);

            _logger.LogInformation(
                "Process '{Exe}' launched in console session {Sid}: PID {Pid}",
                exePath, consoleSessionId, pi.dwProcessId);

            try
            {
                await WaitForProcessStartAsync(pi, ct);
            }
            finally
            {
                Kernel32.CloseHandle(pi.hProcess);
            }
            return pi.dwProcessId;
        }
        finally
        {
            if (envBlock != IntPtr.Zero)
                UserEnv.DestroyEnvironmentBlock(envBlock);
        }
    }

    /// <summary>
    /// Launch an executable in the console session and wait for it to exit.
    /// Returns the process exit code, or -1 on timeout.
    /// Used for short-lived helper processes that must run in the console session
    /// (e.g. --enum-displays, --set-display-hz) where the service needs the result.
    /// </summary>
    public async Task<int> RunInConsoleSessionAsync(
        string exePath,
        string? arguments,
        int timeoutMs,
        CancellationToken ct)
    {
        var consoleSessionId = Kernel32.WTSGetActiveConsoleSessionId();
        _logger.LogDebug(
            "Running '{Exe}' in console session {Sid} (timeout {Ms}ms)",
            exePath, consoleSessionId, timeoutMs);

        if (!WtsApi.WTSQueryUserToken(consoleSessionId, out var userToken))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err,
                $"WTSQueryUserToken failed for console session {consoleSessionId}");
        }

        using var token = new SafeTokenHandle(userToken);

        if (!UserEnv.CreateEnvironmentBlock(out var envBlock, token, false))
            envBlock = IntPtr.Zero;

        try
        {
            var cmdLine = FormatCommandLine(exePath, arguments);
            var workingDir = Path.GetDirectoryName(exePath);

            var si = new Kernel32.StartupInfo
            {
                cb = Marshal.SizeOf<Kernel32.StartupInfo>(),
                lpDesktop = @"WinSta0\Default",
                dwFlags = Kernel32.STARTF_USESHOWWINDOW,
                wShowWindow = Kernel32.SW_HIDE,
            };

            var creationFlags =
                Kernel32.CREATE_UNICODE_ENVIRONMENT |
                Kernel32.CREATE_NO_WINDOW |
                Kernel32.NORMAL_PRIORITY_CLASS;

            if (!AdvApi.CreateProcessAsUserW(
                    token, null, cmdLine,
                    IntPtr.Zero, IntPtr.Zero, false,
                    creationFlags, envBlock, workingDir,
                    ref si, out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CreateProcessAsUser failed for '{exePath}' in console session: Win32 error {err}");
            }

            Kernel32.CloseHandle(pi.hThread);

            try
            {
                // Wait for the process to finish
                var waitResult = await Task.Run(
                    () => Kernel32.WaitForSingleObject(pi.hProcess, (uint)timeoutMs), ct);

                int exitCode = -1;
                if (waitResult == Kernel32.WAIT_OBJECT_0)
                {
                    Kernel32.GetExitCodeProcess(pi.hProcess, out var raw);
                    exitCode = (int)raw;
                }
                else
                {
                    _logger.LogWarning(
                        "Helper '{Exe}' did not exit within {Ms}ms — killing it", exePath, timeoutMs);
                }

                return exitCode;
            }
            finally
            {
                Kernel32.CloseHandle(pi.hProcess);
            }
        }
        finally
        {
            if (envBlock != IntPtr.Zero)
                UserEnv.DestroyEnvironmentBlock(envBlock);
        }
    }

    /// <summary>
    /// Launch Apollo in the active console session (where the GPU display pipeline lives).
    /// RDP sessions cannot access DXGI — QueryDisplayConfig returns ACCESS_DENIED.
    /// The console session has full GPU access, so Apollo can create and capture
    /// SudoVDA virtual displays from there.
    /// </summary>
    public async Task<int> LaunchApolloInConsoleSessionAsync(
        string apolloExePath,
        string configPath,
        CancellationToken ct)
    {
        var consoleSessionId = Kernel32.WTSGetActiveConsoleSessionId();
        _logger.LogInformation(
            "Launching Apollo in console session {Sid} with config: {Config}",
            consoleSessionId, configPath);

        // Get a token for the console session — this gives us GPU/DXGI access.
        // WTSQueryUserToken returns the ELEVATED token for admin users.
        // SudoVDA IPC requires a medium-integrity (non-elevated) token — the same
        // as a normal shell-launched process. We use the linked filtered token instead.
        if (!WtsApi.WTSQueryUserToken(consoleSessionId, out var userToken))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err,
                $"WTSQueryUserToken failed for console session {consoleSessionId}");
        }

        // Try to get the linked filtered token (medium integrity).
        // For non-admin users GetTokenInformationHandle will fail — fall back to userToken.
        IntPtr primaryToken;
        if (AdvApi.GetTokenInformationHandle(
                userToken,
                AdvApi.TokenInformationClass.TokenLinkedToken,
                out var linkedToken) && linkedToken != IntPtr.Zero)
        {
            _logger.LogDebug(
                "Console session {Sid}: using filtered (medium-integrity) linked token for Apollo",
                consoleSessionId);
            Kernel32.CloseHandle(userToken);
            primaryToken = linkedToken;
        }
        else
        {
            _logger.LogDebug(
                "Console session {Sid}: no linked token found, using WTSQueryUserToken result directly",
                consoleSessionId);
            primaryToken = userToken;
        }

        using var token = new SafeTokenHandle(primaryToken);

        if (!UserEnv.CreateEnvironmentBlock(out var envBlock, token, false))
            envBlock = IntPtr.Zero;

        try
        {
            var arguments = $"\"{configPath}\"";
            var cmdLine = FormatCommandLine(apolloExePath, arguments);
            var workingDir = Path.GetDirectoryName(configPath);

            var si = new Kernel32.StartupInfo
            {
                cb = Marshal.SizeOf<Kernel32.StartupInfo>(),
                lpDesktop = @"WinSta0\Default",
                dwFlags = Kernel32.STARTF_USESHOWWINDOW,
                wShowWindow = Kernel32.SW_SHOW,
            };

            var creationFlags =
                Kernel32.CREATE_UNICODE_ENVIRONMENT |
                Kernel32.CREATE_NEW_CONSOLE |
                Kernel32.NORMAL_PRIORITY_CLASS;

            if (!AdvApi.CreateProcessAsUserW(
                    token,
                    null,
                    cmdLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    envBlock,
                    workingDir,
                    ref si,
                    out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err,
                    $"CreateProcessAsUser failed for Apollo in console session: Win32 error {err}");
            }

            Kernel32.CloseHandle(pi.hThread);

            _logger.LogInformation(
                "Apollo launched in console session {Sid}: PID {Pid}",
                consoleSessionId, pi.dwProcessId);

            try
            {
                await WaitForProcessStartAsync(pi, ct);
            }
            finally
            {
                Kernel32.CloseHandle(pi.hProcess);
            }
            return pi.dwProcessId;
        }
        finally
        {
            if (envBlock != IntPtr.Zero)
                UserEnv.DestroyEnvironmentBlock(envBlock);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Format the command line string for CreateProcessAsUser.
    /// The executable must be quoted if it contains spaces.
    /// </summary>
    private static string FormatCommandLine(string exePath, string? arguments)
    {
        // Always quote the executable path
        var quoted = exePath.Contains(' ') || !exePath.StartsWith('"')
            ? $"\"{exePath}\""
            : exePath;

        return string.IsNullOrWhiteSpace(arguments)
            ? quoted
            : $"{quoted} {arguments}";
    }

    /// <summary>
    /// Brief wait to confirm the process didn't crash on startup.
    /// Checks that the process is still alive after a short delay.
    /// </summary>
    private async Task WaitForProcessStartAsync(
        Kernel32.ProcessInformation pi, CancellationToken ct)
    {
        // Wait up to 2 seconds for the process to either stabilize or crash
        var waitResult = await Task.Run(
            () => Kernel32.WaitForSingleObject(pi.hProcess, 2000), ct);

        if (waitResult == Kernel32.WAIT_OBJECT_0)
        {
            // Process exited already — get exit code
            Kernel32.GetExitCodeProcess(pi.hProcess, out var exitCode);
            _logger.LogWarning(
                "Process PID {Pid} exited immediately with code {Code}",
                pi.dwProcessId, exitCode);
        }
        else if (waitResult == Kernel32.WAIT_TIMEOUT)
        {
            // Still running — good
            _logger.LogDebug("Process PID {Pid} is running after 2s startup check",
                pi.dwProcessId);
        }
    }
}
