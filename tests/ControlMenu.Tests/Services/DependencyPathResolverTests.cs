using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class DependencyPathResolverTests
{
    [Fact]
    public void DependencyNotInstalledException_CarriesModuleAndName()
    {
        var ex = new DependencyNotInstalledException("android-devices", "adb", "/expected/path/adb.exe");

        Assert.Equal("android-devices", ex.ModuleId);
        Assert.Equal("adb", ex.Name);
        Assert.Equal("/expected/path/adb.exe", ex.ExpectedPath);
        Assert.Contains("adb", ex.Message);
        Assert.Contains("android-devices", ex.Message);
    }
}
