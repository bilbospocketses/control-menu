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
                results.Add(await RegenerateOneAsync(jobId, libraryId, name, target, apiConfig, cancellationToken));
            }

            var regenerated = results.Count(r => r.Regenerated);
            var failed = results.Count(r => !r.Regenerated);
            var restored = results.Count(r => r.Restored);
            var summary = $"{regenerated} regenerated, {failed} failed, out of {libraryIds.Count} selected"
                          + (restored > 0 ? $" ({restored} rolled back to the previous card)" : "");

            var resultData = JsonSerializer.Serialize(new
            {
                Total = libraryIds.Count,
                Regenerated = regenerated,
                Failed = failed,
                Restored = restored,
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

    private async Task<MediaCardResult> RegenerateOneAsync(Guid jobId, string libraryId, string name,
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
            return await RollBackAsync(libraryId, name, backupPath, ex.Message, apiConfig, ct);
        }

        var reappeared = await WaitForCardAsync(jobId, libraryId, apiConfig, ct);
        if (reappeared)
        {
            _logger?.Ok($"{name}: new card generated");
            return new MediaCardResult(libraryId, name, true, backupPath, null);
        }

        return await RollBackAsync(libraryId, name, backupPath,
            $"No new card after {_cardTimeout.TotalSeconds:N0}s", apiConfig, ct);
    }

    /// <summary>
    /// Puts the backup back when regeneration fails, so a tile is never left blank. Without this a
    /// failed run leaves the card deleted and the only copy sitting in a folder the user has to
    /// find and re-upload by hand -- which is exactly what happened on 2026-08-30.
    /// </summary>
    private async Task<MediaCardResult> RollBackAsync(string libraryId, string name, string? backupPath,
        string failure, JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        if (backupPath is null)
        {
            // There was no card to begin with, so there is nothing to put back.
            _logger?.Fail($"{name}: {failure}");
            return new MediaCardResult(libraryId, name, false, null, failure);
        }

        try
        {
            await _jellyfin.RestoreLibraryCardAsync(libraryId, backupPath, apiConfig, ct);
            _logger?.Ok($"{name}: {failure} -- previous card restored");
            return new MediaCardResult(libraryId, name, false, backupPath, failure, Restored: true);
        }
        catch (Exception ex)
        {
            var message = $"{failure}; restoring the backup ALSO failed ({ex.Message}). "
                          + $"The previous card is at {backupPath}";
            _logger?.Fail($"{name}: {message}");
            return new MediaCardResult(libraryId, name, false, backupPath, message);
        }
    }

    /// <summary>
    /// The refresh is queued server-side, so the POST returning says nothing about the card. Poll
    /// until it exists -- we deleted it first, so anything present is the new one.
    /// </summary>
    private async Task<bool> WaitForCardAsync(Guid jobId, string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _cardTimeout;
        do
        {
            if (ct.IsCancellationRequested) return false;

            // Cancellation is read from the database on EVERY poll, not just between libraries.
            // Checking only between them meant a Cancel click during a 3-minute wait did nothing
            // visible, and the next library was backed up and deleted before the flag was seen.
            if (await IsCancellationRequestedAsync(jobId)) return false;

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

    private async Task<bool> IsCancellationRequestedAsync(Guid jobId)
    {
        try
        {
            return (await _jobService.GetJobAsync(jobId))?.CancellationRequested == true;
        }
        catch
        {
            // A failed status read must not abort a job that is otherwise fine.
            return false;
        }
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

            var lines = results.Select(r => (r.Regenerated, r.Restored) switch
            {
                (true, _) => $"  OK        {r.LibraryName}",
                (false, true) => $"  ROLLED BACK  {r.LibraryName} -- {r.Error}; previous card restored",
                _ => $"  FAILED    {r.LibraryName} -- {r.Error}"
            });

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
