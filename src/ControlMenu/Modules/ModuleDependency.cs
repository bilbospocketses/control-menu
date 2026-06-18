using ControlMenu.Data.Enums;
using ControlMenu.Services.Verification;

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
    /// <summary>
    /// Download URL with {version} placeholder, e.g. "https://nodejs.org/dist/v{version}/node-v{version}-win-x64.zip".
    /// Resolved during version check when the latest version is discovered.
    /// </summary>
    public string? DownloadUrlTemplate { get; init; }
    /// <summary>Vetted SHA-256 hashes keyed by version string (T1, the backbone).</summary>
    public IReadOnlyDictionary<string, string> KnownHashes { get; init; } =
        new Dictionary<string, string>();
    /// <summary>Upstream-published checksum source (T2), or null.</summary>
    public ChecksumSource? Checksum { get; init; }
    /// <summary>Expected Authenticode subject, e.g. "CN=Google LLC" (T3), or null.</summary>
    public string? ExpectedSigner { get; init; }
    /// <summary>Permitted final download hosts (transport hard gate). Empty = unconstrained (avoid).</summary>
    public string[] AllowedHosts { get; init; } = [];
}
