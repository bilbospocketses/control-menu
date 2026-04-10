using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Jellyfin;

public class JellyfinModule : IToolModule
{
    public string Id => "jellyfin";
    public string DisplayName => "Jellyfin";
    public string Icon => "bi-film";
    public int SortOrder => 2;

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
            ProjectHomeUrl = "https://www.sqlite.org/download.html"
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements =>
    [
        new ConfigRequirement("jellyfin-api-key", "Jellyfin API Key", "API key for Jellyfin REST API", IsSecret: true),
        new ConfigRequirement("jellyfin-db-path", "Database Path", "Path to jellyfin.db", DefaultValue: "D:/DockerData/jellyfin/config/data/jellyfin.db"),
        new ConfigRequirement("jellyfin-container-name", "Container Name", "Docker container name", DefaultValue: "jellyfin"),
        new ConfigRequirement("jellyfin-backup-dir", "Backup Directory", "Path for database backups", DefaultValue: "C:/scripts/tools-menu/jellyfin-db-bkup-and-logs"),
        new ConfigRequirement("jellyfin-base-url", "Jellyfin URL", "Base URL for Jellyfin API", DefaultValue: "http://127.0.0.1:8096"),
        new ConfigRequirement("jellyfin-user-id", "User ID", "Jellyfin user ID for API calls"),
        new ConfigRequirement("smtp-server", "SMTP Server", "SMTP server for notifications", DefaultValue: "mail.smtp2go.com"),
        new ConfigRequirement("smtp-port", "SMTP Port", "SMTP server port", DefaultValue: "587"),
        new ConfigRequirement("smtp-username", "SMTP Username", "SMTP login username"),
        new ConfigRequirement("smtp-password", "SMTP Password", "SMTP login password", IsSecret: true),
        new ConfigRequirement("notification-email", "Notification Email", "Email for completion alerts")
    ];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("DB Date Update", "/jellyfin/db-update", "bi-calendar-date", 0),
        new NavEntry("Cast & Crew", "/jellyfin/cast-crew", "bi-people", 1)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() =>
    [
        new BackgroundJobDefinition("cast-crew-update", "Cast & Crew Image Update",
            "Updates images for all cast members, directors, and producers in Jellyfin media libraries.",
            IsLongRunning: true)
    ];
}
