using ControlMenu.Common.Paths;
using Xunit;

namespace ControlMenu.Common.Tests.Paths;

public class PathResolverTests
{
    [Fact]
    public void DeriveInstallRoot_FromExeUnderCurrentSubdir_ReturnsParentOfCurrent()
    {
        var exe = @"C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe";
        var root = PathResolver.DeriveInstallRoot(exe);
        Assert.Equal(@"C:\Program Files\ControlMenu", root);
    }

    [Fact]
    public void DeriveInstallRoot_FromExeUnderArbitraryDevPath_ReturnsParentDir()
    {
        // Dev mode: exe lives at <repo>/src/ControlMenuLauncher/bin/Release/net10.0/ControlMenuLauncher.exe.
        // The "install root" semantic is "exe.parent().parent()" — for dev that
        // resolves to the bin folder's grandparent (Release/), which is fine
        // because no AppConfig lives there and the lenient loader returns
        // defaults in dev. Mirrors paths.rs::compute install_root derivation.
        var exe = @"C:\repo\src\ControlMenuLauncher\bin\Release\net10.0\ControlMenuLauncher.exe";
        var root = PathResolver.DeriveInstallRoot(exe);
        Assert.Equal(@"C:\repo\src\ControlMenuLauncher\bin\Release", root);
    }

    [Fact]
    public void DeriveInstallRoot_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathResolver.DeriveInstallRoot(null!));
        Assert.Throws<ArgumentException>(() => PathResolver.DeriveInstallRoot(string.Empty));
    }

    [Fact]
    public void DeriveInstallRoot_ExeWithNoGrandparent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => PathResolver.DeriveInstallRoot(@"C:\foo.exe"));
    }
}
