using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Jellyfin;

public class JellyfinModule : IToolModule
{
    public string Id => "jellyfin";
    public string DisplayName => "Jellyfin";
    public string Icon => "bi-film";
    public int SortOrder => 3;

    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "docker",
            ExecutableName = "docker",
            VersionCommand = "docker --version",
            VersionPattern = @"Docker version ([\d.]+)",
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://docs.docker.com/get-docker/",
            InstallPath = Path.Combine(DepsRoot, "docker"),
            VersionCheckUrl = "https://api.github.com/repos/moby/moby/releases/latest",
            VersionCheckPattern = @"""tag_name""\s*:\s*""docker-v(\d+\.\d+\.\d+)"""
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
            InstallPath = Path.Combine(DepsRoot, "sqlite3"),
            // T1: KnownHashes keyed by DependencyManagerService.ResolveTargetVersion output.
            // DirectUrl deps use the version-check format (major.minor.patch, no "v" prefix).
            // Pinned 2026-06-18; refresh via scripts/update-dependency-hashes.ps1.
            KnownHashes = new Dictionary<string, string>
            {
                ["3.53.2"] = "2eb7602bbe05895f4f530d8f3c4af244dcd8697d14b858778ccd4abe297a836d"
            },
            // T2: upstream SHA3-256 published on the sqlite download page.
            AllowedHosts = ["sqlite.org"],
            Checksum = new ControlMenu.Services.Verification.ChecksumSource(
                "https://www.sqlite.org/download.html",
                ControlMenu.Services.Verification.ChecksumFormat.SqliteDownloadPage,
                ControlMenu.Services.Verification.ChecksumAlgorithm.Sha3_256)
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
