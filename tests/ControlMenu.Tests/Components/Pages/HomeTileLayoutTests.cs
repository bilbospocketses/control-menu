using ControlMenu.Components.Pages;

namespace ControlMenu.Tests.Components.Pages;

public class HomeTileLayoutTests
{
    [Theory]
    [InlineData(0, 1)]   // defensive: never less than one unit
    [InlineData(1, 1)]
    [InlineData(3, 1)]   // up to 3 links fit in one tile-unit
    [InlineData(4, 2)]
    [InlineData(6, 2)]   // Imaging Tools today
    [InlineData(7, 3)]
    [InlineData(10, 4)]  // future tall card
    public void InitialSpan_RoundsUpToWholeTileUnits(int entries, int expected)
    {
        Assert.Equal(expected, HomeTileLayout.InitialSpan(entries));
    }
}
