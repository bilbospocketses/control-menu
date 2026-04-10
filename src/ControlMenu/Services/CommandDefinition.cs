namespace ControlMenu.Services;

public record CommandDefinition
{
    public required string WindowsCommand { get; init; }
    public required string LinuxCommand { get; init; }
    public string? WindowsArguments { get; init; }
    public string? LinuxArguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan? Timeout { get; init; }
}
