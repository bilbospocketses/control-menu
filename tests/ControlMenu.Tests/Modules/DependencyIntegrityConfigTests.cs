using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Imaging;
using ControlMenu.Modules.Jellyfin;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Modules;

public class DependencyIntegrityConfigTests
{
    private static ModuleDependency Dep(IToolModule m, string name) =>
        m.Dependencies.Single(d => d.Name == name);

    [Fact]
    public void Adb_UsesAuthenticodeSignerPin()
    {
        var adb = Dep(new AndroidDevicesModule(), "adb");
        Assert.Equal("CN=Google LLC", adb.ExpectedSigner);
        Assert.Contains("dl.google.com", adb.AllowedHosts);
        Assert.Null(adb.Checksum); // SHA-1 rejected
    }

    [Fact]
    public void Sqlite_UsesSha3PageChecksum()
    {
        var s = Dep(new JellyfinModule(), "sqlite3");
        Assert.Equal(ChecksumFormat.SqliteDownloadPage, s.Checksum!.Format);
        Assert.Equal(ChecksumAlgorithm.Sha3_256, s.Checksum!.Algorithm);
    }

    [Fact]
    public void Go2rtcAndVtracer_HaveHostsButNoCryptoSource()
    {
        var go2rtc = Dep(new CamerasModule(), "go2rtc");
        var vtracer = Dep(new ImagingModule(), "vtracer");
        foreach (var d in new[] { go2rtc, vtracer })
        {
            Assert.NotEmpty(d.AllowedHosts);
            Assert.Null(d.Checksum);
            Assert.Null(d.ExpectedSigner); // verified unsigned -> Tier 4
        }
    }

    // Seed-verification: KnownHashes pinned 2026-06-18 via scripts/update-dependency-hashes.ps1.
    // These assertions guard against a Tier-4-on-first-update regression (no hash = unverified
    // download proceeds), and confirm the version-key contract is satisfied for every dep.

    [Fact]
    public void Adb_HasPinnedHash()
    {
        var adb = Dep(new AndroidDevicesModule(), "adb");
        Assert.Equal("4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918",
            adb.KnownHashes["37.0.0"]);
    }

    [Fact]
    public void Sqlite_HasPinnedHash()
    {
        var s = Dep(new JellyfinModule(), "sqlite3");
        Assert.Equal("2eb7602bbe05895f4f530d8f3c4af244dcd8697d14b858778ccd4abe297a836d",
            s.KnownHashes["3.53.2"]);
    }

    [Fact]
    public void Magick_HasPinnedHash()
    {
        var magick = Dep(new ImagingModule(), "magick");
        Assert.Equal("ff7c559f51bad365e3662f004aaed0e18c937d110f6e01183363602c07246e40",
            magick.KnownHashes["7.1.2-25"]);
    }

    [Fact]
    public void Vtracer_HasPinnedHash()
    {
        var vtracer = Dep(new ImagingModule(), "vtracer");
        Assert.Equal("6b5bc17a6b017129ee40461df254f65d16f3b494c001a8541d41861066b716bf",
            vtracer.KnownHashes["0.6.4"]);
    }

    [Fact]
    public void Go2rtc_HasPinnedHash()
    {
        var go2rtc = Dep(new CamerasModule(), "go2rtc");
        Assert.Equal("dd4167d75cb04abe618855b7c71f8658bd009f60c1a71835d134d2c11c939907",
            go2rtc.KnownHashes["1.9.14"]);
    }

    [Fact]
    public void Potrace_HasPinnedHash()
    {
        // potrace is T1-only (no T2/T3); this hash is the primary guard against a
        // regression where potrace downloads unsigned and unverified.
        var potrace = Dep(new ImagingModule(), "potrace");
        Assert.Equal("63699f9885b5ed053977fd94452a7bdbb77ee62b77bf28a3f17b2f14b9b9c65d",
            potrace.KnownHashes["1.16"]);
    }
}
