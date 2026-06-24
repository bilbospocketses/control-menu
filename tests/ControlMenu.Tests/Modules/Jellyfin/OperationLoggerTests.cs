using ControlMenu.Common.Paths;
using ControlMenu.Modules.Jellyfin.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class OperationLoggerTests
{
    [Fact]
    public void GetDefaultBackupDirectory_IsPure_DoesNotCreateDirectory()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cm-backupdir-" + Guid.NewGuid().ToString("N"));
        var paths = new Mock<IDataPathResolver>();
        paths.Setup(p => p.GetJellyfinBackupsDir()).Returns(temp);

        var result = OperationLogger.GetDefaultBackupDirectory(paths.Object);

        Assert.Equal(temp, result);
        Assert.False(Directory.Exists(temp), "a path getter must not create the directory");
    }
}
