using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public record UpdateResult(
    bool Success,
    string? NewVersion,
    string? ErrorMessage,
    StaleUrlAction? UrlAction);
