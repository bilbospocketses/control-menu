namespace ControlMenu.Modules;

public record NavEntry(
    string Title,
    string Href,
    string? Icon = null,
    int SortOrder = 0,
    Func<IServiceProvider, bool>? IsVisible = null);
