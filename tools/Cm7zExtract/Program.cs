// Build-time .7z extractor using the bundled SharpCompress library — replaces the previously
// vendored 7zr.exe. Mirrors src/ControlMenu/Services/Archive/ArchiveExtractor.cs's .7z path so
// build-time and runtime extraction behave identically (same library, same zip-slip guard).
// Invoked by scripts/dependencies/_Fetcher.ps1 (Expand-Cm7z): `Cm7zExtract <archive.7z> <destDir>`.
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Cm7zExtract <archive.7z> <destDir>");
    return 2;
}

var archivePath = args[0];
var destDir = args[1];

try
{
    Directory.CreateDirectory(destDir);
    var root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;

    using var archive = SevenZipArchive.OpenArchive(archivePath, new ReaderOptions());
    var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
    foreach (var entry in archive.Entries)
    {
        if (entry.IsDirectory || entry.Key is null)
            continue;

        // Zip-slip guard — refuse any entry that resolves outside the destination root.
        var full = Path.GetFullPath(Path.Combine(destDir, entry.Key));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"refusing zip-slip entry '{entry.Key}' (escapes {destDir})");
            return 3;
        }

        entry.WriteToDirectory(destDir, opts);
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"7z extraction failed for '{archivePath}': {ex.Message}");
    return 1;
}
