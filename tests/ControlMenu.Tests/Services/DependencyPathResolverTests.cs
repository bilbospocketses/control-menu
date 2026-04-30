using ControlMenu.Modules;
using ControlMenu.Services;
using Moq;

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

public class DependencyPathResolverResolveTests
{
    private static IToolModule MakeModule(string id, params ModuleDependency[] deps)
    {
        var mock = new Mock<IToolModule>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.Dependencies).Returns(deps);
        return mock.Object;
    }

    [Fact]
    public async Task ResolveAsync_ReturnsLocalExePath_WhenFileExists()
    {
        var tempDir = Directory.CreateTempSubdirectory("cm-resolver-test");
        try
        {
            var exeName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            var exePath = Path.Combine(tempDir.FullName, exeName);
            File.WriteAllText(exePath, "fake-binary");

            var dep = new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"([\d.]+)",
                InstallPath = tempDir.FullName
            };
            var module = MakeModule("android-devices", dep);

            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync("dep-path-adb", It.IsAny<string?>()))
                  .ReturnsAsync((string?)null);

            var resolver = new DependencyPathResolver(new[] { module }, config.Object);

            var result = await resolver.ResolveAsync("android-devices", "adb");

            Assert.Equal(exePath, result, ignoreCase: true);
        }
        finally { tempDir.Delete(recursive: true); }
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenBinaryMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory("cm-resolver-missing");
        try
        {
            var dep = new ModuleDependency
            {
                Name = "adb", ExecutableName = "adb",
                VersionCommand = "adb --version", VersionPattern = @"([\d.]+)",
                InstallPath = tempDir.FullName
            };
            var module = MakeModule("android-devices", dep);
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync((string?)null);
            var resolver = new DependencyPathResolver(new[] { module }, config.Object);

            var ex = await Assert.ThrowsAsync<DependencyNotInstalledException>(
                () => resolver.ResolveAsync("android-devices", "adb"));
            Assert.Equal("adb", ex.Name);
            Assert.Contains(tempDir.FullName, ex.ExpectedPath);
        }
        finally { tempDir.Delete(recursive: true); }
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenModuleUnknown()
    {
        var resolver = new DependencyPathResolver(Array.Empty<IToolModule>(), new Mock<IConfigurationService>().Object);
        await Assert.ThrowsAsync<DependencyNotInstalledException>(
            () => resolver.ResolveAsync("nope", "adb"));
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenDependencyNotDeclared()
    {
        var module = MakeModule("android-devices");
        var resolver = new DependencyPathResolver(new[] { module }, new Mock<IConfigurationService>().Object);
        await Assert.ThrowsAsync<DependencyNotInstalledException>(
            () => resolver.ResolveAsync("android-devices", "adb"));
    }

    [Fact]
    public async Task ResolveAsync_HonorsUserOverride()
    {
        var defaultDir = Directory.CreateTempSubdirectory("cm-resolver-default");
        var overrideDir = Directory.CreateTempSubdirectory("cm-resolver-override");
        try
        {
            var exeName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            var exeInOverride = Path.Combine(overrideDir.FullName, exeName);
            File.WriteAllText(exeInOverride, "fake");

            var dep = new ModuleDependency
            {
                Name = "adb", ExecutableName = "adb",
                VersionCommand = "adb --version", VersionPattern = @"([\d.]+)",
                InstallPath = defaultDir.FullName
            };
            var module = MakeModule("android-devices", dep);
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync("dep-path-adb", It.IsAny<string?>()))
                  .ReturnsAsync(overrideDir.FullName);
            var resolver = new DependencyPathResolver(new[] { module }, config.Object);

            var result = await resolver.ResolveAsync("android-devices", "adb");
            Assert.Equal(exeInOverride, result, ignoreCase: true);
        }
        finally { defaultDir.Delete(recursive: true); overrideDir.Delete(recursive: true); }
    }
}
