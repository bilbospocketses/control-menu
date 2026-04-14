using ControlMenu.Data.Enums;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

public class CamerasModule : IToolModule
{
    public string Id => "cameras";
    public string DisplayName => "Cameras";
    public string Icon => "bi-camera-video";
    public int SortOrder => 4;

    /// <summary>
    /// Set by Program.cs on startup from the camera-count setting.
    /// Used by GetNavEntries() which can't do async.
    /// </summary>
    public static int CameraCount { get; set; } = ICameraService.DefaultCameraCount;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "go2rtc",
            ExecutableName = "go2rtc.exe",
            VersionCommand = "go2rtc --version",
            VersionPattern = @"go2rtc\s+([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "AlexxIT/go2rtc",
            AssetPattern = @"go2rtc_win64\.zip",
            InstallPath = "dependencies/go2rtc",
            ProjectHomeUrl = "https://github.com/AlexxIT/go2rtc"
        }
    ];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries()
    {
        for (var i = 1; i <= CameraCount; i++)
            yield return new NavEntry($"Camera {i}", $"/cameras/{i}", "📷", i);
    }

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
