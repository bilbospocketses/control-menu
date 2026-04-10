using ControlMenu.Data.Enums;

namespace ControlMenu.Modules;

public record ModuleDependency
{
    public required string Name { get; init; }
    public required string ExecutableName { get; init; }
    public required string VersionCommand { get; init; }
    public required string VersionPattern { get; init; }
    public UpdateSourceType SourceType { get; init; }
    public string? GitHubRepo { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ProjectHomeUrl { get; init; }
    public string? AssetPattern { get; init; }
    public string? InstallPath { get; init; }
    public string[] RelatedFiles { get; init; } = [];
    public string? VersionCheckUrl { get; init; }
    public string? VersionCheckPattern { get; init; }
}
