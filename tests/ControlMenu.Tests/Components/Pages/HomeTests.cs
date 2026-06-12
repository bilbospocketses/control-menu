using System.Reflection;
using System.Runtime.CompilerServices;
using Bunit;
using ControlMenu.Modules;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Pages;

public class HomeTests : BunitContext
{
    private readonly Mock<IConfigurationService> _config = new();

    public HomeTests()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync("true");
        Services.AddSingleton(_config.Object);
        // Home.razor injects IServiceProvider to evaluate NavEntry.IsVisible predicates.
        Services.AddSingleton<IServiceProvider>(sp => sp);
    }

    private static IToolModule MakeModule(string id, string name, params NavEntry[] entries)
    {
        var m = new Mock<IToolModule>();
        m.SetupGet(x => x.Id).Returns(id);
        m.SetupGet(x => x.DisplayName).Returns(name);
        m.SetupGet(x => x.Icon).Returns("bi-box");
        m.SetupGet(x => x.SortOrder).Returns(0);
        m.Setup(x => x.GetNavEntries()).Returns(entries);
        return m.Object;
    }

    // Bypass the reflection-based ModuleDiscoveryService ctor by setting the
    // compiler-generated backing field directly (mirrors the deleted HomeModuleTilesTests).
    private static ModuleDiscoveryService MakeDiscovery(IEnumerable<IToolModule> modules)
    {
        var svc = (ModuleDiscoveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleDiscoveryService));
        var field = typeof(ModuleDiscoveryService)
            .GetField("<Modules>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(svc, (IReadOnlyList<IToolModule>)modules.ToList());
        return svc;
    }

    private void RegisterDiscovery(params IToolModule[] modules)
        => Services.AddSingleton(MakeDiscovery(modules));

    [Fact]
    public void SetupComplete_NoModules_RendersHeroAndEmptyState()
    {
        RegisterDiscovery();

        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Single(cut.FindAll(".hero"));
        Assert.Single(cut.FindAll(".empty-state"));
        Assert.Empty(cut.FindAll(".module-grid"));
    }

    [Fact]
    public void Module_WithVisibleEntries_RendersCardWithPillPerEntry()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", "bi-image", 0),
            new NavEntry("Tracing", "/imaging/tracing", "bi-pencil", 1)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.Contains("Imaging Tools", headings);

        var card = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Imaging Tools");
        var pills = card.QuerySelectorAll(".module-links a.pill-link").ToList();
        Assert.Equal(2, pills.Count);
        Assert.Equal("/imaging/icon-converter", pills[0].GetAttribute("href"));
        Assert.Equal("/imaging/tracing", pills[1].GetAttribute("href"));
    }

    [Fact]
    public void Module_WithNoVisibleEntries_IsHidden()
    {
        // Cameras-style: GetNavEntries() returns nothing when no cameras are registered.
        RegisterDiscovery(MakeModule("cameras", "Cameras"));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.DoesNotContain("Cameras", headings);
        // Only the always-present Settings card remains.
        Assert.Single(cut.FindAll(".module-card"));
    }

    [Fact]
    public void Module_AllEntriesHiddenByPredicate_IsHidden()
    {
        RegisterDiscovery(MakeModule("m", "Hidden Mod",
            new NavEntry("Nope", "/nope", null, 0, _ => false)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.DoesNotContain("Hidden Mod", headings);
    }

    [Fact]
    public void Module_PartiallyHiddenEntries_RendersOnlyVisiblePills()
    {
        RegisterDiscovery(MakeModule("m", "Mod",
            new NavEntry("Hidden", "/hidden", null, 0, _ => false),
            new NavEntry("Shown", "/shown", null, 1)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var card = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Mod");
        var pills = card.QuerySelectorAll(".module-links a.pill-link").ToList();
        Assert.Single(pills);
        Assert.Equal("/shown", pills[0].GetAttribute("href"));
    }

    [Fact]
    public void SettingsCard_LinksToCanonicalSections()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", null, 0)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var settings = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Settings");
        var hrefs = settings.QuerySelectorAll("a.pill-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(new[]
        {
            "/settings/general",
            "/settings/jellyfin",
            "/settings/devices",
            "/settings/cameras",
            "/settings/dependencies",
        }, hrefs);
    }

    [Fact]
    public void NoScannerUi_OnHome()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", null, 0)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        // Discovery-dashboard artifacts must be gone.
        Assert.Empty(cut.FindAll(".home-tiles-band"));
        Assert.Empty(cut.FindAll(".home-status"));
        Assert.Single(cut.FindAll(".module-grid"));
    }

    [Fact]
    public void SetupNotComplete_RendersWizardOnly()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync((string?)null);
        RegisterDiscovery();

        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Empty(cut.FindAll(".home-container"));
    }
}
