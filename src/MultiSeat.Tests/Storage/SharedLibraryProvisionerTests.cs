using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Storage;
using MultiSeat.Tests.Streaming;   // TestLogger<T>
using Xunit;

namespace MultiSeat.Tests.Storage;

/// <summary>
/// The shared library is what stops each seat re-downloading the same game into its own Windows
/// account. It is created once at startup and then nothing ever checks it again, so a failure here
/// shows up much later as "this seat is downloading Baldur's Gate for the third time" rather than
/// as an error.
/// </summary>
public class SharedLibraryProvisionerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"multiseat-lib-{Guid.NewGuid():N}");

    private SharedLibraryProvisioner NewProvisioner(bool enabled = true) =>
        new(new TestLogger<SharedLibraryProvisioner>(),
            Options.Create(new MultiSeatOptions
            {
                EnableSharedGameLibrary = enabled,
                SharedGameLibraryDir = _root,
            }));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Layout ────────────────────────────────────────────────────────

    [Fact]
    public void TheTwoFoldersHangOffTheConfiguredRoot()
    {
        // These paths are handed to the user ("add this folder in Steam") and are written into
        // each seat's RetroArch config by the seeder, so they are part of the contract, not an
        // implementation detail.
        var p = NewProvisioner();

        Assert.Equal(Path.Combine(_root, "SteamLibrary"), p.SteamLibraryDir);
        Assert.Equal(Path.Combine(_root, "ROMs"), p.RomsDir);
    }

    [Fact]
    public async Task ProvisioningCreatesBothFolders()
    {
        await NewProvisioner().EnsureSharedLibraryAsync(CancellationToken.None);

        Assert.True(Directory.Exists(_root));
        Assert.True(Directory.Exists(Path.Combine(_root, "SteamLibrary")));
        Assert.True(Directory.Exists(Path.Combine(_root, "ROMs")));
    }

    [Fact]
    public async Task ProvisioningTwiceIsFine()
    {
        // It runs at every service start, so it has to be idempotent - and must not disturb what
        // is already in the folders.
        var provisioner = NewProvisioner();
        await provisioner.EnsureSharedLibraryAsync(CancellationToken.None);

        var marker = Path.Combine(_root, "ROMs", "game.rom");
        File.WriteAllText(marker, "content");

        await provisioner.EnsureSharedLibraryAsync(CancellationToken.None);

        Assert.True(File.Exists(marker));
        Assert.Equal("content", File.ReadAllText(marker));
    }

    [Fact]
    public async Task DisabledMeansNothingIsCreated()
    {
        // A feature flag that still made directories would leave an empty C:\MultiSeatGames on
        // hosts that deliberately turned the feature off.
        await NewProvisioner(enabled: false).EnsureSharedLibraryAsync(CancellationToken.None);

        Assert.False(Directory.Exists(_root));
    }

    // ── The grant ─────────────────────────────────────────────────────

    [Fact]
    public void TheGrantUsesTheWellKnownSid_NotTheGroupName()
    {
        // "BUILTIN\\Users" does not exist by that name on a non-English Windows, and icacls would
        // fail to resolve it - the same class of bug that made seat provisioning fail on localized
        // hosts before the group SIDs were used. S-1-5-32-545 is the same everywhere.
        var args = SharedLibraryProvisioner.IcaclsArguments(@"C:\MultiSeatGames");

        Assert.Contains("*S-1-5-32-545", args);
        Assert.DoesNotContain("Users:", args);
    }

    [Fact]
    public void TheGrantIsInheritedByEverythingInside()
    {
        // (OI)(CI) is what makes the grant reach the game directories a seat creates later.
        // Without it a seat can enter the library folder and write nothing useful into it.
        var args = SharedLibraryProvisioner.IcaclsArguments(@"C:\MultiSeatGames");

        Assert.Contains("(OI)(CI)M", args);
        Assert.Contains("/T", args);        // and reaches what is already there
    }

    [Fact]
    public void APathWithASpaceStaysOneArgument()
    {
        // SharedGameLibraryDir is user-settable. The default has no space in it, which is exactly
        // why an unquoted path would survive every test on the reference host and break on someone
        // else's "D:\Game Library".
        var args = SharedLibraryProvisioner.IcaclsArguments(@"D:\Game Library");

        Assert.StartsWith("\"D:\\Game Library\"", args, StringComparison.Ordinal);
    }
}
