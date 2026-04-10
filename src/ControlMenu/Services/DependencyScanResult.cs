namespace ControlMenu.Services;

public record DependencyScanResult(
    string Name,
    string ModuleId,
    bool Found,
    string? Path,
    string? Version,
    string Source);
