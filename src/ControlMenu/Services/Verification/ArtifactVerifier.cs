using System.Security.Cryptography;
using System.Text.Json;
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
            var actual = await HashHexAsync(filePath, ChecksumAlgorithm.Sha256, ct);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? new VerificationResult(true, VerificationTier.PinnedHash, "SHA-256", $"pinned {version}")
                : new VerificationResult(false, VerificationTier.PinnedHash, "SHA-256",
                    $"pinned hash mismatch (expected {expected}, got {actual})");
        }

        // T2 - upstream checksum
        if (dep.Checksum is { } src && dep.AllowedHosts.Length > 0)
        {
            try
            {
                if (src.Algorithm == ChecksumAlgorithm.Sha3_256 && !SHA3_256.IsSupported)
                {
                    // OS lacks SHA3; cannot verify this tier -> fall through.
                }
                else
                {
                    var url = src.UrlOrTemplate.Replace("{version}", version);
                    var payload = await _http.GetStringAsync(url, ct);
                    var assetName = Path.GetFileName(new Uri(url).AbsolutePath);
                    var expectedHash = UpstreamChecksum.ExtractExpectedHash(src.Format, payload, assetName)
                                   ?? UpstreamChecksum.ExtractExpectedHash(src.Format, payload, Path.GetFileName(filePath));
                    if (expectedHash is not null)
                    {
                        var actual = await HashHexAsync(filePath, src.Algorithm, ct);
                        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)
                            ? new VerificationResult(true, VerificationTier.UpstreamChecksum, src.Algorithm.ToString(), "upstream checksum match")
                            : new VerificationResult(false, VerificationTier.UpstreamChecksum, src.Algorithm.ToString(),
                                $"upstream checksum mismatch (expected {expectedHash}, got {actual})");
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException
                                           or TaskCanceledException
                                           or JsonException
                                           or FormatException
                                           or UriFormatException
                                           or InvalidOperationException)
            {
                // Checksum source unreachable, URL malformed, or payload unparseable
                // -> fall through (do not hard-fail on a transient or config fault)
            }
        }

        // T3 added in Task 5. For now, fall through.
        return new VerificationResult(false, VerificationTier.Unverified, null,
            "no cryptographic tier available");
    }

    private static async Task<string> HashHexAsync(string path, ChecksumAlgorithm algo, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        byte[] hash = algo switch
        {
            ChecksumAlgorithm.Sha3_256 => await SHA3_256.HashDataAsync(fs, ct),
            _                          => await SHA256.HashDataAsync(fs, ct)
        };
        return Convert.ToHexStringLower(hash);
    }
}
