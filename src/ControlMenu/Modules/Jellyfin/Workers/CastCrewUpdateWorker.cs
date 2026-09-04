using System.Text.Json;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Workers;

public class CastCrewUpdateWorker
{
    private const int MaxConcurrency = 4;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 2000;
    private const int BatchSize = 20;
    private const int LogProgressEveryNBatches = 5;

    private readonly IJellyfinService _jellyfin;
    private readonly IBackgroundJobService _jobService;
    private readonly IEmailService _email;
    private readonly IConfigurationService _config;
    private readonly OperationLogger? _logger;

    public CastCrewUpdateWorker(IJellyfinService jellyfinService, IBackgroundJobService jobService,
        IEmailService emailService, IConfigurationService configService, OperationLogger? logger = null)
    {
        _jellyfin = jellyfinService;
        _jobService = jobService;
        _email = emailService;
        _config = configService;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            // Resolve API config once up front
            _logger?.Step("Resolving Jellyfin API configuration");
            var apiConfig = await _jellyfin.GetApiConfigAsync();
            _logger?.Ok($"API: {apiConfig.BaseUrl}, userId: {apiConfig.UserId ?? "not set"}");

            // Fetch all persons missing images
            _logger?.Step("Fetching persons missing images from Jellyfin API");
            var allPersons = await _jellyfin.GetPersonsMissingImagesAsync(cancellationToken);

            if (allPersons.Count == 0)
            {
                _logger?.Done("No persons missing images — nothing to do");
                await _jobService.CompleteJobAsync(jobId,
                    JsonSerializer.Serialize(new { Total = 0, Processed = 0, Errors = 0 }));
                return;
            }

            _logger?.Ok($"Found {allPersons.Count:N0} persons missing images");

            // Check for resume — skip already-processed persons
            var job = await _jobService.GetJobAsync(jobId);
            var startIndex = 0;
            if (job?.ResultData is not null)
            {
                try
                {
                    var resumeData = JsonSerializer.Deserialize<ResumeData>(job.ResultData);
                    startIndex = resumeData?.LastProcessedIndex ?? 0;
                }
                catch { /* fresh start */ }
            }

            if (startIndex > 0)
                _logger?.Step($"Resuming from index {startIndex:N0} ({allPersons.Count - startIndex:N0} remaining)");

            var persons = allPersons.Skip(startIndex).ToList();
            var totalOverall = allPersons.Count;
            var processed = 0;
            var errors = 0;

            using var semaphore = new SemaphoreSlim(MaxConcurrency);

            // Process in batches
            var batchNumber = 0;
            for (var batchStart = 0; batchStart < persons.Count; batchStart += BatchSize)
            {
                // Check cancellation
                if (cancellationToken.IsCancellationRequested)
                    break;

                var checkJob = await _jobService.GetJobAsync(jobId);
                if (checkJob?.CancellationRequested == true)
                    break;

                var batch = persons.Skip(batchStart).Take(BatchSize).ToList();
                var tasks = batch.Select(async person =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await ProcessPersonWithRetryAsync(person, apiConfig, cancellationToken);
                        Interlocked.Increment(ref processed);
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                batchNumber++;

                // Update progress
                var currentIndex = startIndex + batchStart + batch.Count;
                var progress = (int)((double)currentIndex / totalOverall * 100);
                var message = $"Processing person {currentIndex:N0} of {totalOverall:N0}";

                await _jobService.UpdateProgressAsync(jobId, Math.Min(progress, 99), message);

                if (batchNumber % LogProgressEveryNBatches == 0)
                    _logger?.Step($"Progress: {currentIndex:N0}/{totalOverall:N0} ({progress}%) — {processed:N0} succeeded, {errors:N0} failed");
            }

            // Save final state
            var resultData = JsonSerializer.Serialize(new
            {
                Total = totalOverall,
                Processed = processed,
                Errors = errors,
                LastProcessedIndex = startIndex + processed + errors
            });

            if (cancellationToken.IsCancellationRequested ||
                (await _jobService.GetJobAsync(jobId))?.CancellationRequested == true)
            {
                var cancelMsg = $"Cancelled after processing {processed:N0} of {totalOverall:N0}. Resume supported.";
                _logger?.Fail(cancelMsg);
                await _jobService.FailJobAsync(jobId, cancelMsg, resultData);
                await SendNotificationAsync("Cancelled", cancelMsg);
            }
            else if (processed == 0 && errors > 0)
            {
                var failMsg = $"0 of {totalOverall:N0} persons succeeded — all {errors:N0} updates failed";
                _logger?.Fail(failMsg);
                await _jobService.FailJobAsync(jobId, failMsg, resultData);
                await SendNotificationAsync("Failed", failMsg);
            }
            else
            {
                var summary = $"{processed:N0} succeeded, {errors:N0} failed out of {totalOverall:N0} total";
                _logger?.Done($"Completed: {summary}");
                await _jobService.CompleteJobAsync(jobId, resultData);
                await SendNotificationAsync("Completed", summary);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.Fail("Cancelled by token");
            try
            {
                await _jobService.FailJobAsync(jobId, "Cancelled. Resume supported on next run.");
            }
            catch { /* best effort — scope may be disposed */ }
        }
        catch (Exception ex)
        {
            _logger?.Fail($"Unexpected error: {ex.Message}");
            await _jobService.FailJobAsync(jobId, ex.Message);
        }
    }

    private async Task ProcessPersonWithRetryAsync(JellyfinPerson person, JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _jellyfin.TriggerPersonImageDownloadAsync(person.Id, apiConfig, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // don't retry job cancellations — but do retry per-request timeouts
            }
            catch (Exception) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelayMs * attempt, ct);
            }
        }
    }

    private async Task SendNotificationAsync(string status, string details)
    {
        try
        {
            var to = await _config.GetSettingAsync("notification-email");
            if (string.IsNullOrEmpty(to)) return;

            var subject = $"Cast & Crew Update — {status}";
            var body = $"Cast & Crew image update has {status.ToLowerInvariant()}.\n\n{details}";
            await _email.SendAsync(to, subject, body);
        }
        catch
        {
            // Best effort — don't fail the job over a notification
        }
    }

    private record ResumeData(int LastProcessedIndex);
}
