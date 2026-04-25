using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class BackgroundJobService : IBackgroundJobService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BackgroundJobService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Job> CreateJobAsync(string moduleId, string jobType)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = new Job
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            JobType = jobType,
            Status = JobStatus.Queued
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<Job?> GetJobAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task StartJobAsync(Guid id, int processId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Running;
        job.ProcessId = processId;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpdateProgressAsync(Guid id, int progress, string? message = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Progress = progress;
        job.ProgressMessage = message;
        await db.SaveChangesAsync();
    }

    public async Task CompleteJobAsync(Guid id, string? resultData = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = DateTime.UtcNow;
        job.ResultData = resultData;
        await db.SaveChangesAsync();
    }

    public async Task FailJobAsync(Guid id, string errorMessage, string? resultData = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Failed;
        job.ErrorMessage = errorMessage;
        if (resultData is not null)
            job.ResultData = resultData;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RequestCancellationAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return;
        job.CancellationRequested = true;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Job>> GetActiveJobsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Jobs
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .OrderBy(j => j.StartedAt ?? DateTime.MaxValue)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Job>> GetJobsByModuleAsync(string moduleId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Jobs
            .Where(j => j.ModuleId == moduleId)
            .OrderByDescending(j => j.StartedAt ?? j.CompletedAt ?? DateTime.MinValue)
            .AsNoTracking()
            .ToListAsync();
    }
}
