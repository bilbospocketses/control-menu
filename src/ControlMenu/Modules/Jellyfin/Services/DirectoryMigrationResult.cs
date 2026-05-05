namespace ControlMenu.Modules.Jellyfin.Services;

public sealed record DirectoryMigrationResult(
    bool Success,
    int MovedCount,
    int FailedCount,
    IReadOnlyList<string> FailedFiles,
    string? ErrorMessage)
{
    public static DirectoryMigrationResult Ok(int movedCount, IReadOnlyList<string> failedFiles) =>
        new(Success: true, MovedCount: movedCount, FailedCount: failedFiles.Count, FailedFiles: failedFiles, ErrorMessage: null);

    public static DirectoryMigrationResult Error(string message) =>
        new(Success: false, MovedCount: 0, FailedCount: 0, FailedFiles: [], ErrorMessage: message);
}
