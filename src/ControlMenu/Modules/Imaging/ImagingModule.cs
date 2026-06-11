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
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        // Pages added in later phases. Phase A leaves this empty so the module
        // registers cleanly and magick shows up in Settings -> Dependencies
        // before any tool pages exist.
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
