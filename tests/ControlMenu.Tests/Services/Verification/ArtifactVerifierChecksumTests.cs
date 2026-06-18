using System.Net;
using System.Security.Cryptography;
using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

/// <summary>
/// Verifier-level T2 tests. Uses a fake <see cref="HttpMessageHandler"/> so no real network
/// access is needed — <c>HttpClient</c> itself fetches the payload, bypassing the file:// limitation.
/// </summary>
public class ArtifactVerifierChecksumTests
{
    // --- helpers ---

    private static string WriteTemp(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "cm-t2-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// Returns an <see cref="HttpClient"/> whose only handler returns <paramref name="body"/>
    /// for any request.
    /// </summary>
    private static HttpClient FakeHttp(string body)
    {
        var handler = new FakeHttpHandler(body);
        return new HttpClient(handler);
    }

    private static ModuleDependency DepWithChecksum(ChecksumSource src, string host = "example.com") =>
        new()
        {
            Name = "t", ExecutableName = "t", VersionCommand = "t", VersionPattern = "(.+)",
            Checksum = src,
            AllowedHosts = [host]
        };

    // --- tests ---

    [Fact]
    public async Task T2_InToto_Match_Verified()
    {
        var bytes = "payload"u8.ToArray();
        var sha256 = Sha256Hex(bytes);

        // The in-toto subject name matches the asset filename — verifier's fallback path
        // (Path.GetFileName(filePath)) resolves it because we write the temp file with that name.
        var assetName = "mylib-1.0.0.7z";
        var jsonl = $"{{\"_type\":\"https://in-toto.io/Statement/v1\",\"subject\":[{{\"name\":\"{assetName}\",\"digest\":{{\"sha256\":\"{sha256}\"}}}}],\"predicateType\":\"https://slsa.dev/provenance/v1\",\"predicate\":{{}}}}";

        // Write artifact file named after the asset so Path.GetFileName(filePath) == assetName.
        var file = Path.Combine(Path.GetTempPath(), assetName);
        File.WriteAllBytes(file, bytes);

        var url = $"https://example.com/provenance/{assetName}.intoto.jsonl";
        var src = new ChecksumSource(url, ChecksumFormat.InTotoJsonl, ChecksumAlgorithm.Sha256);

        var verifier = new ArtifactVerifier(new NullAuthenticodeInspector2(), FakeHttp(jsonl));
        var r = await verifier.VerifyAsync(file, DepWithChecksum(src), "1.0.0", default);

        Assert.True(r.Verified);
        Assert.Equal(VerificationTier.UpstreamChecksum, r.Tier);
    }

    [Fact]
    public async Task T2_InToto_Mismatch_HardFail()
    {
        var bytes = "payload"u8.ToArray();
        var wrongHash = Sha256Hex("different"u8.ToArray());

        var assetName = "mylib-1.0.0-mismatch.7z";
        var jsonl = $"{{\"_type\":\"https://in-toto.io/Statement/v1\",\"subject\":[{{\"name\":\"{assetName}\",\"digest\":{{\"sha256\":\"{wrongHash}\"}}}}],\"predicateType\":\"https://slsa.dev/provenance/v1\",\"predicate\":{{}}}}";

        var file = Path.Combine(Path.GetTempPath(), assetName);
        File.WriteAllBytes(file, bytes);

        var url = $"https://example.com/provenance/{assetName}.intoto.jsonl";
        var src = new ChecksumSource(url, ChecksumFormat.InTotoJsonl, ChecksumAlgorithm.Sha256);

        var verifier = new ArtifactVerifier(new NullAuthenticodeInspector2(), FakeHttp(jsonl));
        var r = await verifier.VerifyAsync(file, DepWithChecksum(src), "1.0.0", default);

        // A found-but-mismatched checksum is a hard fail — must NOT fall through to Unverified.
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.UpstreamChecksum, r.Tier);
    }

    [Fact]
    public async Task T2_NetworkError_FallsThrough_ToUnverified()
    {
        var file = WriteTemp("payload"u8.ToArray());
        var src = new ChecksumSource("https://example.com/checksums.jsonl", ChecksumFormat.InTotoJsonl, ChecksumAlgorithm.Sha256);

        var verifier = new ArtifactVerifier(new NullAuthenticodeInspector2(), FakeHttp(null!));
        var r = await verifier.VerifyAsync(file, DepWithChecksum(src), "1.0.0", default);

        // Network error must fall through; T3/Unverified is the next result (no T3 yet → Unverified).
        Assert.Equal(VerificationTier.Unverified, r.Tier);
    }

    [Fact]
    public async Task T2_NoChecksumConfigured_FallsThrough_ToUnverified()
    {
        var file = WriteTemp("payload"u8.ToArray());
        var dep = new ModuleDependency
        {
            Name = "t", ExecutableName = "t", VersionCommand = "t", VersionPattern = "(.+)"
            // no Checksum, no AllowedHosts
        };

        var verifier = new ArtifactVerifier(new NullAuthenticodeInspector2(), new HttpClient());
        var r = await verifier.VerifyAsync(file, dep, "1.0.0", default);

        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.Unverified, r.Tier);
    }
}

// Minimal inspector that reports "not signed" so T3 always falls through in these tests.
file sealed class NullAuthenticodeInspector2 : IAuthenticodeInspector
{
    public AuthenticodeInfo Inspect(string filePath) => new(false, false, null);
}

/// <summary>
/// Returns a fixed body string for any GET. Throws <see cref="HttpRequestException"/> when body
/// is null, simulating a network error.
/// </summary>
file sealed class FakeHttpHandler(string? body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (body is null)
            throw new HttpRequestException("simulated network failure");

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
        return Task.FromResult(response);
    }
}
