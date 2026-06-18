using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class CamerasModuleTests
{
    private readonly CamerasModule _sut = new();

    [Fact]
    public void Id_IsCameras() => Assert.Equal("cameras", _sut.Id);

    [Fact]
    public void DisplayName_IsCameras() => Assert.Equal("Cameras", _sut.DisplayName);

    [Fact]
    public void SortOrder_Is5() => Assert.Equal(5, _sut.SortOrder);

    [Fact]
    public void GetNavEntries_ReturnsEntriesFromEnabledCameras()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        CamerasModule.EnabledCameras = new()
        {
            (id1, "Front Door"),
            (id2, "Backyard"),
            (id3, "Garage"),
        };
        var entries = _sut.GetNavEntries().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal("Front Door", entries[0].Title);
        Assert.Equal($"/cameras/{id1:N}", entries[0].Href);
        Assert.Equal("Garage", entries[2].Title);
        Assert.Equal($"/cameras/{id3:N}", entries[2].Href);
        CamerasModule.EnabledCameras = new(); // reset
    }

    [Fact]
    public void Dependencies_ContainsGo2Rtc()
    {
        var deps = _sut.Dependencies.ToList();
        Assert.Single(deps);
        Assert.Equal("go2rtc", deps[0].Name);
        Assert.Equal("go2rtc.exe", deps[0].ExecutableName);
    }

    [Fact]
    public void ProjectEnabledNav_returns_only_enabled_cameras_as_id_and_name()
    {
        var front = new Camera { Id = Guid.NewGuid(), Name = "Front", IpAddress = "1.1.1.1", Enabled = true };
        var off = new Camera { Id = Guid.NewGuid(), Name = "Off", IpAddress = "1.1.1.2", Enabled = false };
        var back = new Camera { Id = Guid.NewGuid(), Name = "Back", IpAddress = "1.1.1.3", Enabled = true };

        var nav = CamerasModule.ProjectEnabledNav(new[] { front, off, back });

        Assert.Equal(2, nav.Count);
        Assert.Equal((front.Id, "Front"), nav[0]);
        Assert.Equal((back.Id, "Back"), nav[1]);
    }

    [Fact]
    public async Task RefreshEnabledNavAsync_sets_EnabledCameras_from_a_fresh_scope()
    {
        var cam = new Camera { Id = Guid.NewGuid(), Name = "Front", IpAddress = "1.1.1.1", Enabled = true };
        var cameraService = new Mock<ICameraService>();
        cameraService.Setup(s => s.GetAllAsync()).ReturnsAsync(new[] { cam });

        CamerasModule.EnabledCameras = new();
        await CamerasModule.RefreshEnabledNavAsync(ScopeFactoryReturning(cameraService.Object), NullLogger.Instance);

        Assert.Single(CamerasModule.EnabledCameras);
        Assert.Equal((cam.Id, "Front"), CamerasModule.EnabledCameras[0]);
        CamerasModule.EnabledCameras = new();
    }

    [Fact]
    public async Task RefreshEnabledNavAsync_when_service_throws_swallows_and_does_not_propagate()
    {
        var cameraService = new Mock<ICameraService>();
        cameraService.Setup(s => s.GetAllAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        var ex = await Record.ExceptionAsync(
            () => CamerasModule.RefreshEnabledNavAsync(ScopeFactoryReturning(cameraService.Object), NullLogger.Instance));

        Assert.Null(ex);
    }

    private static IServiceScopeFactory ScopeFactoryReturning(ICameraService cameraService)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ICameraService))).Returns(cameraService);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
