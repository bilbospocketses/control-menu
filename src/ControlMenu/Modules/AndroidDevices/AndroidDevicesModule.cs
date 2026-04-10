using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.AndroidDevices;

public class AndroidDevicesModule : IToolModule
{
    public string Id => "android-devices";
    public string DisplayName => "Android Devices";
    public string Icon => "bi-phone";
    public int SortOrder => 1;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "adb",
            ExecutableName = "adb",
            VersionCommand = "adb --version",
            VersionPattern = @"Android Debug Bridge version ([\d.]+)",
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://developer.android.com/tools/releases/platform-tools"
        },
        new ModuleDependency
        {
            Name = "scrcpy",
            ExecutableName = "scrcpy",
            VersionCommand = "scrcpy --version",
            VersionPattern = @"scrcpy ([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "Genymobile/scrcpy",
            AssetPattern = @"scrcpy-win64-v[\d.]+\.zip"
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Device List", "/android/devices", "bi-list-ul", 0),
        new NavEntry("Google TV", "/android/googletv", "bi-tv", 1),
        new NavEntry("Pixel", "/android/pixel", "bi-phone", 2)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
