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

    private readonly IJellyfinService _jellyfin;
    private readonly IBackgroundJobService _jobService;

    public CastCrewUpdateWorker(IJellyfinService jellyfinService, IBackgroundJobService jobService)
    {
        _jellyfin = jellyfinService;
        _jobService = jobService;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            // Fetch all persons missing images
            var allPersons = await _jellyfin.GetPersonsMissingImagesAsync(cancellationToken);

            if (allPersons.Count == 0)
            {
                await _jobService.CompleteJobAsync(jobId,
                    JsonSerializer.Serialize(new { Total = 0, Processed = 0, Errors = 0 }));
                return;
            }

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

            var persons = allPersons.Skip(startIndex).ToList();
            var totalOverall = allPersons.Count;
            var processed = 0;
            var errors = 0;

            using var semaphore = new SemaphoreSlim(MaxConcurrency);

            // Process in batches
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
                        await ProcessPersonWithRetryAsync(person, cancellationToken);
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

                // Update progress
                var currentIndex = startIndex + batchStart + batch.Count;
                var progress = (int)((double)currentIndex / totalOverall * 100);
                var message = $"Processing person {currentIndex:N0} of {totalOverall:N0}";

                await _jobService.UpdateProgressAsync(jobId, Math.Min(progress, 99), message);
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
                // Save resume data so next run can pick up where we left off
                await _jobService.FailJobAsync(jobId,
                    $"Cancelled after processing {processed} of {totalOverall}. Resume supported.");
            }
            else
            {
                await _jobService.CompleteJobAsync(jobId, resultData);
            }
        }
        catch (OperationCanceledException)
        {
            // Save resume state on cancellation
            // Job stays in Running state for resume
        }
        catch (Exception ex)
        {
            await _jobService.FailJobAsync(jobId, ex.Message);
        }
    }

    private async Task ProcessPersonWithRetryAsync(JellyfinPerson person, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _jellyfin.TriggerPersonImageDownloadAsync(person.Id, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw; // don't retry cancellations
            }
            catch (Exception) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelayMs * attempt, ct);
            }
        }
    }

    private record ResumeData(int LastProcessedIndex);
}
