using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;

namespace ControlMenu.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task CanRoundTripSetting()
    {
        using var db = TestDbContextFactory.Create();

        var setting = new Setting
        {
            Id = Guid.NewGuid(),
            Key = "theme",
            Value = "dark",
            IsSecret = false
        };

        db.Settings.Add(setting);
        await db.SaveChangesAsync();

        var loaded = await db.Settings.FindAsync(setting.Id);

        Assert.NotNull(loaded);
        Assert.Equal("theme", loaded.Key);
        Assert.Equal("dark", loaded.Value);
    }

    [Fact]
    public async Task CanRoundTripDevice()
    {
        using var db = TestDbContextFactory.Create();

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Living Room TV",
            Type = DeviceType.GoogleTV,
            MacAddress = "b8-7b-d4-f3-ae-84",
            ModuleId = "android-devices",
            AdbPort = 5555
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var loaded = await db.Devices.FindAsync(device.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Living Room TV", loaded.Name);
        Assert.Equal(DeviceType.GoogleTV, loaded.Type);
    }

    [Fact]
    public async Task CanRoundTripJob()
    {
        using var db = TestDbContextFactory.Create();

        var job = new Job
        {
            Id = Guid.NewGuid(),
            ModuleId = "jellyfin",
            JobType = "cast-crew-update",
            Status = JobStatus.Queued
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var loaded = await db.Jobs.FindAsync(job.Id);

        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Queued, loaded.Status);
    }

    [Fact]
    public async Task CanRoundTripDependency()
    {
        using var db = TestDbContextFactory.Create();

        var dep = new Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "android-devices",
            Name = "adb",
            Status = DependencyStatus.UpToDate,
            SourceType = UpdateSourceType.DirectUrl
        };

        db.Dependencies.Add(dep);
        await db.SaveChangesAsync();

        var loaded = await db.Dependencies.FindAsync(dep.Id);

        Assert.NotNull(loaded);
        Assert.Equal("adb", loaded.Name);
        Assert.Equal(DependencyStatus.UpToDate, loaded.Status);
    }

    [Fact]
    public async Task SettingUniqueIndex_ModuleIdAndKey()
    {
        using var db = TestDbContextFactory.Create();

        db.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            ModuleId = null,
            Key = "theme",
            Value = "dark"
        });
        await db.SaveChangesAsync();

        db.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            ModuleId = null,
            Key = "theme",
            Value = "light"
        });

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => db.SaveChangesAsync());
    }
}
