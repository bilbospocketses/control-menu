namespace ControlMenu.Services;

public record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);
