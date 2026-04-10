namespace ControlMenu.Modules;

public record BackgroundJobDefinition(
    string JobType,
    string DisplayName,
    string Description,
    bool IsLongRunning = false);
