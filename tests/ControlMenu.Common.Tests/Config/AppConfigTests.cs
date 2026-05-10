using System.Text.Json;
using ControlMenu.Common.Config;
using Xunit;

namespace ControlMenu.Common.Tests.Config;

public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "missing.json");
        var cfg = AppConfig.Load(path);
        Assert.Null(cfg.InstallMode);
        Assert.False(cfg.FirstRunComplete);
        Assert.Null(cfg.WebPort);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{not json");
        var cfg = AppConfig.Load(path);
        Assert.Null(cfg.InstallMode);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "");
        var cfg = AppConfig.Load(path);
        Assert.Null(cfg.InstallMode);
    }

    [Fact]
    public void Load_ServiceMode_IsServiceModeTrue()
    {
        // CM-spec vocabulary: installMode = "service" (NOT upstream's "user-service")
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"service","webPort":5159}""");
        var cfg = AppConfig.Load(path);
        Assert.Equal("service", cfg.InstallMode);
        Assert.Equal(5159, cfg.WebPort);
        Assert.True(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_UserMode_IsServiceModeFalse()
    {
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"user"}""");
        var cfg = AppConfig.Load(path);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_UpstreamServiceVariant_IsServiceModeFalseInCm()
    {
        // CM's vocabulary is "service" (not "user-service"). If a user somehow
        // gets a config.json with upstream-style "user-service", CM does NOT
        // treat it as service mode — that's the deliberate spec delta.
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"user-service"}""");
        var cfg = AppConfig.Load(path);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_IgnoresUnknownFields()
    {
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"user","autoUpdate":true,"channel":"beta"}""");
        var cfg = AppConfig.Load(path);
        Assert.Equal("user", cfg.InstallMode);
    }

    [Fact]
    public void LoadStrict_MissingFile_Throws()
    {
        var path = Path.Combine(_tempDir, "missing.json");
        Assert.Throws<FileNotFoundException>(() => AppConfig.LoadStrict(path));
    }

    [Fact]
    public void LoadStrict_MalformedJson_Throws()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{not json");
        Assert.Throws<JsonException>(() => AppConfig.LoadStrict(path));
    }

    [Fact]
    public void DataRootForWindows_WithProgramData_AppendsControlMenu()
    {
        var root = AppConfigPaths.DataRootForWindows(@"C:\ProgramData");
        Assert.Equal(@"C:\ProgramData\ControlMenu", root);
    }

    [Fact]
    public void DataRootForWindows_NullProgramData_FallsBackToDefault()
    {
        var root = AppConfigPaths.DataRootForWindows(null);
        Assert.Equal(@"C:\ProgramData\ControlMenu", root);
    }

    [Fact]
    public void DataRootForWindows_EmptyProgramData_FallsBackToDefault()
    {
        var root = AppConfigPaths.DataRootForWindows("");
        Assert.Equal(@"C:\ProgramData\ControlMenu", root);
    }

    [Fact]
    public void DataRootForWindows_CustomProgramData_Honored()
    {
        var root = AppConfigPaths.DataRootForWindows(@"D:\Custom\ProgramData");
        Assert.Equal(@"D:\Custom\ProgramData\ControlMenu", root);
    }
}
