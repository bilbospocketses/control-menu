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

    [Fact]
    public void Running_AndroidOnly_ShowsScanningLabel_AndroidDisabled_OthersIdle()
    {
        var cut = RenderComponent<HomeScanBand>(p => p
            .Add(c => c.AndroidRunning, true)
            .Add(c => c.CamerasRunning, false)
            .Add(c => c.AllRunning, false));

        var buttons = cut.FindAll("button.scan-button");
        Assert.Contains("⏳ Scanning Android…", buttons[0].TextContent);
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.Contains("⚡ Scan Cameras", buttons[1].TextContent);
        Assert.False(buttons[1].HasAttribute("disabled"));
        Assert.Contains("⚡ Scan All", buttons[2].TextContent);
        Assert.False(buttons[2].HasAttribute("disabled"));
    }

    [Fact]
    public void Running_AllRunning_AllButtonsDisabled_AllShowRunningLabel()
    {
        var cut = RenderComponent<HomeScanBand>(p => p
            .Add(c => c.AndroidRunning, true)
            .Add(c => c.CamerasRunning, true)
            .Add(c => c.AllRunning, true));

        var buttons = cut.FindAll("button.scan-button");
        Assert.All(buttons, b => Assert.True(b.HasAttribute("disabled")));
        Assert.Contains("⏳ Scanning Android…", buttons[0].TextContent);
        Assert.Contains("⏳ Scanning Cameras…", buttons[1].TextContent);
        Assert.Contains("⏳ Scanning All…", buttons[2].TextContent);
    }

    [Fact]
    public async Task Click_AndroidButton_FiresOnScanAndroidCallback()
    {
        var fired = false;
        var cut = RenderComponent<HomeScanBand>(p => p
            .Add(c => c.OnScanAndroid, EventCallback.Factory.Create(this, () => { fired = true; })));

        await cut.Find("button.scan-button-android").ClickAsync(new());
        Assert.True(fired);
    }
}
