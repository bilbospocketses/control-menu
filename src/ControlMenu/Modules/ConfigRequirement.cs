namespace ControlMenu.Modules;

public record ConfigRequirement(
    string Key,
    string DisplayName,
    string Description,
    bool IsSecret = false,
    string? DefaultValue = null);
