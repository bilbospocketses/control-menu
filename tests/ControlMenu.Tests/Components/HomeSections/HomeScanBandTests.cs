using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeScanBandTests : TestContext
{
    [Fact]
    public void Idle_RendersThreeButtons_FixedWidth_WithIdleLabels()
    {
        var cut = RenderComponent<HomeScanBand>(p => p
            .Add(c => c.AndroidRunning, false)
            .Add(c => c.CamerasRunning, false)
            .Add(c => c.AllRunning, false)
            .Add(c => c.OnScanAndroid, EventCallback.Empty)
            .Add(c => c.OnScanCameras, EventCallback.Empty)
            .Add(c => c.OnScanAll, EventCallback.Empty));

        var buttons = cut.FindAll("button.scan-button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("Scan Android", buttons[0].TextContent);
        Assert.Contains("Scan Cameras", buttons[1].TextContent);
        Assert.Contains("Scan All", buttons[2].TextContent);
        // Each button must have the fixed-width class regardless of state
        Assert.All(buttons, b => Assert.Contains("scan-button", b.GetAttribute("class")!));
    }
}
