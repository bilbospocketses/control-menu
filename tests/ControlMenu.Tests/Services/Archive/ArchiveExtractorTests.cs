using ControlMenu.Services.Archive;

namespace ControlMenu.Tests.Services.Archive;

public class ArchiveExtractorTests
{
    private static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly ArchiveExtractor _extractor = new();

    [Fact]
    public void Extract_SevenZipWithBcjFilter_ExtractsEntry()
    {
        var archive = Path.Combine(FixtureDir, "bcj-sample.7z");
        var dest = Path.Combine(Path.GetTempPath(), "cm-7z-" + Guid.NewGuid().ToString("N"));
        try
        {
            _extractor.Extract(archive, dest);
            var extracted = Path.Combine(dest, "sample.exe");
            Assert.True(File.Exists(extracted), "sample.exe should have been extracted");
            Assert.StartsWith("MZ", File.ReadAllText(extracted));
        }
        finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }

    [Fact]
    public void Extract_UnsupportedExtension_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => _extractor.Extract("whatever.rar", "dest"));
    }
}
