using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace ControlMenu.Services.Archive;

public sealed class ArchiveExtractor : IArchiveExtractor
{
    public void Extract(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(archivePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, destDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = SevenZipArchive.Open(archivePath);
            var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
            foreach (var entry in archive.Entries)
                if (!entry.IsDirectory)
                    entry.WriteToDirectory(destDir, opts);
        }
        else
        {
            throw new NotSupportedException($"Unsupported archive type: {archivePath}");
        }
    }
}
