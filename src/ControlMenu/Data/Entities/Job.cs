using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Job
{
    public Guid Id { get; set; }
    public required string ModuleId { get; set; }
    public required string JobType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int? Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public int? ProcessId { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultData { get; set; }
}
