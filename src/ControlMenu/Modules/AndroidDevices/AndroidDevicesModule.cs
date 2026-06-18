using ControlMenu.Data.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ControlMenu.Modules.AndroidDevices;

public class AndroidDevicesModule : IToolModule
{
    public string Id => "android-devices";
    public string DisplayName => "Android Devices";
    public string Icon => "bi-phone";
    public int SortOrder => 1;

    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

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
            InstallPath = Path.Combine(DepsRoot, "platform-tools"),
            // T1: KnownHashes keyed by DependencyManagerService.ResolveTargetVersion output.
            // DirectUrl deps use the version-check format (major.minor.micro, no "v" prefix).
            // Pinned 2026-06-18; refresh via scripts/update-dependency-hashes.ps1.
            KnownHashes = new Dictionary<string, string>
            {
                ["37.0.0"] = "4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918"
            },
            // T3: Authenticode signer pin. No T2 checksum — Google's only upstream hash is weak SHA-1, deliberately omitted.
            AllowedHosts = ["dl.google.com"],
            ExpectedSigner = "CN=Google LLC"
        },
        // node was a CM dependency when Managed-mode spawned the ws-scrcpy-web
        // node process. External-mode-only refactor removed all process-spawn
        // code; node is no longer invoked anywhere in CM. Declaration removed
        // 2026-05-07. ws-scrcpy-web declaration below still references node as
        // its ExecutableName for legacy version-check shape; that whole entry
        // is rewritten under TODO #12 (GitHub-source / Velopack-bundled).
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
        new NavEntry("Device List",    "/android/devices",  "/images/devices/device-list.svg", 0),
        new NavEntry("Google TV",      "/android/googletv", "/images/devices/smart-tv.svg",    1, HasDevicesOfType(DeviceType.GoogleTV)),
        new NavEntry("Android Phone",  "/android/phone",    "/images/devices/smart-phone.svg", 2, HasDevicesOfType(DeviceType.AndroidPhone)),
        new NavEntry("Android Tablet", "/android/tablet",   "/images/devices/tablet.svg",      3, HasDevicesOfType(DeviceType.AndroidTablet)),
        new NavEntry("Android Watch",  "/android/watch",    "/images/devices/smart-watch.svg", 4, HasDevicesOfType(DeviceType.AndroidWatch)),
    ];

    private static Func<IServiceProvider, bool> HasDevicesOfType(DeviceType type) =>
        sp => sp.GetRequiredService<ControlMenu.Services.IDeviceTypeCache>().HasDevicesOfType(type);

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
