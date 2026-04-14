using ControlMenu.Data;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Jellyfin.Workers;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class CastCrewUpdateWorkerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IJellyfinService> _mockJellyfin = new();
    private readonly Mock<IBackgroundJobService> _mockJobService;
    private static readonly JellyfinApiConfig TestApiConfig = new("http://localhost:8096", "test-key", "test-user");

    public CastCrewUpdateWorkerTests()
    {
        _db = TestDbContextFactory.Create();
        _mockJobService = new Mock<IBackgroundJobService>();
        _mockJellyfin.Setup(j => j.GetApiConfigAsync()).ReturnsAsync(TestApiConfig);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExecuteAsync_ProcessesAllPersonsWithParallelism()
    {
        var persons = Enumerable.Range(1, 20)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);

        var processedIds = new System.Collections.Concurrent.ConcurrentBag<string>();
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .Returns<string, JellyfinApiConfig, CancellationToken>(async (id, _, ct) =>
            {
                await Task.Delay(10, ct); // simulate network
                processedIds.Add(id);
            });

        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new ControlMenu.Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.ExecuteAsync(jobId, cts.Token);

        Assert.Equal(20, processedIds.Count);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCanellation()
    {
        var persons = Enumerable.Range(1, 100)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);

        var processedCount = 0;
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .Returns<string, JellyfinApiConfig, CancellationToken>(async (_, _, ct) =>
            {
                await Task.Delay(50, ct);
                Interlocked.Increment(ref processedCount);
            });

        var jobId = Guid.NewGuid();
        var cancelRequested = false;
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(() => new ControlMenu.Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running,
                CancellationRequested = cancelRequested
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        // Cancel after a short delay
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await worker.ExecuteAsync(jobId, cts.Token);

        // Should have processed some but not all
        Assert.True(processedCount < 100, $"Expected partial processing but got {processedCount}");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgress()
    {
        var persons = Enumerable.Range(1, 10)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new ControlMenu.Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.ExecuteAsync(jobId, cts.Token);

        // Verify progress was reported at least once
        _mockJobService.Verify(
            j => j.UpdateProgressAsync(jobId, It.IsAny<int>(), It.IsAny<string>()),
            Times.AtLeastOnce);

        // Verify completion
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }
}
