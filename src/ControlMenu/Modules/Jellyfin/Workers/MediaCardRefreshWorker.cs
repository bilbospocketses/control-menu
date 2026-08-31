using System.Text.Json;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Workers;

/// <summary>
/// Regenerates the My Media cards for the selected libraries: back up the current card, delete it,
/// ask Jellyfin to rebuild it, then wait until the new one actually exists.
/// </summary>
public sealed class MediaCardRefreshWorker
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultCardTimeout = TimeSpan.FromMinutes(3);

    private readonly IJellyfinService _jellyfin;
    private readonly IBackgroundJobService _jobService;
    private readonly IEmailService _email;
    private readonly IConfigurationService _config;
    private readonly OperationLogger? _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _cardTimeout;

    public MediaCardRefreshWorker(IJellyfinService jellyfinService, IBackgroundJobService jobService,
        IEmailService emailService, IConfigurationService configService, OperationLogger? logger = null,
        TimeSpan? pollInterval = null, TimeSpan? cardTimeout = null)
    {
        _jellyfin = jellyfinService;
        _jobService = jobService;
        _email = emailService;
        _config = configService;
        _logger = logger;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _cardTimeout = cardTimeout ?? DefaultCardTimeout;
    }

    public async Task ExecuteAsync(Guid jobId, IReadOnlyList<string> libraryIds, CancellationToken cancellationToken)
    {
        try
        {
            _logger?.Step("Resolving Jellyfin API configuration");
            var apiConfig = await _jellyfin.GetApiConfigAsync();
            _logger?.Ok($"API: {apiConfig.BaseUrl}");

            var targets = await SafeGetTargetsAsync(apiConfig, cancellationToken);
            string NameOf(string id) => targets.FirstOrDefault(t => t.Id == id)?.Name ?? id;

            var results = new List<MediaCardResult>();
            var cancelled = false;

            for (var i = 0; i < libraryIds.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                // Cancellation is read from the database each library, so it survives a circuit
                // disconnect or a page navigation the same way the cast & crew job does.
                var checkJob = await _jobService.GetJobAsync(jobId);
                if (checkJob?.CancellationRequested == true)
                {
                    cancelled = true;
                    break;
                }

                var libraryId = libraryIds[i];
                var name = NameOf(libraryId);

                await _jobService.UpdateProgressAsync(jobId,
                    (int)((double)i / libraryIds.Count * 100),
                    $"Regenerating {name} ({i + 1} of {libraryIds.Count})");

                var target = targets.FirstOrDefault(t => t.Id == libraryId);
                results.Add(await RegenerateOneAsync(libraryId, name, target, apiConfig, cancellationToken));
            }

            var regenerated = results.Count(r => r.Regenerated);
            var failed = results.Count(r => !r.Regenerated);
            var summary = $"{regenerated} regenerated, {failed} failed, out of {libraryIds.Count} selected";

            var resultData = JsonSerializer.Serialize(new
            {
                Total = libraryIds.Count,
                Regenerated = regenerated,
                Failed = failed,
                Libraries = results
            });

            if (cancelled)
            {
                var cancelMsg = $"Cancelled after {results.Count} of {libraryIds.Count} libraries. {summary}";
                _logger?.Fail(cancelMsg);
                await _jobService.FailJobAsync(jobId, cancelMsg, resultData);
                await SendNotificationAsync("Cancelled", cancelMsg, results);
            }
            else if (failed > 0)
            {
                _logger?.Fail(summary);
                await _jobService.FailJobAsync(jobId, summary, resultData);
                await SendNotificationAsync("Completed with failures", summary, results);
            }
            else
            {
                await _jobService.UpdateProgressAsync(jobId, 100, "All cards regenerated");
                _logger?.Done($"Completed: {summary}");
                await _jobService.CompleteJobAsync(jobId, resultData);
                await SendNotificationAsync("Completed", summary, results);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.Fail("Cancelled by token");
            try
            {
                await _jobService.FailJobAsync(jobId, "Cancelled.");
            }
            catch { /* best effort -- the scope may already be disposed */ }
        }
        catch (Exception ex)
        {
            _logger?.Fail($"Unexpected error: {ex.Message}");
            await _jobService.FailJobAsync(jobId, ex.Message);
        }
    }

    private async Task<MediaCardResult> RegenerateOneAsync(string libraryId, string name,
        MediaCardTarget? target, JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        // Last line of defence for the irreversible case. The page disables these, but a stale page
        // could still submit one, and a deleted Live TV card cannot be rebuilt by Jellyfin at all.
        // A target we could not look up is allowed through -- the page already validated it.
        if (target is { CanRegenerate: false })
        {
            var reason = target.BlockedReason ?? "Jellyfin cannot generate this card";
            _logger?.Fail($"{name}: refused -- {reason}");
            return new MediaCardResult(libraryId, name, false, null, reason);
        }

        string? backupPath;
        try
        {
            _logger?.Step($"Backing up the {name} card");
            backupPath = await _jellyfin.BackupLibraryCardAsync(libraryId, name, apiConfig, ct);
            _logger?.Ok(backupPath is null
                ? $"{name}: no existing card to back up"
                : $"{name}: backed up to {backupPath}");
        }
        catch (Exception ex)
        {
            // Deleting a card we could not back up destroys a hand-made card with no way back.
            var message = $"Backup failed, card left untouched: {ex.Message}";
            _logger?.Fail($"{name}: {message}");
            return new MediaCardResult(libraryId, name, false, null, message);
        }

        try
        {
            await _jellyfin.DeleteLibraryCardAsync(libraryId, apiConfig, ct);
            await _jellyfin.RefreshLibraryCardAsync(libraryId, apiConfig, ct);
        }
        catch (Exception ex)
        {
            _logger?.Fail($"{name}: refresh request failed: {ex.Message}");
            return new MediaCardResult(libraryId, name, false, backupPath, ex.Message);
        }

        var reappeared = await WaitForCardAsync(libraryId, apiConfig, ct);
        if (reappeared)
        {
            _logger?.Ok($"{name}: new card generated");
            return new MediaCardResult(libraryId, name, true, backupPath, null);
        }

        var timeoutMessage = $"No new card after {_cardTimeout.TotalSeconds:N0}s"
                             + (backupPath is null ? "" : $" -- restore from {backupPath}");
        _logger?.Fail($"{name}: {timeoutMessage}");
        return new MediaCardResult(libraryId, name, false, backupPath, timeoutMessage);
    }

    /// <summary>
    /// The refresh is queued server-side, so the POST returning says nothing about the card. Poll
    /// until it exists -- we deleted it first, so anything present is the new one.
    /// </summary>
    private async Task<bool> WaitForCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _cardTimeout;
        do
        {
            if (ct.IsCancellationRequested) return false;

            try
            {
                if (await _jellyfin.HasLibraryCardAsync(libraryId, apiConfig, ct)) return true;
            }
            catch (Exception ex)
            {
                _logger?.Step($"Card check failed, still waiting: {ex.Message}");
            }

            await Task.Delay(_pollInterval, ct);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private async Task<IReadOnlyList<MediaCardTarget>> SafeGetTargetsAsync(JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        try
        {
            return await _jellyfin.GetMediaCardTargetsAsync(apiConfig, ct) ?? [];
        }
        catch (Exception ex)
        {
            // The job runs off the ids the page selected; this list supplies names and the
            // can-regenerate guard, both of which degrade gracefully when it is unavailable.
            _logger?.Step($"Could not read the My Media row: {ex.Message}");
            return [];
        }
    }

    private async Task SendNotificationAsync(string status, string details, IReadOnlyList<MediaCardResult> results)
    {
        try
        {
            var to = await _config.GetSettingAsync("notification-email");
            if (string.IsNullOrEmpty(to)) return;

            var lines = results.Select(r => r.Regenerated
                ? $"  OK      {r.LibraryName}"
                : $"  FAILED  {r.LibraryName} -- {r.Error}");

            var body = $"Jellyfin My Media card regeneration has {status.ToLowerInvariant()}.\n\n{details}\n\n"
                       + string.Join("\n", lines);

            await _email.SendAsync(to, $"Media Card Refresh -- {status}", body);
        }
        catch
        {
            // Best effort -- don't fail the job over a notification.
        }
    }
}
