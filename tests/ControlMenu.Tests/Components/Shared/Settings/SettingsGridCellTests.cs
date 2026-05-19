using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsGridCellTests : BunitContext
{
    [Fact]
    public void Renders_AllSlotsWhenProvided()
    {
        var cut = Render<SettingsGridCell>(parameters => parameters
            .Add(p => p.Label, b => b.AddContent(0, "Theme"))
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<button data-testid=\"ctl\">go</button>"))
            .Add(p => p.Hint, b => b.AddContent(0, "extra info"))
        );

        Assert.Contains("Theme", cut.Find(".settings-grid-cell-label").TextContent);
        Assert.NotNull(cut.Find("[data-testid=\"ctl\"]"));
        Assert.Contains("extra info", cut.Find(".settings-grid-cell-hint").TextContent);
    }

    [Fact]
    public void OmitsLabelHeader_WhenLabelSlotEmpty()
    {
        var cut = Render<SettingsGridCell>(parameters => parameters
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<button>save</button>"))
        );

        Assert.Empty(cut.FindAll(".settings-grid-cell-label"));
    }

    [Fact]
    public void OmitsHint_WhenHintSlotEmpty()
    {
        var cut = Render<SettingsGridCell>(parameters => parameters
            .Add(p => p.Label, b => b.AddContent(0, "X"))
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<input/>"))
        );

        Assert.Empty(cut.FindAll(".settings-grid-cell-hint"));
    }

    [Fact]
    public void FullRow_AppliesGridColumnSpan()
    {
        var cut = Render<SettingsGridCell>(parameters => parameters
            .Add(p => p.FullRow, true)
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<input/>"))
        );

        var cell = cut.Find(".settings-grid-cell");
        Assert.Contains("settings-grid-cell-full", cell.GetAttribute("class") ?? "");
    }
}
