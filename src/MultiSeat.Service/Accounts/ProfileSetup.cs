using System.Runtime.InteropServices;
using Microsoft.Win32;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Accounts;

/// <summary>
/// Handles first-login profile hydration for new MultiSeat accounts.
/// Ensures the user profile directory exists and applies registry
/// tweaks needed for headless game streaming (disable animation,
/// disable lock screen, etc.).
/// </summary>
public sealed class ProfileSetup
{
    private readonly ILogger<ProfileSetup> _logger;

    public ProfileSetup(ILogger<ProfileSetup> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensure the user profile is created and apply streaming-optimized settings.
    /// Must be called after the first logon to the account.
    /// </summary>
    public string? EnsureProfileReady(IntPtr userToken, string accountName)
    {
        var profileDir = GetProfilePath(userToken);
        if (profileDir is null)
        {
            _logger.LogWarning("Could not resolve profile path for {Account}", accountName);
            return null;
        }

        _logger.LogInformation("Profile path for {Account}: {Path}", accountName, profileDir);

        // Apply registry tweaks for the user hive
        ApplyStreamingOptimizations(accountName, profileDir);

        return profileDir;
    }

    private static string? GetProfilePath(IntPtr userToken)
    {
        var buf = new char[260];
        var size = buf.Length;

        if (!UserEnv.GetUserProfileDirectoryW(userToken, buf, ref size))
            return null;

        return new string(buf, 0, size - 1); // trim null terminator
    }

    /// <summary>
    /// Load the user's NTUSER.DAT registry hive and apply streaming-optimized settings.
    /// These tweaks eliminate visual overhead and prevent session lockout.
    /// </summary>
    private void ApplyStreamingOptimizations(string accountName, string profileDir)
    {
        var hivePath = Path.Combine(profileDir, "NTUSER.DAT");
        if (!File.Exists(hivePath))
        {
            _logger.LogWarning("NTUSER.DAT not found at {Path} — skipping optimizations", hivePath);
            return;
        }

        // Load the user's registry hive under a temporary mount point
        var mountKey = $"TRIO_TEMP_{accountName}";
        var loadResult = RegLoadKey(HKEY_USERS, mountKey, hivePath);
        if (loadResult != 0)
        {
            _logger.LogWarning(
                "RegLoadKey failed for {Account} (error {Error}) — " +
                "profile may be in use or access denied. Optimizations skipped.",
                accountName, loadResult);
            return;
        }

        try
        {
            using var hive = Registry.Users.OpenSubKey(mountKey, writable: true);
            if (hive is null)
            {
                _logger.LogWarning("Failed to open loaded hive for {Account}", accountName);
                return;
            }

            int applied = 0;

            // ── Disable window animations ─────────────────────────────
            // Faster window rendering, less GPU overhead for compositing
            using (var dwm = hive.CreateSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                dwm.SetValue("EnableAeroPeek", 0, RegistryValueKind.DWord);
                dwm.SetValue("AlwaysHibernateThumbnails", 0, RegistryValueKind.DWord);
                applied++;
            }

            using (var visualFx = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"))
            {
                visualFx.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); // Custom
                applied++;
            }

            using (var desktop = hive.CreateSubKey(@"Control Panel\Desktop"))
            {
                desktop.SetValue("UserPreferencesMask", new byte[] {
                    0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
                desktop.SetValue("MenuShowDelay", "0", RegistryValueKind.String);
                applied++;
            }

            using (var windowMetrics = hive.CreateSubKey(@"Control Panel\Desktop\WindowMetrics"))
            {
                windowMetrics.SetValue("MinAnimate", "0", RegistryValueKind.String);
                applied++;
            }

            // ── Disable lock screen ───────────────────────────────────
            // Prevents session from auto-locking during streaming
            using (var personal = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lock Screen"))
            {
                personal.SetValue("Enabled", 0, RegistryValueKind.DWord);
                applied++;
            }

            using (var screenSaver = hive.CreateSubKey(@"Control Panel\Desktop"))
            {
                screenSaver.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                screenSaver.SetValue("ScreenSaverIsSecure", "0", RegistryValueKind.String);
                applied++;
            }

            // ── Disable Aero Shake and Snap Assist ────────────────────
            // Prevents accidental window management during gameplay
            using (var explorer = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
            {
                explorer.SetValue("DisallowShaking", 1, RegistryValueKind.DWord);
                explorer.SetValue("SnapAssist", 0, RegistryValueKind.DWord);
                explorer.SetValue("DITest", 0, RegistryValueKind.DWord); // Disable drag-snap
                applied++;
            }

            // ── Disable toast notifications ───────────────────────────
            // Prevents notification popups during gameplay
            using (var pushNotif = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
            {
                pushNotif.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
                applied++;
            }

            // ── Disable Game Bar overlay ──────────────────────────────
            // Prevents Xbox Game Bar from interfering with streaming
            using (var gameBar = hive.CreateSubKey(@"Software\Microsoft\GameBar"))
            {
                gameBar.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                gameBar.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                applied++;
            }

            // ── Enable Game Mode ──────────────────────────────────────
            // Prioritizes GPU/CPU for fullscreen applications
            using (var gameMode = hive.CreateSubKey(@"Software\Microsoft\GameBar"))
            {
                gameMode.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                applied++;
            }

            _logger.LogInformation(
                "Applied {Count} streaming optimizations for {Account}", applied, accountName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying registry optimizations for {Account}", accountName);
        }
        finally
        {
            // Always unload the hive
            var unloadResult = RegUnloadKey(HKEY_USERS, mountKey);
            if (unloadResult != 0)
                _logger.LogWarning("RegUnloadKey failed for {Account} (error {Error})", accountName, unloadResult);
        }
    }

    // ── P/Invoke for registry hive loading ───────────────────────────

    private static readonly IntPtr HKEY_USERS = new(unchecked((int)0x80000003));

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegUnloadKey(IntPtr hKey, string lpSubKey);
}
