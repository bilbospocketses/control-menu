using ControlMenu.Data.Enums;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Jellyfin.Workers;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class MediaCardRefreshWorkerTests
{
    private static readonly JellyfinApiConfig TestApiConfig = new("http://localhost:8096", "test-key", "test-user");

    private readonly Mock<IJellyfinService> _mockJellyfin = new();
    private readonly Mock<IBackgroundJobService> _mockJobService = new();
    private readonly Mock<IEmailService> _mockEmail = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();

    public MediaCardRefreshWorkerTests()
    {
        _mockJellyfin.Setup(j => j.GetApiConfigAsync()).ReturnsAsync(TestApiConfig);
        _mockConfig.Setup(c => c.GetSettingAsync("notification-email", It.IsAny<string?>()))
            .ReturnsAsync("test@example.com");
        _mockEmail.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        // Default happy path: a backup is taken, and the card comes back after the refresh.
        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:/backups/card.png");
        _mockJellyfin.Setup(j => j.HasLibraryCardAsync(It.IsAny<string>(),
                It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private MediaCardRefreshWorker CreateWorker() => new(
        _mockJellyfin.Object, _mockJobService.Object, _mockEmail.Object, _mockConfig.Object,
        logger: null,
        pollInterval: TimeSpan.FromMilliseconds(1),
        cardTimeout: TimeSpan.FromMilliseconds(50));

    private Guid SetupRunningJob()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new ControlMenu.Data.Entities.Job
            {
                Id = jobId,
                ModuleId = "jellyfin",
                JobType = "media-card-refresh",
                Status = JobStatus.Running
            });
        return jobId;
    }

    [Fact]
    public async Task ExecuteAsync_PrunesBackupsOnceTheRunHasEnded()
    {
        // Retention otherwise runs only from the DB Date Update routine. A user who regenerates
        // cards but never runs that routine would keep every card backup ever written.
        var jobId = SetupRunningJob();

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.CleanupOldBackupsAsync(It.IsAny<OperationLogger?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BacksUpThenDeletesThenRefreshes_ForEachSelectedLibrary()
    {
        var jobId = SetupRunningJob();
        var calls = new List<string>();

        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync("lib-1", It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:/backups/movies.png").Callback(() => calls.Add("backup"));
        _mockJellyfin.Setup(j => j.DeleteLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => calls.Add("delete"));
        _mockJellyfin.Setup(j => j.RefreshLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => calls.Add("refresh"));

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        // The order is load-bearing: the collage provider only fires when no image exists, and a
        // delete with no backup behind it is unrecoverable.
        Assert.Equal(["backup", "delete", "refresh"], calls);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TouchesOnlyTheSelectedLibraries()
    {
        var jobId = SetupRunningJob();

        await CreateWorker().ExecuteAsync(jobId, ["lib-1", "lib-3"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync("lib-3", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync("lib-2", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NeverDeletesWhenTheBackupFails()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync("lib-1", It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        // Deleting a card we could not back up destroys a hand-made card with no way back.
        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockJellyfin.Verify(j => j.RefreshLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProceedsWhenThereWasNoCardToBackUp()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync("lib-1", It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        // null means "no existing card", which is not a failure -- it is the whole point of the job.
        _mockJellyfin.Verify(j => j.RefreshLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FailsTheJob_WhenTheCardNeverComesBack()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.HasLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        // Reporting success on a library whose card is now simply gone would be the worst outcome.
        _mockJobService.Verify(j => j.FailJobAsync(jobId, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PutsTheBackupBack_WhenTheCardNeverComesBack()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync("lib-1", It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:/backups/movies.png");
        _mockJellyfin.Setup(j => j.HasLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        // A failed run must never leave the tile blank with the only copy in a folder the user has
        // to find and re-upload by hand -- that is what happened on 2026-08-30.
        _mockJellyfin.Verify(j => j.RestoreLibraryCardAsync("lib-1", "C:/backups/movies.png",
            It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PutsTheBackupBack_WhenTheRefreshRequestFails()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.RefreshLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("500"));

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.RestoreLibraryCardAsync("lib-1", It.IsAny<string>(),
            It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRestore_WhenThereWasNoCardToBeginWith()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.BackupLibraryCardAsync("lib-1", It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockJellyfin.Setup(j => j.HasLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.RestoreLibraryCardAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_StopsMidWait_WhenCancelIsClickedDuringTheWait()
    {
        var jobId = Guid.NewGuid();
        var job = new ControlMenu.Data.Entities.Job
        {
            Id = jobId, ModuleId = "jellyfin", JobType = "media-card-refresh", Status = JobStatus.Running
        };
        _mockJobService.Setup(j => j.GetJobAsync(jobId)).ReturnsAsync(job);

        // The card never appears; the user clicks Cancel while the first library is still waiting.
        _mockJellyfin.Setup(j => j.HasLibraryCardAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { job.CancellationRequested = true; return false; });

        await CreateWorker().ExecuteAsync(jobId, ["lib-1", "lib-2"], CancellationToken.None);

        // Checking the flag only BETWEEN libraries meant a click during a 3-minute wait did nothing
        // and the next library was deleted anyway.
        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync("lib-2", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesToTheNextLibrary_AfterOneFails()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.RefreshLibraryCardAsync("lib-1", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("500"));

        await CreateWorker().ExecuteAsync(jobId, ["lib-1", "lib-2"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.RefreshLibraryCardAsync("lib-2", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationIsRequestedInTheDatabase()
    {
        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new ControlMenu.Data.Entities.Job
            {
                Id = jobId,
                ModuleId = "jellyfin",
                JobType = "media-card-refresh",
                Status = JobStatus.Running,
                CancellationRequested = true
            });

        await CreateWorker().ExecuteAsync(jobId, ["lib-1", "lib-2"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockJobService.Verify(j => j.FailJobAsync(jobId, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesImmediately_WhenNothingIsSelected()
    {
        var jobId = SetupRunningJob();

        await CreateWorker().ExecuteAsync(jobId, [], CancellationToken.None);

        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RefusesATargetJellyfinCannotRegenerate()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.GetMediaCardTargetsAsync(It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCardTarget[]
            {
                new("view-livetv", "Live TV", "livetv", "UserView", true, false, "Jellyfin never generates a Live TV card.")
            });

        await CreateWorker().ExecuteAsync(jobId, ["view-livetv"], CancellationToken.None);

        // The page disables these, but a stale page could still submit one -- and a deleted Live TV
        // card cannot be rebuilt by Jellyfin at all.
        _mockJellyfin.Verify(j => j.BackupLibraryCardAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockJellyfin.Verify(j => j.DeleteLibraryCardAsync(It.IsAny<string>(), It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockJobService.Verify(j => j.FailJobAsync(jobId, It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ProceedsForARegenerableTarget()
    {
        var jobId = SetupRunningJob();
        _mockJellyfin.Setup(j => j.GetMediaCardTargetsAsync(It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCardTarget[]
            {
                new("view-plist", "Playlists", "playlists", "UserView", true, true, null)
            });

        await CreateWorker().ExecuteAsync(jobId, ["view-plist"], CancellationToken.None);

        _mockJellyfin.Verify(j => j.RefreshLibraryCardAsync("view-plist", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        // The name comes from the target list, not the raw id.
        _mockJellyfin.Verify(j => j.BackupLibraryCardAsync("view-plist", "Playlists", It.IsAny<JellyfinApiConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SendsANotificationNamingWhatChanged()
    {
        var jobId = SetupRunningJob();
        string? body = null;
        _mockEmail.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, b, _) => body = b)
            .ReturnsAsync((true, (string?)null));

        await CreateWorker().ExecuteAsync(jobId, ["lib-1"], CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("1 regenerated", body);
    }
}
