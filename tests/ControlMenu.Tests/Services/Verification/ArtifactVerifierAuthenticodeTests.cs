using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class ArtifactVerifierAuthenticodeTests
{
    private sealed class FakeInspector(AuthenticodeInfo info) : IAuthenticodeInspector
    {
        public AuthenticodeInfo Inspect(string filePath) => info;
    }
    private static string TempFile()
    {
        var p = Path.Combine(Path.GetTempPath(), "cm-sig-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(p, "x");
        return p;
    }
    private static ModuleDependency AdbDep() => new()
    {
        Name = "adb", ExecutableName = "adb", VersionCommand = "adb", VersionPattern = "(.+)",
        ExpectedSigner = "CN=Google LLC", AllowedHosts = ["dl.google.com"]
    };

    [Fact]
    public async Task ValidGoogleSignature_Verified()
    {
        var file = TempFile();
        try
        {
            var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Google LLC")), new HttpClient());
            var r = await v.VerifyAsync(file, AdbDep(), "37.0.0", default);
            Assert.True(r.Verified);
            Assert.Equal(VerificationTier.Authenticode, r.Tier);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task WrongSigner_HardFail()
    {
        var file = TempFile();
        try
        {
            var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Evil Corp")), new HttpClient());
            var r = await v.VerifyAsync(file, AdbDep(), "37.0.0", default);
            Assert.False(r.Verified);
            Assert.Equal(VerificationTier.Authenticode, r.Tier);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Unsigned_FallsThroughToUnverified()
    {
        var file = TempFile();
        try
        {
            var v = new ArtifactVerifier(new FakeInspector(new(false, false, null)), new HttpClient());
            var r = await v.VerifyAsync(file, AdbDep(), "37.0.0", default);
            Assert.Equal(VerificationTier.Unverified, r.Tier);
        }
        finally { File.Delete(file); }
    }

    // IsTrustedResult — offline-tolerant revocation policy (no cert/network needed).
    [Theory]
    [InlineData(0,                       true,  "S_OK")]
    [InlineData(unchecked((int)0x800B010E), true,  "CERT_E_REVOCATION_FAILURE")]
    [InlineData(unchecked((int)0x80092013), true,  "CRYPT_E_REVOCATION_OFFLINE")]
    [InlineData(unchecked((int)0x800B010C), false, "CERT_E_REVOKED")]
    [InlineData(unchecked((int)0x80096010), false, "TRUST_E_BAD_DIGEST")]
    [InlineData(unchecked((int)0x80004005), false, "E_FAIL (arbitrary non-zero)")]
    public void IsTrustedResult_PolicyTable(int hr, bool expected, string label)
    {
        _ = label; // for test name readability only
        Assert.Equal(expected, WindowsAuthenticodeInspector.IsTrustedResult(hr));
    }

    // Empirical coverage of the real WindowsAuthenticodeInspector P/Invoke path.
    [Fact]
    public void RealInspector_UnsignedFile_IsSignedFalse()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Non-Windows: inspector short-circuits; still returns IsSigned=false.
            var inspector = new WindowsAuthenticodeInspector();
            var p = Path.Combine(Path.GetTempPath(), "cm-sig-nonsig-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(p, [0x4D, 0x5A, 0x00, 0x00]); // MZ header, unsigned
            try
            {
                var info = inspector.Inspect(p);
                Assert.False(info.IsSigned);
            }
            finally { File.Delete(p); }
            return;
        }

        // Windows: write random bytes — no embedded Authenticode sig → IsSigned must be false.
        var path = Path.Combine(Path.GetTempPath(), "cm-sig-rnd-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, new byte[512]);
        try
        {
            var info = new WindowsAuthenticodeInspector().Inspect(path);
            Assert.False(info.IsSigned);
        }
        finally { File.Delete(path); }
    }
}
