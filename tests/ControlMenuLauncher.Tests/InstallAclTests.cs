using ControlMenu.Launcher;
using Xunit;

namespace ControlMenu.Launcher.Tests;

public class InstallAclTests : IDisposable
{
    private readonly string _tempDir;

    public InstallAclTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm-acl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void IsWritable_ReturnsTrueForUserOwnedTempDir()
    {
        Assert.True(InstallAcl.IsWritable(_tempDir));
    }

    [Fact]
    public void IsWritable_DoesNotLeaveSentinelBehind()
    {
        InstallAcl.IsWritable(_tempDir);
        var sentinel = Path.Combine(_tempDir, ".controlmenu-write-test");
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public void IsWritable_ReturnsFalseForNonexistentPath()
    {
        var nonexistent = Path.Combine(_tempDir, "does-not-exist");
        Assert.False(InstallAcl.IsWritable(nonexistent));
    }
}
