using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public enum UpdateOutcome { Installed, Failed, NeedsUnverifiedConfirmation }

public record UpdateResult(
    bool Success,
    string? NewVersion,
    string? ErrorMessage,
    StaleUrlAction? UrlAction,
    UpdateOutcome Outcome = UpdateOutcome.Installed,
    string? ConfirmTool = null,
    string? ConfirmVersion = null,
    string? ConfirmHost = null);
