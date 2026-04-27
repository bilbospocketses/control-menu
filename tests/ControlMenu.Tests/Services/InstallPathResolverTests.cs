using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class InstallPathResolverTests
{
    [Fact]
    public void Resolve_NullOverride_ReturnsDefault()
    {
        var def = Path.Combine(Path.GetTempPath(), "deps", "platform-tools");
        Assert.Equal(def, InstallPathResolver.Resolve(def, null));
        Assert.Equal(def, InstallPathResolver.Resolve(def, ""));
        Assert.Equal(def, InstallPathResolver.Resolve(def, "   "));
    }

    [Fact]
    public void Resolve_AbsoluteOverride_ReturnsOverrideUnchanged()
    {
        var def = Path.Combine(Path.GetTempPath(), "deps", "platform-tools");
        var abs = OperatingSystem.IsWindows()
            ? @"C:\custom\platform-tools"
            : "/opt/custom/platform-tools";
        Assert.Equal(abs, InstallPathResolver.Resolve(def, abs));
    }

    [Fact]
    public void Resolve_RelativeOverride_CombinesWithDepsRoot()
    {
        var depsRoot = Path.Combine(Path.GetTempPath(), "repoA", "deps");
        var def = Path.Combine(depsRoot, "platform-tools");

        var resolved = InstallPathResolver.Resolve(def, "platform-tools");

        Assert.Equal(Path.Combine(depsRoot, "platform-tools"), resolved);
    }

    [Fact]
    public void Resolve_RelativeOverride_FollowsDepsRootAcrossRename()
    {
        // Same relative override produces different absolute paths when DepsRoot changes —
        // the rename-survival property we want.
        var oldDef = Path.Combine(Path.GetTempPath(), "repoOld", "deps", "platform-tools");
        var newDef = Path.Combine(Path.GetTempPath(), "repoNew", "deps", "platform-tools");

        Assert.NotEqual(
            InstallPathResolver.Resolve(oldDef, "platform-tools"),
            InstallPathResolver.Resolve(newDef, "platform-tools"));

        var sep = Path.DirectorySeparatorChar;
        Assert.EndsWith($"repoNew{sep}deps{sep}platform-tools",
            InstallPathResolver.Resolve(newDef, "platform-tools"));
    }

    [Fact]
    public void Encode_PathUnderDepsRoot_ReturnsRelative()
    {
        var depsRoot = Path.Combine(Path.GetTempPath(), "repoA", "deps");
        var def = Path.Combine(depsRoot, "platform-tools");
        var chosen = Path.Combine(depsRoot, "platform-tools");

        Assert.Equal("platform-tools", InstallPathResolver.Encode(chosen, def));
    }

    [Fact]
    public void Encode_PathOutsideDepsRoot_ReturnsAbsolute()
    {
        var depsRoot = Path.Combine(Path.GetTempPath(), "repoA", "deps");
        var def = Path.Combine(depsRoot, "platform-tools");
        var chosen = OperatingSystem.IsWindows()
            ? @"C:\custom-tools\platform-tools"
            : "/opt/custom-tools/platform-tools";

        Assert.Equal(chosen, InstallPathResolver.Encode(chosen, def));
    }

    [Fact]
    public void Encode_RoundTrip_RestoresAbsolutePath()
    {
        var depsRoot = Path.Combine(Path.GetTempPath(), "repoA", "deps");
        var def = Path.Combine(depsRoot, "platform-tools");
        var chosen = Path.Combine(depsRoot, "platform-tools");

        var stored = InstallPathResolver.Encode(chosen, def);
        var resolved = InstallPathResolver.Resolve(def, stored);

        Assert.Equal(chosen, resolved);
    }

    [Fact]
    public void IsParentMissing_NonExistentParent_ReturnsTrue()
    {
        var ghost = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "child");
        Assert.True(InstallPathResolver.IsParentMissing(ghost));
    }

    [Fact]
    public void IsParentMissing_ExistingParent_ReturnsFalse()
    {
        var parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(parent);
        try
        {
            var child = Path.Combine(parent, "child");
            Assert.False(InstallPathResolver.IsParentMissing(child));
        }
        finally
        {
            Directory.Delete(parent);
        }
    }
}
