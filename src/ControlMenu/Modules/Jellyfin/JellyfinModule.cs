using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Jellyfin;

public class JellyfinModule : IToolModule
{
    public string Id => "jellyfin";
    public string DisplayName => "Jellyfin";
    public string Icon => "bi-film";
    public int SortOrder => 2;

    private static string DepsRoot => FindDepsRoot();

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
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://www.sqlite.org/download.html",
            InstallPath = Path.Combine(DepsRoot, "sqlite3")
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements =>
    [
        new ConfigRequirement("jellyfin-compose-path", "Docker Compose Path", "Path to Jellyfin docker-compose.yml"),
        new ConfigRequirement("jellyfin-api-key", "Jellyfin API Key", "API key for Jellyfin REST API", IsSecret: true),
        new ConfigRequirement("jellyfin-base-url", "Jellyfin URL", "Base URL for Jellyfin API", DefaultValue: "http://127.0.0.1:8096"),
        new ConfigRequirement("jellyfin-user-id", "User ID", "Jellyfin user ID for API calls"),
        new ConfigRequirement("jellyfin-backup-retention-days", "Backup Retention (days)", "Days to keep database backups", DefaultValue: "5"),
        new ConfigRequirement("smtp-server", "SMTP Server", "SMTP server for notifications", DefaultValue: "mail.smtp2go.com"),
        new ConfigRequirement("smtp-port", "SMTP Port", "SMTP server port", DefaultValue: "587"),
        new ConfigRequirement("smtp-username", "SMTP Username", "SMTP login username"),
        new ConfigRequirement("smtp-password", "SMTP Password", "SMTP login password", IsSecret: true),
        new ConfigRequirement("notification-email", "Notification Email", "Email for completion alerts")
    ];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("DB Date Update", "/jellyfin/db-update", "bi-calendar-date", 0),
        new NavEntry("Cast & Crew", "/jellyfin/cast-crew", "bi-people", 1),
        // Jellyfin settings are under main Settings > Jellyfin tab
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() =>
    [
        new BackgroundJobDefinition("cast-crew-update", "Cast & Crew Image Update",
            "Updates images for all cast members, directors, and producers in Jellyfin media libraries.",
            IsLongRunning: true)
    ];
}
