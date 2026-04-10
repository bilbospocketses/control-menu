namespace ControlMenu.Services;

public record DependencyCheckResult(
    Guid DependencyId,
    string Name,
    Data.Enums.DependencyStatus Status,
    string? InstalledVersion,
    string? LatestVersion,
    string? ErrorMessage);
