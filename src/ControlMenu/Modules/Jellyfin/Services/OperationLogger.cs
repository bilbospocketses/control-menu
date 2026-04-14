namespace ControlMenu.Modules.Jellyfin.Services;

public class OperationLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _filePath;

    public string FilePath => _filePath;

    private OperationLogger(StreamWriter writer, string filePath)
    {
        _writer = writer;
        _filePath = filePath;
    }

    public static OperationLogger Create(string operation)
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");
        Directory.CreateDirectory(logDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(logDir, $"{operation}_{timestamp}.log");
        var writer = new StreamWriter(filePath, append: false) { AutoFlush = true };

        var logger = new OperationLogger(writer, filePath);
        logger.Log("START", operation);
        return logger;
    }

    public void Log(string level, string message)
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        _writer.WriteLine($"{ts} {level,-5} {message}");
    }

    public void Step(string message) => Log("STEP", message);
    public void Ok(string message) => Log("OK", message);
    public void Fail(string message) => Log("FAIL", message);
    public void Done(string message) => Log("DONE", message);

    public static string GetLogDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");

    public static string GetBackupDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static IReadOnlyList<OperationLogEntry> GetRecentLogs(int count = 10)
    {
        var logDir = GetLogDirectory();
        if (!Directory.Exists(logDir)) return [];

        return Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(count)
            .Select(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var parts = name.Split('_', 2);
                var lines = File.ReadAllLines(f);
                var lastLine = lines.LastOrDefault() ?? "";
                var success = lastLine.Contains("DONE") && !lastLine.Contains("FAIL");
                return new OperationLogEntry(
                    Operation: parts[0],
                    Timestamp: File.GetLastWriteTimeUtc(f),
                    Success: success,
                    FilePath: f,
                    Summary: lastLine
                );
            })
            .ToList();
    }

    public void Dispose() => _writer.Dispose();
}

public record OperationLogEntry(
    string Operation,
    DateTime Timestamp,
    bool Success,
    string FilePath,
    string Summary);
