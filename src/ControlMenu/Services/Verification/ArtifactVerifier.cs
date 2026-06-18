using System.Security.Cryptography;
using ControlMenu.Modules;

namespace ControlMenu.Services.Verification;

public sealed class ArtifactVerifier(IAuthenticodeInspector authenticode, HttpClient http)
    : IArtifactVerifier
{
    // Stored for T2 (http) and T3 (authenticode) added in Tasks 4-5.
    private readonly IAuthenticodeInspector _authenticode = authenticode;
    private readonly HttpClient _http = http;

    public async Task<VerificationResult> VerifyAsync(
        string filePath, ModuleDependency dep, string version, CancellationToken ct)
    {
        // T1 - pinned hash
        if (dep.KnownHashes.TryGetValue(version, out var expected))
        {
            var actual = await Sha256HexAsync(filePath, ct);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? new VerificationResult(true, VerificationTier.PinnedHash, "SHA-256", $"pinned {version}")
                : new VerificationResult(false, VerificationTier.PinnedHash, "SHA-256",
                    $"pinned hash mismatch (expected {expected}, got {actual})");
        }

        // T2/T3 added in Tasks 4-5. For now, fall through.
        return new VerificationResult(false, VerificationTier.Unverified, null,
            "no cryptographic tier available");
    }

    private static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hash);
    }
}
