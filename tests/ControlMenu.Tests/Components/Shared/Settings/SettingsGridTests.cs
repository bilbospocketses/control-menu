using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsGridTests : TestContext
{
    [Fact]
    public void Renders_ChildrenInGridContainer()
    {
        var cut = RenderComponent<SettingsGrid>(parameters => parameters
            .AddChildContent("<div data-testid=\"a\"/><div data-testid=\"b\"/>")
        );

        var grid = cut.Find(".settings-grid");
        Assert.NotNull(grid);
        Assert.NotNull(cut.Find("[data-testid=\"a\"]"));
        Assert.NotNull(cut.Find("[data-testid=\"b\"]"));
    }
}
