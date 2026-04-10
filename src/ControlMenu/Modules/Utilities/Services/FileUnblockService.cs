using ControlMenu.Services;

namespace ControlMenu.Modules.Utilities.Services;

public class FileUnblockService : IFileUnblockService
{
    private readonly ICommandExecutor _executor;

    public FileUnblockService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<UnblockResult> UnblockDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        var command = $"-Command \"$files = Get-ChildItem '{directoryPath}' -Recurse | Unblock-File -PassThru; $files.Count\"";
        var result = await _executor.ExecuteAsync("powershell", command, null, ct);

        if (result.ExitCode != 0)
            return new UnblockResult(false, ErrorMessage: result.StandardError.Trim());

        int.TryParse(result.StandardOutput.Trim(), out var count);
        return new UnblockResult(true, count);
    }
}
