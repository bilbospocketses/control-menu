using ControlMenu.Common.Seeding;
using Xunit;

namespace ControlMenu.Common.Tests.Seeding;

public class SeedHydratorTests : IDisposable
{
    private readonly string _root;
    private readonly string _currentDir;
    private readonly string _seedDir;
    private readonly string _dataRoot;
    private readonly string _depsDir;

    public SeedHydratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm-seed-test-" + Guid.NewGuid().ToString("N"));
        _currentDir = Path.Combine(_root, "install", "current");
        _seedDir = Path.Combine(_currentDir, "seed", "dependencies");
        _dataRoot = Path.Combine(_root, "data");
        _depsDir = Path.Combine(_dataRoot, "dependencies");
        Directory.CreateDirectory(_seedDir);
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Seed(string leaf, string relPath, string content)
    {
        var path = Path.Combine(_seedDir, leaf, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Hydrate_FreshDataRoot_CopiesAllSeedDirs()
    {
        Seed("platform-tools", "adb.exe", "ADB-BINARY");
        Seed("platform-tools", "AdbWinApi.dll", "DLL");
        Seed("go2rtc", "go2rtc.exe", "GO2RTC");

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(2, result.Copied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("ADB-BINARY", File.ReadAllText(Path.Combine(_depsDir, "platform-tools", "adb.exe")));
        Assert.Equal("DLL", File.ReadAllText(Path.Combine(_depsDir, "platform-tools", "AdbWinApi.dll")));
        Assert.Equal("GO2RTC", File.ReadAllText(Path.Combine(_depsDir, "go2rtc", "go2rtc.exe")));
    }

    [Fact]
    public void Hydrate_ExistingDataRootEntry_LeavesItAlone()
    {
        Seed("platform-tools", "adb.exe", "SEED-VERSION");
        // User-updated entry already in dataRoot — must not be overwritten.
        var userDir = Path.Combine(_depsDir, "platform-tools");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "adb.exe"), "USER-VERSION");

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("USER-VERSION", File.ReadAllText(Path.Combine(_depsDir, "platform-tools", "adb.exe")));
    }

    [Fact]
    public void Hydrate_NoSeedDir_NoOps()
    {
        // Dev mode: no seed/ inside current/. Should silently no-op.
        Directory.Delete(_seedDir, recursive: true);
        Directory.Delete(Path.Combine(_currentDir, "seed"), recursive: true);

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(0, result.Copied);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Hydrate_PartialOverlap_OnlyMissingLeavesCopied()
    {
        Seed("platform-tools", "adb.exe", "SEED-ADB");
        Seed("go2rtc", "go2rtc.exe", "SEED-GO2RTC");
        // Only platform-tools is already present
        Directory.CreateDirectory(Path.Combine(_depsDir, "platform-tools"));
        File.WriteAllText(Path.Combine(_depsDir, "platform-tools", "adb.exe"), "USER-ADB");

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(1, result.Copied);    // go2rtc
        Assert.Equal(1, result.Skipped);   // platform-tools
        Assert.Equal("USER-ADB", File.ReadAllText(Path.Combine(_depsDir, "platform-tools", "adb.exe")));
        Assert.Equal("SEED-GO2RTC", File.ReadAllText(Path.Combine(_depsDir, "go2rtc", "go2rtc.exe")));
    }

    [Fact]
    public void Hydrate_NestedDirectoriesInSeed_AreCopiedRecursively()
    {
        Seed("fake-tool", "fake-tool.exe", "EXE");
        Seed("fake-tool", "lib/sub/data.bin", "NESTED");

        SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.True(File.Exists(Path.Combine(_depsDir, "fake-tool", "lib", "sub", "data.bin")));
        Assert.Equal("NESTED", File.ReadAllText(Path.Combine(_depsDir, "fake-tool", "lib", "sub", "data.bin")));
    }

    [Fact]
    public void Hydrate_PrunesRetiredScrcpyLeaf_FromDataRoot()
    {
        // An existing install hydrated scrcpy/ before it was retired. Hydrate must
        // delete it on launch so the ~40MB is reclaimed.
        var scrcpyDir = Path.Combine(_depsDir, "scrcpy");
        Directory.CreateDirectory(Path.Combine(scrcpyDir, "lib"));
        File.WriteAllText(Path.Combine(scrcpyDir, "scrcpy.exe"), "OLD");
        File.WriteAllText(Path.Combine(scrcpyDir, "lib", "SDL2.dll"), "OLD");

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(1, result.Pruned);
        Assert.False(Directory.Exists(scrcpyDir));
    }

    [Fact]
    public void Hydrate_NoRetiredLeaf_PrunesNothing()
    {
        Seed("platform-tools", "adb.exe", "ADB");

        var result = SeedHydrator.Hydrate(_currentDir, _depsDir);

        Assert.Equal(0, result.Pruned);
    }
}
