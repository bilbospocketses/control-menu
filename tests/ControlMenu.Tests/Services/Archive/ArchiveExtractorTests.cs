using System.Formats.Tar;
using System.IO.Compression;
using ControlMenu.Services.Archive;

namespace ControlMenu.Tests.Services.Archive;

public class ArchiveExtractorTests
{
    private static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly ArchiveExtractor _extractor = new();

    // --- .7z / BCJ ---

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
            var bytes = File.ReadAllBytes(extracted);
            Assert.Equal(0x4D, bytes[0]); // 'M'
            Assert.Equal(0x5A, bytes[1]); // 'Z'
        }
        finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }

    // --- .zip ---

    [Fact]
    public void Extract_Zip_ExtractsEntry()
    {
        var src = Path.Combine(Path.GetTempPath(), "cm-zip-src-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(Path.GetTempPath(), "cm-zip-" + Guid.NewGuid().ToString("N") + ".zip");
        var dest = Path.Combine(Path.GetTempPath(), "cm-zip-dest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "hello.txt"), "hello-zip");
            ZipFile.CreateFromDirectory(src, zipPath);

            _extractor.Extract(zipPath, dest);

            var extracted = Path.Combine(dest, "hello.txt");
            Assert.True(File.Exists(extracted), "hello.txt should have been extracted");
            Assert.Equal("hello-zip", File.ReadAllText(extracted));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
        }
    }

    // --- .tar.gz ---

    [Fact]
    public void Extract_TarGz_ExtractsEntry()
    {
        var src = Path.Combine(Path.GetTempPath(), "cm-tgz-src-" + Guid.NewGuid().ToString("N"));
        var tgzPath = Path.Combine(Path.GetTempPath(), "cm-tgz-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        var dest = Path.Combine(Path.GetTempPath(), "cm-tgz-dest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "hello.txt"), "hello-tgz");

            // Build .tar.gz inline: TarFile → GZipStream
            using (var tarStream = new MemoryStream())
            {
                TarFile.CreateFromDirectory(src, tarStream, includeBaseDirectory: false);
                tarStream.Position = 0;
                using var fs = File.Create(tgzPath);
                using var gz = new GZipStream(fs, CompressionMode.Compress);
                tarStream.CopyTo(gz);
            }

            _extractor.Extract(tgzPath, dest);

            var extracted = Path.Combine(dest, "hello.txt");
            Assert.True(File.Exists(extracted), "hello.txt should have been extracted");
            Assert.Equal("hello-tgz", File.ReadAllText(extracted));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (File.Exists(tgzPath)) File.Delete(tgzPath);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
        }
    }

    // --- zip-slip guard (IsWithinRoot) ---

    [Theory]
    [InlineData("sample.exe", true)]
    [InlineData("subdir/file.txt", true)]
    [InlineData("../evil.exe", false)]
    [InlineData("../../x", false)]
    public void IsWithinRoot_PathTraversal_Detected(string entryKey, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-root-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(expected, ArchiveExtractor.IsWithinRoot(root, entryKey));
    }

    // --- unsupported extension ---

    [Fact]
    public void Extract_UnsupportedExtension_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => _extractor.Extract("whatever.rar", "dest"));
    }

    // --- null guards ---

    [Fact]
    public void Extract_NullArchivePath_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _extractor.Extract(null!, "dest"));
    }

    [Fact]
    public void Extract_NullDestDir_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _extractor.Extract("whatever.zip", null!));
    }
}
