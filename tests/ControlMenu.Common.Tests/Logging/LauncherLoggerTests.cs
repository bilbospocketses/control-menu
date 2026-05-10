using ControlMenu.Common.Logging;
using Xunit;

namespace ControlMenu.Common.Tests.Logging;

[Collection("EnvVarSerialized")]
public class LauncherLoggerTests : IDisposable
{
    private readonly string _origProgramData;
    private readonly string _testProgramData;

    public LauncherLoggerTests()
    {
        // Override PROGRAMDATA so AppConfigPaths.DataRootFromEnv resolves into a tempdir.
        _origProgramData = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? "";
        _testProgramData = Path.Combine(Path.GetTempPath(), "cm-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testProgramData);
        Environment.SetEnvironmentVariable("PROGRAMDATA", _testProgramData);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PROGRAMDATA", _origProgramData);
        if (Directory.Exists(_testProgramData))
            Directory.Delete(_testProgramData, recursive: true);
    }

    [Fact]
    public void Info_AppendsLineToLogFileUnderProgramDataLogs()
    {
        LauncherLogger.Info("hello");
        var expectedPath = Path.Combine(_testProgramData, "ControlMenu", "logs", "launcher.log");
        Assert.True(File.Exists(expectedPath), $"expected {expectedPath}");
        var content = File.ReadAllText(expectedPath);
        Assert.Contains("hello", content);
        Assert.Contains("[INFO]", content);
    }

    [Fact]
    public void Error_AppendsLineWithErrorLevel()
    {
        LauncherLogger.Error("oops");
        var expectedPath = Path.Combine(_testProgramData, "ControlMenu", "logs", "launcher.log");
        var content = File.ReadAllText(expectedPath);
        Assert.Contains("oops", content);
        Assert.Contains("[ERROR]", content);
    }

    [Fact]
    public void MultipleCalls_AllLinesAppendInOrder()
    {
        LauncherLogger.Info("first");
        LauncherLogger.Info("second");
        LauncherLogger.Error("third");
        var path = Path.Combine(_testProgramData, "ControlMenu", "logs", "launcher.log");
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
        Assert.Contains("third", lines[2]);
        Assert.Contains("[INFO]", lines[0]);
        Assert.Contains("[INFO]", lines[1]);
        Assert.Contains("[ERROR]", lines[2]);
    }

    [Fact]
    public void TimestampFormat_IsIso8601UtcWithMillisecondsAndBrackets()
    {
        LauncherLogger.Info("ts-check");
        var path = Path.Combine(_testProgramData, "ControlMenu", "logs", "launcher.log");
        var line = File.ReadAllText(path).TrimEnd();
        // Format: "yyyy-MM-dd HH:mm:ss.fff [INFO] ts-check"
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] ts-check$", line);
    }

    [Fact]
    public void DoesNotThrow_WhenFileWriteFails()
    {
        // Creates a directory at the log path so File.AppendAllText fails.
        var logsDir = Path.Combine(_testProgramData, "ControlMenu", "logs");
        Directory.CreateDirectory(logsDir);
        var blocking = Path.Combine(logsDir, "launcher.log");
        Directory.CreateDirectory(blocking);  // path exists as dir, blocks file write

        // Must not throw — best-effort logging.
        var ex = Record.Exception(() => LauncherLogger.Info("blocked"));
        Assert.Null(ex);
    }
}
