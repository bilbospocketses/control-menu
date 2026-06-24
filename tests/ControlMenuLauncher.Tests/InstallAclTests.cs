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
        Assert.Empty(Directory.GetFiles(_tempDir, ".controlmenu-write-test*"));
    }

    [Fact]
    public void IsWritable_ReturnsFalseForNonexistentPath()
    {
        var nonexistent = Path.Combine(_tempDir, "does-not-exist");
        Assert.False(InstallAcl.IsWritable(nonexistent));
    }

    [Fact]
    public async Task IsWritable_ConcurrentCalls_AllSucceed_NoLeftoverSentinels()
    {
        // Per-call unique sentinel names mean concurrent probes never delete each other's file — a
        // fixed name could race (one probe removing another's sentinel between its write and delete).
        var results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => InstallAcl.IsWritable(_tempDir))));
        Assert.All(results, r => Assert.True(r));
        Assert.Empty(Directory.GetFiles(_tempDir, ".controlmenu-write-test*"));
    }
}
