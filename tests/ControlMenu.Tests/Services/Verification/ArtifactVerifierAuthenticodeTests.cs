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
        var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Google LLC")), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.True(r.Verified);
        Assert.Equal(VerificationTier.Authenticode, r.Tier);
    }

    [Fact]
    public async Task WrongSigner_HardFail()
    {
        var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Evil Corp")), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.Authenticode, r.Tier);
    }

    [Fact]
    public async Task Unsigned_FallsThroughToUnverified()
    {
        var v = new ArtifactVerifier(new FakeInspector(new(false, false, null)), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.Equal(VerificationTier.Unverified, r.Tier);
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
