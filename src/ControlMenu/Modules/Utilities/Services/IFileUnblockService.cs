namespace ControlMenu.Modules.Utilities.Services;

public record UnblockResult(bool Success, int FileCount = 0, string? ErrorMessage = null);

public interface IFileUnblockService
{
    bool IsSupported { get; }
    Task<UnblockResult> UnblockDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
