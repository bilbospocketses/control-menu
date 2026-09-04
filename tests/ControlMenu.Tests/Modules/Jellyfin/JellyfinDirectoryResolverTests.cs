using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinDirectoryResolverTests
{
    private static Mock<IConfigurationService> ConfigReturning(
        string? backupOverride = null,
        string? logOverride = null)
    {
        var mock = new Mock<IConfigurationService>();
        mock.Setup(c => c.GetSettingAsync("jellyfin-backup-directory", null))
            .ReturnsAsync(backupOverride);
        mock.Setup(c => c.GetSettingAsync("jellyfin-log-directory", null))
            .ReturnsAsync(logOverride);
        return mock;
    }

    [Fact]
    public async Task GetBackupDirectory_NoOverride_ReturnsDefault()
    {
        using var temp = new TempDir();
        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultBackupDirectory(paths), dir);
    }

    [Fact]
    public async Task GetBackupDirectory_OverrideSet_ReturnsOverride()
    {
        using var temp = new TempDir();
        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(backupOverride: "D:\\custom\\backups").Object, paths);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal("D:\\custom\\backups", dir);
    }

    [Fact]
    public async Task MigrateBackupsAsync_MovesTheMediaCardsFolderWithTheDatabases()
    {
        // The Settings page migrated *.db and left media-cards/ behind at the old path, so the
        // Media Cards page's Restore button -- which reads from the *current* path -- found nothing.
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(Path.Combine(oldDir, "media-cards"));
        File.WriteAllText(Path.Combine(oldDir, "jellyfin_a.db"), "db");
        File.WriteAllText(Path.Combine(oldDir, "media-cards", "Movies-20260101-000000.png"), "png");

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, new TestPathResolver(temp.Path));
        var result = await resolver.MigrateBackupsAsync(oldDir, newDir);

        Assert.True(result.Success);
        Assert.Equal(2, result.MovedCount);
        Assert.True(File.Exists(Path.Combine(newDir, "jellyfin_a.db")));
        Assert.True(File.Exists(Path.Combine(newDir, "media-cards", "Movies-20260101-000000.png")));
        Assert.False(File.Exists(Path.Combine(oldDir, "media-cards", "Movies-20260101-000000.png")));
    }

    [Fact]
    public void GetBackupStats_CountsCardBackupsAlongsideTheDatabases()
    {
        // The Backups stat globbed *.db at the root, so the disk the card backups occupied
        // was invisible in the UI.
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "media-cards"));
        File.WriteAllText(Path.Combine(temp.Path, "jellyfin_a.db"), "12345");
        File.WriteAllText(Path.Combine(temp.Path, "media-cards", "Movies-20260101-000000.png"), "1234567");
        File.WriteAllText(Path.Combine(temp.Path, "unrelated.txt"), "x");   // never counted

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, new TestPathResolver(temp.Path));
        var stats = resolver.GetBackupStats(temp.Path);

        Assert.Equal(2, stats.FileCount);
        Assert.Equal(12, stats.TotalBytes);
    }

    [Fact]
    public async Task GetLogDirectory_NoOverride_ReturnsDefault()
    {
        using var temp = new TempDir();
        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var dir = await resolver.GetLogDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultLogDirectory(paths), dir);
    }

    [Fact]
    public async Task GetLogDirectory_OverrideSet_ReturnsOverride()
    {
        using var temp = new TempDir();
        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(logOverride: "D:\\custom\\logs").Object, paths);
        var dir = await resolver.GetLogDirectoryAsync();
        Assert.Equal("D:\\custom\\logs", dir);
    }

    [Fact]
    public async Task GetBackupDirectory_OverrideEmpty_ReturnsDefault()
    {
        using var temp = new TempDir();
        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(backupOverride: "").Object, paths);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultBackupDirectory(paths), dir);
    }

    [Fact]
    public async Task MigrateBackupDirectory_NoOldFiles_ReturnsZeroMoved()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);

        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(0, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public async Task MigrateBackupDirectory_HappyPath_AllFilesMoved()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "a.db"), "x");
        File.WriteAllText(Path.Combine(oldDir, "b.db"), "y");

        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(2, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(Path.Combine(newDir, "a.db")));
        Assert.True(File.Exists(Path.Combine(newDir, "b.db")));
        Assert.Empty(Directory.GetFiles(oldDir, "*.db"));
    }

    [Fact]
    public async Task MigrateBackupDirectory_TargetDirNotCreatable_ReturnsError()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        Directory.CreateDirectory(oldDir);
        // Path with NUL char on Windows is invalid and cannot be created
        var badNewDir = Path.Combine(temp.Path, "bad\0path");

        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var result = await resolver.MigrateFilesAsync(oldDir, badNewDir, "*.db");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task MigrateBackupDirectory_OneFileLocked_ReturnsPartialResult()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "a.db"), "x");
        var lockedPath = Path.Combine(oldDir, "locked.db");
        File.WriteAllText(lockedPath, "y");

        // Hold an exclusive lock on locked.db
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var paths = new TestPathResolver(temp.Path);
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object, paths);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(1, result.MovedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.FailedFiles);
        Assert.Equal("locked.db", result.FailedFiles[0]);
        Assert.True(File.Exists(Path.Combine(newDir, "a.db")));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
