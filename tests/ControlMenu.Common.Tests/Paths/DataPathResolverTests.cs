using ControlMenu.Common.Paths;
using Xunit;

namespace ControlMenu.Common.Tests.Paths;

public class DataPathResolverTests
{
    [Fact]
    public void Velopack_GetDataRoot_RootsAtProgramDataControlMenu()
    {
        var r = new VelopackDataPathResolver(installRoot: @"C:\Program Files\ControlMenu", programData: @"C:\ProgramData");
        Assert.Equal(@"C:\ProgramData\ControlMenu", r.GetDataRoot());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config", r.GetConfigDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config\controlmenu.db", r.GetDbPath());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config\app-config.json", r.GetAppConfigPath());
        Assert.Equal(@"C:\ProgramData\ControlMenu\dependencies", r.GetDependenciesDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\logs", r.GetLogsDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\keys", r.GetKeysDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\jellyfin-backups", r.GetJellyfinBackupsDir());
    }

    [Fact]
    public void Velopack_GetInstallRoot_AndCurrent_ReflectInputs()
    {
        var r = new VelopackDataPathResolver(installRoot: @"C:\Program Files\ControlMenu", programData: @"C:\ProgramData");
        Assert.Equal(@"C:\Program Files\ControlMenu", r.GetInstallRoot());
        Assert.Equal(@"C:\Program Files\ControlMenu\current", r.GetCurrentDir());
    }

    [Fact]
    public void Dev_RootsAtBaseDirectory()
    {
        var baseDir = @"C:\repo\src\ControlMenu\bin\Release\net10.0";
        var r = new DevDataPathResolver(baseDir);
        Assert.Equal(baseDir, r.GetDataRoot());
        Assert.Equal(Path.Combine(baseDir, "controlmenu.db"), r.GetDbPath());
        Assert.Equal(Path.Combine(baseDir, "dependencies"), r.GetDependenciesDir());
        Assert.Equal(Path.Combine(baseDir, "logs"), r.GetLogsDir());
        Assert.Equal(Path.Combine(baseDir, "keys"), r.GetKeysDir());
    }

    [Fact]
    public void Factory_DetectsVelopackMode_WhenUpdateExeAdjacentToInstallRoot()
    {
        var tempInstall = Path.Combine(Path.GetTempPath(), "vmode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        Directory.CreateDirectory(Path.Combine(tempInstall, "current"));
        File.WriteAllText(Path.Combine(tempInstall, "Update.exe"), "stub");

        try
        {
            var fakeExe = Path.Combine(tempInstall, "current", "ControlMenuLauncher.exe");
            var resolver = DataPathResolverFactory.Create(fakeExe, programData: @"C:\ProgramData");
            Assert.IsType<VelopackDataPathResolver>(resolver);
        }
        finally { Directory.Delete(tempInstall, recursive: true); }
    }

    [Fact]
    public void Factory_DetectsDevMode_WhenNoAdjacentUpdateExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-" + Guid.NewGuid().ToString("N"));
        var devExe = Path.Combine(root, "bin", "Release", "net10.0", "ControlMenu.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(devExe)!);
        File.WriteAllText(devExe, "stub");
        try
        {
            var resolver = DataPathResolverFactory.Create(devExe, programData: @"C:\ProgramData");
            Assert.IsType<DevDataPathResolver>(resolver);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
