using ControlMenu.Modules;
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class BuildVersionedDownloadUrlTests
{
    private static ModuleDependency Dep(string? assetPattern) => new()
    {
        Name = "tool",
        ExecutableName = "tool",
        VersionCommand = "--version",
        VersionPattern = @"[\d.]+",
        AssetPattern = assetPattern,
    };

    [Fact]
    public void UsesDeclaredAssetPattern_NotHardcodedSqlitePattern()
    {
        // A non-sqlite dependency: the hardcoded sqlite-tools pattern would never match this,
        // so a successful resolve proves the dep's own AssetPattern drove the match.
        var page = @"<a href=""2025/foo-42.zip"">foo</a>";
        var url = DependencyManagerService.BuildVersionedDownloadUrl(
            "https://example.com/old/foo-1.zip", page, Dep(@"foo-\d+\.zip"));
        Assert.Equal("https://sqlite.org/2025/foo-42.zip", url);
    }

    [Fact]
    public void FallsBackToSqlitePattern_WhenAssetPatternNull()
    {
        if (!OperatingSystem.IsWindows()) return; // the fallback pattern uses "win" on a Windows runner
        var page = @"<a href=""2025/sqlite-tools-win-x64-3450100.zip"">x</a>";
        var url = DependencyManagerService.BuildVersionedDownloadUrl(
            "https://sqlite.org/old/sqlite-tools-win-x64-1.zip", page, Dep(null));
        Assert.Equal("https://sqlite.org/2025/sqlite-tools-win-x64-3450100.zip", url);
    }
}
