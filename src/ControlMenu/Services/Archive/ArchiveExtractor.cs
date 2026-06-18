using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ControlMenu.Services.Archive;

public sealed class ArchiveExtractor : IArchiveExtractor
{
    public void Extract(string archivePath, string destDir)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(destDir);

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
            using var archive = SevenZipArchive.OpenArchive(archivePath, new ReaderOptions());
            var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Key is null)
                    continue;
                if (!IsWithinRoot(destDir, entry.Key))
                    throw new InvalidOperationException(
                        $"Archive entry '{entry.Key}' would extract outside the destination directory.");
                entry.WriteToDirectory(destDir, opts);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported archive type: {archivePath}");
        }
    }

    /// <summary>Returns true when <paramref name="entryKey"/> resolves to a path that is a
    /// descendant of <paramref name="destDir"/>; guards against zip-slip attacks.</summary>
    internal static bool IsWithinRoot(string destDir, string entryKey)
    {
        var root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(destDir, entryKey));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
