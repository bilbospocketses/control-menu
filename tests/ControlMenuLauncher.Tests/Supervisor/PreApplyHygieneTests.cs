using System.Runtime.Versioning;
using ControlMenu.Common.Paths;
using ControlMenu.Launcher.Supervisor;
using Xunit;

namespace ControlMenu.Launcher.Tests.Supervisor;

[SupportedOSPlatform("windows")]
public class PreApplyHygieneTests
{
    private sealed class StubPaths : IDataPathResolver
    {
        private readonly string _root;
        public StubPaths(string root) { _root = root; }
        public string GetInstallRoot() => _root;
        public string GetCurrentDir() => _root;
        public string GetDataRoot() => _root;
        public string GetConfigDir() => _root;
        public string GetDbPath() => Path.Combine(_root, "controlmenu.db");
        public string GetAppConfigPath() => Path.Combine(_root, "app-config.json");
        public string GetDependenciesDir() => Path.Combine(_root, "dependencies");
        public string GetLogsDir() => Path.Combine(_root, "logs");
        public string GetKeysDir() => Path.Combine(_root, "keys");
        public string GetJellyfinBackupsDir() => Path.Combine(_root, "jellyfin-backups");
    }

    [Fact]
    public async Task RunAsync_DoesNotThrow_WhenAdbAbsent()
    {
        // Tempdir; no adb.exe present. The hygiene should skip kill-server,
        // run taskkill (which returns nonzero when no matching process exists,
        // but TryRunAsync tolerates that), then settle, then return cleanly.
        var temp = Path.Combine(Path.GetTempPath(), "cm-hyg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var paths = new StubPaths(temp);
            var ex = await Record.ExceptionAsync(() => PreApplyHygiene.RunAsync(paths));
            Assert.Null(ex);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_RespondsToCancellation()
    {
        // Pre-cancelled token: hygiene swallows OperationCanceledException internally
        // and must still return without throwing.
        var temp = Path.Combine(Path.GetTempPath(), "cm-hyg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var paths = new StubPaths(temp);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = await Record.ExceptionAsync(() => PreApplyHygiene.RunAsync(paths, cts.Token));
            Assert.Null(ex);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
