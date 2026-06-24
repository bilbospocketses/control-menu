namespace ControlMenu.Modules.Jellyfin.Services;

public class OperationLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _filePath;
    private readonly TimeSpan _utcOffset;

    public string FilePath => _filePath;

    private OperationLogger(StreamWriter writer, string filePath, TimeSpan utcOffset)
    {
        _writer = writer;
        _filePath = filePath;
        _utcOffset = utcOffset;
    }

    public static OperationLogger Create(string operation, TimeSpan? utcOffset, ControlMenu.Common.Paths.IDataPathResolver paths)
    {
        var offset = utcOffset ?? TimeSpan.Zero;
        var logDir = Path.Combine(paths.GetLogsDir(), "jellyfin");
        Directory.CreateDirectory(logDir);

        var now = DateTimeOffset.UtcNow.ToOffset(offset);
        var timestamp = now.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(logDir, $"{operation}_{timestamp}.log");
        var writer = new StreamWriter(filePath, append: false) { AutoFlush = true };

        var logger = new OperationLogger(writer, filePath, offset);
        logger.Log("START", operation);
        return logger;
    }

    /// <summary>
    /// Parses the "app-timezone" setting value into a TimeSpan offset.
    /// Returns TimeSpan.Zero (UTC) for null, empty, or "UTC".
    /// </summary>
    public static TimeSpan ParseTimezoneOffset(string? setting)
    {
        if (string.IsNullOrEmpty(setting) || setting == "UTC")
            return TimeSpan.Zero;
        return TimeSpan.TryParse(setting, out var offset) ? offset : TimeSpan.Zero;
    }

    public void Log(string level, string message)
    {
        var ts = DateTimeOffset.UtcNow.ToOffset(_utcOffset).ToString("yyyy-MM-dd HH:mm:ss");
        _writer.WriteLine($"{ts} {level,-5} {message}");
    }

    public void Step(string message) => Log("STEP", message);
    public void Ok(string message) => Log("OK", message);
    public void Fail(string message) => Log("FAIL", message);
    public void Done(string message) => Log("DONE", message);

    public static string GetDefaultLogDirectory(ControlMenu.Common.Paths.IDataPathResolver paths) =>
        Path.Combine(paths.GetLogsDir(), "jellyfin");

    // Pure: returns the path only. Callers that WRITE backups create the directory
    // (JellyfinService.BackupDatabaseAsync already does), so a getter shouldn't have a
    // filesystem side effect — mirrors GetDefaultLogDirectory above.
    public static string GetDefaultBackupDirectory(ControlMenu.Common.Paths.IDataPathResolver paths) =>
        paths.GetJellyfinBackupsDir();

    public static IReadOnlyList<OperationLogEntry> GetRecentLogs(int count, ControlMenu.Common.Paths.IDataPathResolver paths)
    {
        var logDir = GetDefaultLogDirectory(paths);
        if (!Directory.Exists(logDir)) return [];

        var entries = new List<OperationLogEntry>();
        var files = Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(count);

        foreach (var f in files)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var parts = name.Split('_', 2);
                // Open with FileShare.ReadWrite so we can read logs that are actively being written
                using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lastLine = "";
                while (reader.ReadLine() is { } line)
                    lastLine = line;
                var hasDone = lastLine.Contains("DONE");
                var hasFail = lastLine.Contains("FAIL");
                var status = hasFail ? OperationLogStatus.Failed
                    : hasDone ? OperationLogStatus.Success
                    : OperationLogStatus.InProgress;
                entries.Add(new OperationLogEntry(
                    Operation: parts[0],
                    Timestamp: File.GetLastWriteTimeUtc(f),
                    Status: status,
                    FilePath: f,
                    Summary: lastLine
                ));
            }
            catch (IOException)
            {
                // Skip files that can't be read
            }
        }

        return entries;
    }

    public void Dispose() => _writer.Dispose();
}

public enum OperationLogStatus { InProgress, Success, Failed }

public record OperationLogEntry(
    string Operation,
    DateTime Timestamp,
    OperationLogStatus Status,
    string FilePath,
    string Summary)
{
    // Backward compat for existing callers that check .Success
    public bool Success => Status == OperationLogStatus.Success;
};
