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
    public void GetNavEntries_Returns8Cameras()
    {
        var entries = _sut.GetNavEntries().ToList();
        Assert.Equal(8, entries.Count);
        Assert.Equal("Camera 1", entries[0].Title);
        Assert.Equal("/cameras/1", entries[0].Href);
        Assert.Equal("Camera 8", entries[7].Title);
        Assert.Equal("/cameras/8", entries[7].Href);
    }

    [Fact]
    public void Dependencies_IsEmpty() => Assert.Empty(_sut.Dependencies);
}
