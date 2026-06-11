using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Imaging;

public class ImagingModule : IToolModule
{
    public string Id => "imaging";
    public string DisplayName => "Imaging Tools";
    public string Icon => "bi-image";

    // After Cameras (5); top-level. NOTE: the original plan said 5, but PR #39
    // renumbered Cameras 4 -> 5, so this module takes 6.
    public int SortOrder => 6;

    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "magick",
            ExecutableName = "magick",
            VersionCommand = "magick --version",
            VersionPattern = @"ImageMagick ([\d.]+-\d+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "ImageMagick/ImageMagick",
            ProjectHomeUrl = "https://imagemagick.org",
            // ImageMagick ships Windows portables as .7z on GitHub releases (the
            // imagemagick.org/archive .zip path is gone). Q8-x64 is the smallest
            // full-feature portable. e.g. ImageMagick-7.1.2-25-portable-Q8-x64.7z
            AssetPattern = @"ImageMagick-[\d.]+-\d+-portable-Q8-x64\.7z",
            InstallPath = Path.Combine(DepsRoot, "magick")
        },
        // vtracer: color raster -> SVG tracer (Phase G "Tracing"). GitHub source.
        // Release tags carry NO "v" prefix (0.6.4). The Windows zip extracts
        // vtracer.exe flat at the archive root. NOTE: --version prints
        // "visioncortex VTracer 0.6.4" (capital VTracer), so the pattern matches
        // "VTracer", not the lowercase executable name.
        new ModuleDependency
        {
            Name = "vtracer",
            ExecutableName = "vtracer",
            VersionCommand = "vtracer --version",
            VersionPattern = @"VTracer ([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "visioncortex/vtracer",
            ProjectHomeUrl = "https://github.com/visioncortex/vtracer",
            AssetPattern = @"vtracer-x86_64-pc-windows-msvc\.zip",
            InstallPath = Path.Combine(DepsRoot, "vtracer")
        },
        // potrace: B&W raster -> SVG tracer (Phase G "Tracing"). DirectUrl from
        // upstream SourceForge (no GitHub mirror). potrace 1.16 is stable/pinned,
        // so no version-check URL. The zip extracts to a nested
        // potrace-1.16.win64/ dir containing potrace.exe (+ mkbitmap.exe, docs);
        // the fetcher stages that subfolder so potrace.exe lands at the leaf root.
        new ModuleDependency
        {
            Name = "potrace",
            ExecutableName = "potrace",
            VersionCommand = "potrace --version",
            VersionPattern = @"potrace ([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            ProjectHomeUrl = "https://potrace.sourceforge.net/",
            DownloadUrl = "https://potrace.sourceforge.net/download/1.16/potrace-1.16.win64.zip",
            InstallPath = Path.Combine(DepsRoot, "potrace")
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Icon Converter", "/imaging/icon-converter", "🖼️", 0),
        new NavEntry("Format Converter", "/imaging/format-converter", "🔁", 1),
        new NavEntry("Image Resize", "/imaging/image-resize", "📐", 2),
        new NavEntry("SVG Rasterize", "/imaging/svg-rasterize", "🖌️", 3),
        new NavEntry("Magic Wand", "/imaging/magic-wand", "🪄", 4),
        new NavEntry("Tracing", "/imaging/tracing", "✏️", 5)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
