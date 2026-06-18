using System.Security.Cryptography;
using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class ArtifactVerifierPinnedTests
{
    private static string WriteTemp(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "cm-art-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(p, bytes);
        return p;
    }
    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private readonly ArtifactVerifier _verifier =
        new(new NullAuthenticodeInspector(), new HttpClient());

    [Fact]
    public async Task PinnedHash_ExactMatch_Verified()
    {
        var bytes = "payload-v1"u8.ToArray();
        var file = WriteTemp(bytes);
        var dep = DepWithHashes(("1.0", Sha256Hex(bytes)));
        var r = await _verifier.VerifyAsync(file, dep, "1.0", default);
        Assert.True(r.Verified);
        Assert.Equal(VerificationTier.PinnedHash, r.Tier);
    }

    [Fact]
    public async Task PinnedHash_Mismatch_HardFail()
    {
        var file = WriteTemp("tampered"u8.ToArray());
        var dep = DepWithHashes(("1.0", Sha256Hex("original"u8.ToArray())));
        var r = await _verifier.VerifyAsync(file, dep, "1.0", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.PinnedHash, r.Tier); // mismatch is attributed to the tier that ran
    }

    [Fact]
    public async Task NoTierAvailable_Unverified()
    {
        var file = WriteTemp("anything"u8.ToArray());
        var dep = DepWithHashes(); // no known hash for "9.9", no checksum, no signer
        var r = await _verifier.VerifyAsync(file, dep, "9.9", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.Unverified, r.Tier);
    }

    private static ModuleDependency DepWithHashes(params (string v, string h)[] hashes) =>
        new()
        {
            Name = "t", ExecutableName = "t", VersionCommand = "t", VersionPattern = "(.+)",
            KnownHashes = hashes.ToDictionary(x => x.v, x => x.h)
        };
}

// Minimal inspector that reports "not signed" so T3 always falls through in these tests.
file sealed class NullAuthenticodeInspector : IAuthenticodeInspector
{
    public AuthenticodeInfo Inspect(string filePath) => new(false, false, null);
}
