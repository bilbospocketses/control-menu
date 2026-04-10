using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class BackgroundJobService : IBackgroundJobService
{
    private readonly AppDbContext _db;

    public BackgroundJobService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Job> CreateJobAsync(string moduleId, string jobType)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            JobType = jobType,
            Status = JobStatus.Queued
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<Job?> GetJobAsync(Guid id)
    {
        return await _db.Jobs.FindAsync(id);
    }

    public async Task StartJobAsync(Guid id, int processId)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Running;
        job.ProcessId = processId;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateProgressAsync(Guid id, int progress, string? message = null)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Progress = progress;
        job.ProgressMessage = message;
        await _db.SaveChangesAsync();
    }

    public async Task CompleteJobAsync(Guid id, string? resultData = null)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = DateTime.UtcNow;
        job.ResultData = resultData;
        await _db.SaveChangesAsync();
    }

    public async Task FailJobAsync(Guid id, string errorMessage)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Failed;
        job.ErrorMessage = errorMessage;
        job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RequestCancellationAsync(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.CancellationRequested = true;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Job>> GetActiveJobsAsync()
    {
        return await _db.Jobs
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .OrderBy(j => j.StartedAt ?? DateTime.MaxValue)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Job>> GetJobsByModuleAsync(string moduleId)
    {
        return await _db.Jobs
            .Where(j => j.ModuleId == moduleId)
            .OrderByDescending(j => j.StartedAt ?? j.CompletedAt ?? DateTime.MinValue)
            .ToListAsync();
    }
}
