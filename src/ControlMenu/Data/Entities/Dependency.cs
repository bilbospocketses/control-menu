using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Dependency
{
    public Guid Id { get; set; }
    public required string ModuleId { get; set; }
    public required string Name { get; set; }
    public string? InstalledVersion { get; set; }
    public string? LatestKnownVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ProjectHomeUrl { get; set; }
    public DateTime? LastChecked { get; set; }
    public DependencyStatus Status { get; set; }
    public UpdateSourceType SourceType { get; set; }
}
