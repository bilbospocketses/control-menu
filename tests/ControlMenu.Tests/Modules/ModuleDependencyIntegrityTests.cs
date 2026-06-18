using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Modules;

public class ModuleDependencyIntegrityTests
{
    [Fact]
    public void IntegrityFields_DefaultToEmpty()
    {
        var dep = new ModuleDependency
        {
            Name = "x", ExecutableName = "x",
            VersionCommand = "x --version", VersionPattern = "(.+)"
        };
        Assert.Empty(dep.KnownHashes);
        Assert.Empty(dep.AllowedHosts);
        Assert.Null(dep.Checksum);
        Assert.Null(dep.ExpectedSigner);
    }

    [Fact]
    public void Checksum_RecordHoldsFormatAndAlgorithm()
    {
        var c = new ChecksumSource("https://x/page", ChecksumFormat.SqliteDownloadPage, ChecksumAlgorithm.Sha3_256);
        Assert.Equal(ChecksumFormat.SqliteDownloadPage, c.Format);
        Assert.Equal(ChecksumAlgorithm.Sha3_256, c.Algorithm);
    }
}
