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
    public void GetNavEntries_ReturnsEntriesBasedOnCameraCount()
    {
        CamerasModule.CameraCount = 3;
        var entries = _sut.GetNavEntries().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal("Camera 1", entries[0].Title);
        Assert.Equal("/cameras/1", entries[0].Href);
        Assert.Equal("Camera 3", entries[2].Title);
        Assert.Equal("/cameras/3", entries[2].Href);
        CamerasModule.CameraCount = 8; // reset
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
