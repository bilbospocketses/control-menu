using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Jellyfin;

public class JellyfinModule : IToolModule
{
    public string Id => "jellyfin";
    public string DisplayName => "Jellyfin";
    public string Icon => "bi-film";
    public int SortOrder => 2;

    private static readonly string DepsRoot = FindDepsRoot();

    private static string FindDepsRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "dependencies");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null) break;
            dir = parent;
        }
        var fallback = Path.Combine(AppContext.BaseDirectory, "dependencies");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "docker",
            ExecutableName = "docker",
            VersionCommand = "docker --version",
            VersionPattern = @"Docker version ([\d.]+)",
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://docs.docker.com/get-docker/"
        },
        new ModuleDependency
        {
            Name = "sqlite3",
            ExecutableName = "sqlite3",
            VersionCommand = "sqlite3 --version",
            VersionPattern = @"([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            DownloadUrl = OperatingSystem.IsWindows()
                ? "https://sqlite.org/2026/sqlite-tools-win-x64-3530000.zip"
                : "https://sqlite.org/2026/sqlite-tools-linux-x64-3530000.zip",
            VersionCheckUrl = "https://www.sqlite.org/download.html",
            VersionCheckPattern = @"version\s+(\d+\.\d+\.\d+)",
            ProjectHomeUrl = "https://www.sqlite.org/download.html",
            InstallPath = Path.Combine(DepsRoot, "sqlite3")
        }
    ];

    // All Jellyfin settings are managed in Settings > Jellyfin tab directly
    // SMTP/email settings are in Settings > General
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("DB Date Update", "/jellyfin/db-update", "🗃️", 0),
        new NavEntry("Cast & Crew", "/jellyfin/cast-crew", "🎭", 1),
        // Jellyfin settings are under main Settings > Jellyfin tab
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() =>
    [
        new BackgroundJobDefinition("cast-crew-update", "Cast & Crew Image Update",
            "Updates images for all cast members, directors, and producers in Jellyfin media libraries.",
            IsLongRunning: true)
    ];
}
