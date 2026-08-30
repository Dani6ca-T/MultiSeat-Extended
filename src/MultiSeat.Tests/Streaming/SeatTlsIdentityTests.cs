using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// A seat used to be seeded with a copy of the console Apollo's cakey.pem, so every seat and the
/// console shared one private key — and the source copy stays readable by every local user, which
/// no permission work on our side can fix. Any seat could therefore impersonate any other.
///
/// The fix is to seed nothing: Apollo generates its own credentials when either file is missing.
/// Replacing an existing seat's key is opt-in, because a client pairs against the SERVER
/// CERTIFICATE (Apollo hands it over as root.plaincert during pairing), so rotating it makes every
/// client paired to that seat pair again.
/// </summary>
public class SeatTlsIdentityTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"multiseat-tls-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string BuildConfigFor(string account, bool rotate = false)
    {
        var builder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(),
            Options.Create(new MultiSeatOptions { RotateSharedSeatTls = rotate }));

        return builder.BuildConfig(
            new SeatInfo { AccountName = account, PortBase = 47984 }, _root);
    }

    private string CredDir(string account) =>
        Path.Combine(_root, account, "config", "credentials");

    // ── Seeding ───────────────────────────────────────────────────────

    [Fact]
    public void ProvisioningSeedsNoTlsIdentity()
    {
        // The point of the change. An empty credentials directory is what makes Apollo generate a
        // key for this seat alone; anything we put there would be a key something else also holds.
        BuildConfigFor("SeatA");

        var dir = CredDir("SeatA");
        Assert.True(Directory.Exists(dir), "the credentials directory must still be created");
        Assert.False(File.Exists(Path.Combine(dir, "cakey.pem")));
        Assert.False(File.Exists(Path.Combine(dir, "cacert.pem")));
    }

    [Fact]
    public void TheConfigStillPointsAtThatDirectory()
    {
        // Apollo only generates into the path the config names. If these ever disagree it writes
        // its key somewhere the seat cannot reach and dies on "HTTP interface failed to
        // initialize" — the failure PR #21 fixed.
        var content = File.ReadAllText(BuildConfigFor("SeatA"));
        var expected = CredDir("SeatA").Replace('\\', '/');

        Assert.Contains($"pkey = {expected}/cakey.pem", content);
        Assert.Contains($"cert = {expected}/cacert.pem", content);
    }

    // ── Rotation is opt-in and precise ────────────────────────────────

    [Fact]
    public void ByDefaultAnExistingKeyIsLeftAlone()
    {
        // Silence is the safe default here: touching a key rotates a seat's identity and forces
        // every client on it to pair again.
        BuildConfigFor("SeatA");
        var key = Path.Combine(CredDir("SeatA"), "cakey.pem");
        File.WriteAllText(key, "EXISTING KEY");

        BuildConfigFor("SeatA");

        Assert.True(File.Exists(key));
        Assert.Equal("EXISTING KEY", File.ReadAllText(key));
    }

    [Fact]
    public void AKeyApolloGeneratedIsNeverRotated_EvenWhenAsked()
    {
        // The safety of the whole feature. A seat that already owns its key does not match the
        // console Apollo's, so rotation must skip it — deleting it would break that seat's
        // pairings for nothing.
        BuildConfigFor("SeatA");
        var key = Path.Combine(CredDir("SeatA"), "cakey.pem");
        File.WriteAllText(key, "A KEY THIS SEAT GENERATED FOR ITSELF");

        BuildConfigFor("SeatA", rotate: true);

        Assert.True(File.Exists(key));
        Assert.Equal("A KEY THIS SEAT GENERATED FOR ITSELF", File.ReadAllText(key));
    }

    // ── The identity check the rotation rests on ──────────────────────

    [Fact]
    public void IdenticalFilesAreRecognised()
    {
        Directory.CreateDirectory(_root);
        var a = Path.Combine(_root, "a.pem");
        var b = Path.Combine(_root, "b.pem");
        File.WriteAllText(a, "-----BEGIN RSA PRIVATE KEY-----\nsame\n");
        File.WriteAllText(b, "-----BEGIN RSA PRIVATE KEY-----\nsame\n");

        Assert.True(ApolloConfigBuilder.IsSameFile(a, b));
    }

    [Fact]
    public void SameLengthButDifferentContentIsNotAMatch()
    {
        // Two RSA keys of the same size are the same LENGTH on disk, so a length-only comparison
        // would call every seat's freshly generated key "the seeded one" and delete it. The bytes
        // have to be compared.
        Directory.CreateDirectory(_root);
        var a = Path.Combine(_root, "a.pem");
        var b = Path.Combine(_root, "b.pem");
        File.WriteAllText(a, "AAAAAAAAAAAAAAAA");
        File.WriteAllText(b, "BBBBBBBBBBBBBBBB");

        Assert.Equal(new FileInfo(a).Length, new FileInfo(b).Length);
        Assert.False(ApolloConfigBuilder.IsSameFile(a, b));
    }

    [Fact]
    public void AMissingFileIsNeverAMatch()
    {
        // Reached when the console Apollo is not installed at all. Treating "both absent" as equal
        // would make rotation delete a key on a host that has no shared key to be rid of.
        Directory.CreateDirectory(_root);
        var real = Path.Combine(_root, "real.pem");
        File.WriteAllText(real, "content");
        var absent = Path.Combine(_root, "absent.pem");

        Assert.False(ApolloConfigBuilder.IsSameFile(real, absent));
        Assert.False(ApolloConfigBuilder.IsSameFile(absent, real));
        Assert.False(ApolloConfigBuilder.IsSameFile(absent, absent));
    }
}
