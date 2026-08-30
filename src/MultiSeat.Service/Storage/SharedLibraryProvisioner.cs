using System.Diagnostics;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;

namespace MultiSeat.Service.Storage;

/// <summary>
/// Creates a shared game library all seat accounts can read/write, so games and ROMs are not
/// siloed per Windows account. Layout under <see cref="MultiSeatOptions.SharedGameLibraryDir"/>:
///   {root}\SteamLibrary  — add this as a Steam Library Folder in each seat's Steam so an
///                          already-installed game owned by another account isn't re-downloaded.
///   {root}\ROMs          — shared ROM/content directory for emulators.
///
/// Host-level, idempotent, run once at service startup. Grants BUILTIN\Users Modify via icacls
/// (matches the netsh/mklink shell-out style used elsewhere).
/// </summary>
public sealed class SharedLibraryProvisioner
{
    private readonly ILogger<SharedLibraryProvisioner> _logger;
    private readonly MultiSeatOptions _options;

    // Well-known SID for BUILTIN\Users — locale-independent (avoids the localized group name).
    private const string UsersSid = "*S-1-5-32-545";

    public SharedLibraryProvisioner(
        ILogger<SharedLibraryProvisioner> logger, IOptions<MultiSeatOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>Absolute path to the shared Steam library folder.</summary>
    public string SteamLibraryDir => Path.Combine(_options.SharedGameLibraryDir, "SteamLibrary");

    /// <summary>Absolute path to the shared ROMs folder.</summary>
    public string RomsDir => Path.Combine(_options.SharedGameLibraryDir, "ROMs");

    public async Task EnsureSharedLibraryAsync(CancellationToken ct)
    {
        if (!_options.EnableSharedGameLibrary)
            return;

        try
        {
            var root = _options.SharedGameLibraryDir;
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(SteamLibraryDir);
            Directory.CreateDirectory(RomsDir);

            // Grant BUILTIN\Users Modify with inheritance so every seat account can read ROMs and
            // download into the Steam library. (OI)(CI) = object + container inherit; M = Modify.
            await GrantUsersModifyAsync(root, ct);

            _logger.LogInformation(
                "Shared game library ready: Steam → {Steam} | ROMs → {Roms}. " +
                "Add the SteamLibrary folder in each seat's Steam (Settings → Storage).",
                SteamLibraryDir, RomsDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not provision shared game library at {Dir} (non-critical)",
                _options.SharedGameLibraryDir);
        }
    }

    /// <summary>
    /// The icacls arguments for the grant, separated out so they can be asserted.
    ///
    /// Three things here are load-bearing and none of them announce themselves when wrong:
    /// the SID form rather than the group name (a localized Windows has no "Users" group to
    /// resolve), the (OI)(CI) inheritance (without it seats can enter the folder but not the
    /// game directories inside it), and the quoting (the default path has no space, but
    /// SharedGameLibraryDir is user-settable and one space would truncate the command).
    /// </summary>
    internal static string IcaclsArguments(string dir) =>
        $"\"{dir}\" /grant \"{UsersSid}:(OI)(CI)M\" /T /C /Q";

    private async Task GrantUsersModifyAsync(string dir, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("icacls", IcaclsArguments(dir))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.LogWarning("Failed to start icacls for {Dir}", dir);
                return;
            }

            var error = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
                _logger.LogWarning(
                    "icacls grant on {Dir} exited {Code}: {Err}", dir, proc.ExitCode, error.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "icacls grant failed for {Dir}", dir);
        }
    }
}
