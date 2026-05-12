using ControlMenu.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlMenu.Tests.Logging;

public class FileLoggingConfiguratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public FileLoggingConfiguratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "controlmenu.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* file may still be held by a flushed sink */ }
    }

    [Fact]
    public void ConfigureFileLogging_WritesEntriesToConfiguredPath()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => FileLoggingConfigurator.AddFileSink(b, _logPath));
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<FileLoggingConfiguratorTests>>();

        logger.LogInformation("hello from {Source}", "unit-test");

        // Serilog flushes on dispose; force it.
        FileLoggingConfigurator.CloseAndFlush();

        // Rolling interval = Day → Serilog inserts the date before the extension,
        // so the actual file is `controlmenu<yyyyMMdd>.log`. Verify by directory contents.
        var logFiles = Directory.GetFiles(_tempDir, "controlmenu*.log");
        Assert.NotEmpty(logFiles);
        var contents = File.ReadAllText(logFiles[0]);
        Assert.Contains("hello from", contents);
        Assert.Contains("unit-test", contents);
    }
}
