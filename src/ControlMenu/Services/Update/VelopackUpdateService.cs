using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace ControlMenu.Services.Update;

/// <summary>
/// Provides update-check, download, and apply-signaling for Control Menu via Velopack.
///
/// API surface verified against Velopack 1.1.1 (clean build + 445 tests, 2026-06-03):
/// - UpdateManager.IsInstalled: bool property (not a method)
/// - UpdateManager.CurrentVersion: SemanticVersion? property (null when not installed)
/// - UpdateManager.CheckForUpdatesAsync(): Task&lt;UpdateInfo?&gt; — null means no update
/// - UpdateManager.DownloadUpdatesAsync(UpdateInfo, Action&lt;int&gt;?, CancellationToken): Task
///   (CT is a named parameter, not .WaitAsync())
/// - UpdateInfo.TargetFullRelease.Version: SemanticVersion (path confirmed via docs examples)
/// - GithubSource(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? = null)
///
/// Apply strategy (Phase 1): instead of mgr.ApplyUpdatesAndExit(), we set
/// Environment.ExitCode = 75 and call IHostApplicationLifetime.StopApplication().
/// The launcher's ChildSupervisor.ExitCodeApplyUpdate constant (= 75) matches.
/// Phase 3 will wire the full Velopack apply orchestration in the launcher.
/// The constant is hardcoded here (not imported from ControlMenuLauncher) because
/// ControlMenu.csproj does NOT reference ControlMenuLauncher.csproj.
/// </summary>
public sealed class VelopackUpdateService : IVelopackUpdateService
{
    private const string GitHubRepo = "https://github.com/bilbospocketses/control-menu";

    /// <summary>
    /// Exit-75 contract: must stay in sync with ControlMenu.Launcher.Supervisor.ChildSupervisor.ExitCodeApplyUpdate.
    /// Cannot import it directly — ControlMenu does not reference ControlMenuLauncher.
    /// </summary>
    public const int ExitCodeApplyUpdate = 75;

    private readonly UpdateManager _manager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly UpdateApplyState _applyState;
    private readonly ILogger<VelopackUpdateService> _log;
    private UpdateInfo? _pending;

    public VelopackUpdateService(IHostApplicationLifetime lifetime, UpdateApplyState applyState, ILogger<VelopackUpdateService> log)
    {
        _lifetime = lifetime;
        _applyState = applyState;
        _log = log;
        // Pin the update channel to "stable" — the channel release.yml packs non-prerelease tags
        // into (`vpk pack --channel stable`; -beta/-alpha tags go to the "beta" channel). Without an
        // explicit channel the updater falls back to Velopack's RID-derived default, which would not
        // reliably match the published "stable" channel. (Publisher/signature pinning beyond the
        // channel is deferred to the Velopack Phase-2 signing initiative.)
        _manager = new UpdateManager(
            new GithubSource(GitHubRepo, accessToken: null, prerelease: false),
            new UpdateOptions { ExplicitChannel = "stable" });
    }

    public async Task<UpdateAvailability> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
        {
            _log.LogInformation("Velopack not installed (running from dev tree); update check skipped");
            return new UpdateAvailability(false, null, null);
        }

        try
        {
            // CheckForUpdatesAsync has no CancellationToken overload; use WaitAsync for cancellation.
            _pending = await _manager.CheckForUpdatesAsync().WaitAsync(ct);
            var current = _manager.CurrentVersion?.ToString();
            if (_pending is null) return new UpdateAvailability(false, null, current);
            return new UpdateAvailability(true, _pending.TargetFullRelease.Version.ToString(), current);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CheckForUpdatesAsync failed");
            return new UpdateAvailability(false, null, _manager.CurrentVersion?.ToString());
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_pending is null)
            throw new InvalidOperationException("No pending update; call CheckForUpdatesAsync first");

        // DownloadUpdatesAsync accepts cancelToken as a named parameter directly.
        await _manager.DownloadUpdatesAsync(_pending, cancelToken: ct);
    }

    public void RequestApplyUpdate()
    {
        if (_pending is null)
            throw new InvalidOperationException("No downloaded update to apply");

        _log.LogInformation("Requesting Velopack apply via exit-{ExitCode}", ExitCodeApplyUpdate);
        _applyState.ApplyRequested = true; // Program returns ExitCodeApplyUpdate after RunAsync
        _lifetime.StopApplication();
    }
}
