using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsSectionTests : BunitContext
{
    [Fact]
    public void Renders_TitleAndChildContent()
    {
        var cut = Render<SettingsSection>(parameters => parameters
            .Add(p => p.Title, "Email (SMTP)")
            .AddChildContent("<p data-testid=\"body\">hello</p>")
        );

        Assert.Contains("Email (SMTP)", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=\"body\"]"));
    }

    [Fact]
    public void Renders_TitleAsHeader()
    {
        var cut = Render<SettingsSection>(parameters => parameters
            .Add(p => p.Title, "General")
            .AddChildContent("<span/>")
        );

        var titleEl = cut.Find(".settings-section-title");
        Assert.Equal("General", titleEl.TextContent.Trim());
    }
}
