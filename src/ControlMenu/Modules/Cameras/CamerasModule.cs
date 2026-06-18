using ControlMenu.Data.Enums;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Modules.Cameras;

public class CamerasModule : IToolModule
{
    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

    public string Id => "cameras";
    public string DisplayName => "Cameras";
    public string Icon => "bi-camera-video";
    public int SortOrder => 5;

    /// <summary>
    /// Set by Program.cs on startup AND on CameraChangeNotifier.CamerasChanged.
    /// Used by GetNavEntries() which can't do async.
    /// </summary>
    public static List<(Guid Id, string Name)> EnabledCameras { get; set; } = new();

    /// <summary>Projects the enabled cameras to their (Id, Name) sidebar-nav entries.</summary>
    public static List<(Guid Id, string Name)> ProjectEnabledNav(IEnumerable<Camera> cameras)
        => cameras.Where(c => c.Enabled).Select(c => (c.Id, c.Name)).ToList();

    /// <summary>
    /// Refreshes <see cref="EnabledCameras"/> from a fresh DI scope. Exception-safe: this runs as a
    /// fire-and-forget worker off the camera-change notifier's thread, where an escaping exception
    /// has no owner and would fault the notifying caller (#21).
    /// </summary>
    public static async Task RefreshEnabledNavAsync(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
            EnabledCameras = ProjectEnabledNav(await cameraService.GetAllAsync());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh camera sidebar nav after a camera change");
        }
    }

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "go2rtc",
            ExecutableName = "go2rtc.exe",
            VersionCommand = "go2rtc --version",
            VersionPattern = @"go2rtc\s+version\s+([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "AlexxIT/go2rtc",
            AssetPattern = @"go2rtc_win64\.zip",
            InstallPath = Path.Combine(DepsRoot, "go2rtc"),
            ProjectHomeUrl = "https://github.com/AlexxIT/go2rtc",
            // T1: KnownHashes keyed by DependencyManagerService.ResolveTargetVersion output.
            // GitHub deps strip the leading "v" from the tag (e.g. "1.9.14").
            // Pinned 2026-06-18; refresh via scripts/update-dependency-hashes.ps1.
            KnownHashes = new Dictionary<string, string>
            {
                ["1.9.14"] = "dd4167d75cb04abe618855b7c71f8658bd009f60c1a71835d134d2c11c939907"
            },
            // Tier 4: verified unsigned; no Checksum (T2), no ExpectedSigner (T3).
            AllowedHosts = ["github.com", "*.githubusercontent.com"]
        }
    ];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries()
    {
        var index = 0;
        foreach (var cam in EnabledCameras)
        {
            yield return new NavEntry(cam.Name, $"/cameras/{cam.Id:N}", "/images/cameras-logo.svg", index++);
        }
    }

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
