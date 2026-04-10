using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AndroidDevicesModuleTests
{
    private readonly AndroidDevicesModule _module = new();

    [Fact]
    public void Id_IsAndroidDevices()
    {
        Assert.Equal("android-devices", _module.Id);
    }

    [Fact]
    public void DisplayName_IsAndroidDevices()
    {
        Assert.Equal("Android Devices", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsPhoneIcon()
    {
        Assert.Equal("bi-phone", _module.Icon);
    }

    [Fact]
    public void Dependencies_IncludesAdbAndScrcpy()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "adb");
        Assert.Contains(deps, d => d.Name == "scrcpy");
    }

    [Fact]
    public void NavEntries_IncludesDeviceSelectorAndDashboards()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/android/devices");
        Assert.Contains(entries, e => e.Href == "/android/googletv");
        Assert.Contains(entries, e => e.Href == "/android/pixel");
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty()
    {
        Assert.Empty(_module.GetBackgroundJobs());
    }

    [Fact]
    public void AdbDependency_HasCorrectVersionCommand()
    {
        var adb = _module.Dependencies.First(d => d.Name == "adb");
        Assert.Equal("adb --version", adb.VersionCommand);
        Assert.Equal("adb", adb.ExecutableName);
    }

    [Fact]
    public void ScrcpyDependency_HasGitHubSource()
    {
        var scrcpy = _module.Dependencies.First(d => d.Name == "scrcpy");
        Assert.Equal(UpdateSourceType.GitHub, scrcpy.SourceType);
        Assert.Equal("Genymobile/scrcpy", scrcpy.GitHubRepo);
    }
}
