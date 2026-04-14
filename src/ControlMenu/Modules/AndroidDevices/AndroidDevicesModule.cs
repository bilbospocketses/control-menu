using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.AndroidDevices;

public class AndroidDevicesModule : IToolModule
{
    public string Id => "android-devices";
    public string DisplayName => "Android Devices";
    public string Icon => "bi-phone";
    public int SortOrder => 1;

    private static readonly string DepsRoot = FindDepsRoot();

    private static string FindDepsRoot()
    {
        // Check content root first (dev: project dir), then base dir (published)
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "dependencies");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null) break;
            dir = parent;
        }
        // Fallback: create at base directory
        var fallback = Path.Combine(AppContext.BaseDirectory, "dependencies");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "adb",
            ExecutableName = "adb",
            VersionCommand = "adb --version",
            VersionPattern = @"Version ([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            ProjectHomeUrl = "https://developer.android.com/tools/releases/platform-tools",
            DownloadUrl = OperatingSystem.IsWindows()
                ? "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"
                : "https://dl.google.com/android/repository/platform-tools-latest-linux.zip",
            VersionCheckUrl = "https://dl.google.com/android/repository/repository2-3.xml",
            VersionCheckPattern = @"path=""platform-tools"".*?<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>",
            DownloadUrlTemplate = OperatingSystem.IsWindows()
                ? "https://dl.google.com/android/repository/platform-tools_r{version}-win.zip"
                : "https://dl.google.com/android/repository/platform-tools_r{version}-linux.zip",
            InstallPath = Path.Combine(DepsRoot, "platform-tools")
        },
        new ModuleDependency
        {
            Name = "scrcpy",
            ExecutableName = "scrcpy",
            VersionCommand = "scrcpy --version",
            VersionPattern = @"scrcpy ([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "Genymobile/scrcpy",
            ProjectHomeUrl = "https://github.com/Genymobile/scrcpy",
            AssetPattern = @"scrcpy-win64-v[\d.]+\.zip",
            InstallPath = Path.Combine(DepsRoot, "scrcpy")
        },
        new ModuleDependency
        {
            Name = "node",
            ExecutableName = "node",
            VersionCommand = "node --version",
            VersionPattern = @"v([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            ProjectHomeUrl = "https://nodejs.org/",
            DownloadUrl = "https://nodejs.org/en/download/",
            VersionCheckUrl = "https://nodejs.org/dist/latest-v22.x/",
            VersionCheckPattern = @"node-v(\d+\.\d+\.\d+)",
            DownloadUrlTemplate = OperatingSystem.IsWindows()
                ? "https://nodejs.org/dist/v{version}/node-v{version}-win-x64.zip"
                : "https://nodejs.org/dist/v{version}/node-v{version}-linux-x64.tar.gz",
            InstallPath = Path.Combine(DepsRoot, "node")
        },
        new ModuleDependency
        {
            Name = "ws-scrcpy-web",
            ExecutableName = "node",
            VersionCommand = "node -e \"console.log('installed')\"",
            VersionPattern = @"(installed)",
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://github.com/bilbospocketses/ws-scrcpy-web"
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Device List", "/android/devices", "📋", 0),
        new NavEntry("Google TV", "/android/googletv", "📺", 1),
        new NavEntry("Pixel", "/android/pixel", "📱", 2)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
