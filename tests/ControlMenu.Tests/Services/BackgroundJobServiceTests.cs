using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Data;

namespace ControlMenu.Tests.Services;

public class BackgroundJobServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly BackgroundJobService _service;

    public BackgroundJobServiceTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _service = new BackgroundJobService(_dbFactory);
    }

    public void Dispose() => _dbFactory.Dispose();

    [Fact]
    public async Task CreateJobAsync_InsertsJobWithQueuedStatus()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("jellyfin", job.ModuleId);
        Assert.Equal("cast-crew-update", job.JobType);
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsJob_WhenExists()
    {
        var created = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        var retrieved = await _service.GetJobAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetJobAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateProgressAsync_SetsProgressAndMessage()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        await _service.UpdateProgressAsync(job.Id, 42, "Processing person 42 of 100");

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(42, updated!.Progress);
        Assert.Equal("Processing person 42 of 100", updated.ProgressMessage);
    }

    [Fact]
    public async Task StartJobAsync_SetsRunningStatusAndPid()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        await _service.StartJobAsync(job.Id, 12345);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Running, updated!.Status);
        Assert.Equal(12345, updated.ProcessId);
        Assert.NotNull(updated.StartedAt);
    }

    [Fact]
    public async Task CompleteJobAsync_SetsCompletedStatus()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.CompleteJobAsync(job.Id);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Completed, updated!.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.Equal(100, updated.Progress);
    }

    [Fact]
    public async Task FailJobAsync_SetsFailedStatusWithError()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.FailJobAsync(job.Id, "Something went wrong");

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Failed, updated!.Status);
        Assert.Equal("Something went wrong", updated.ErrorMessage);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task RequestCancellationAsync_SetsCancellationFlag()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.RequestCancellationAsync(job.Id);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.True(updated!.CancellationRequested);
    }

    [Fact]
    public async Task GetActiveJobsAsync_ReturnsOnlyRunningAndQueued()
    {
        var j1 = await _service.CreateJobAsync("jellyfin", "type1");
        var j2 = await _service.CreateJobAsync("jellyfin", "type2");
        var j3 = await _service.CreateJobAsync("jellyfin", "type3");
        await _service.StartJobAsync(j2.Id, 123);
        await _service.StartJobAsync(j3.Id, 456);
        await _service.CompleteJobAsync(j3.Id);

        var active = await _service.GetActiveJobsAsync();

        Assert.Equal(2, active.Count); // j1 (Queued) + j2 (Running)
        Assert.Contains(active, j => j.Id == j1.Id);
        Assert.Contains(active, j => j.Id == j2.Id);
    }

    [Fact]
    public async Task GetJobsByModuleAsync_FiltersCorrectly()
    {
        await _service.CreateJobAsync("jellyfin", "type1");
        await _service.CreateJobAsync("other", "type2");

        var jobs = await _service.GetJobsByModuleAsync("jellyfin");

        Assert.Single(jobs);
        Assert.Equal("jellyfin", jobs[0].ModuleId);
    }
}
