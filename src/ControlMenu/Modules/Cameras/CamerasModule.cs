using ControlMenu.Data.Enums;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

public class CamerasModule : IToolModule
{
    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

    public string Id => "cameras";
    public string DisplayName => "Cameras";
    public string Icon => "bi-camera-video";
    public int SortOrder => 5;

    /// <summary>
    /// Set by Program.cs on startup AND on CameraChangeNotifier.CamerasChanged.
    /// Used by GetNavEntries() which can't do async.
    /// </summary>
    public static List<(Guid Id, string Name)> EnabledCameras { get; set; } = new();

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "go2rtc",
            ExecutableName = "go2rtc.exe",
            VersionCommand = "go2rtc --version",
            VersionPattern = @"go2rtc\s+version\s+([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "AlexxIT/go2rtc",
            AssetPattern = @"go2rtc_win64\.zip",
            InstallPath = Path.Combine(DepsRoot, "go2rtc"),
            ProjectHomeUrl = "https://github.com/AlexxIT/go2rtc",
            // T1: KnownHashes keyed by DependencyManagerService.ResolveTargetVersion output.
            // GitHub deps strip the leading "v" from the tag (e.g. "1.9.14").
            // Pinned 2026-06-18; refresh via scripts/update-dependency-hashes.ps1.
            KnownHashes = new Dictionary<string, string>
            {
                ["1.9.14"] = "dd4167d75cb04abe618855b7c71f8658bd009f60c1a71835d134d2c11c939907"
            },
            // Tier 4: verified unsigned; no Checksum (T2), no ExpectedSigner (T3).
            AllowedHosts = ["github.com", "*.githubusercontent.com"]
        }
    ];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries()
    {
        var index = 0;
        foreach (var cam in EnabledCameras)
        {
            yield return new NavEntry(cam.Name, $"/cameras/{cam.Id:N}", "/images/cameras-logo.svg", index++);
        }
    }

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
