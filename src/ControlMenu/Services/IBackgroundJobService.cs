using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IBackgroundJobService
{
    Task<Job> CreateJobAsync(string moduleId, string jobType);
    Task<Job?> GetJobAsync(Guid id);
    Task StartJobAsync(Guid id, int processId);
    Task UpdateProgressAsync(Guid id, int progress, string? message = null);
    Task CompleteJobAsync(Guid id, string? resultData = null);
    Task FailJobAsync(Guid id, string errorMessage);
    Task RequestCancellationAsync(Guid id);
    Task<IReadOnlyList<Job>> GetActiveJobsAsync();
    Task<IReadOnlyList<Job>> GetJobsByModuleAsync(string moduleId);
}
