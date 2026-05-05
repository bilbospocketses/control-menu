using ControlMenu.Modules.Cameras;

namespace ControlMenu.Tests.Modules.Cameras;

public class CamerasModuleTests
{
    private readonly CamerasModule _sut = new();

    [Fact]
    public void Id_IsCameras() => Assert.Equal("cameras", _sut.Id);

    [Fact]
    public void DisplayName_IsCameras() => Assert.Equal("Cameras", _sut.DisplayName);

    [Fact]
    public void SortOrder_Is4() => Assert.Equal(4, _sut.SortOrder);

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
}
