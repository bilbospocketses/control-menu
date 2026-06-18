using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class UpstreamChecksumTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void SqlitePage_ExtractsSha3ForNamedAsset()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.SqliteDownloadPage,
            Fixture("sqlite-download-snippet.html"),
            "sqlite-tools-win-x64-3530000.zip");
        Assert.Equal("7b1d2c0f9a4e6b8d3c5f1029384756abcdef0123456789abcdef0123456789ab", hash);
    }

    [Fact]
    public void InToto_ExtractsSha256ForNamedAsset()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.InTotoJsonl,
            Fixture("imagemagick.intoto.jsonl"),
            "ImageMagick-7.1.2-25-portable-Q8-x64.7z");
        Assert.Equal("ff7c559f51bad365e3662f004aaed0e18c937d110f6e01183363602c07246e40", hash);
    }

    [Fact]
    public void UnknownAsset_ReturnsNull()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.InTotoJsonl, Fixture("imagemagick.intoto.jsonl"), "not-present.7z");
        Assert.Null(hash);
    }
}
