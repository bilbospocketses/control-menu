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
}
