using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Modules;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeModuleTilesTests : BunitContext
{
    public HomeModuleTilesTests()
    {
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

    /// <summary>
    /// Bypass the reflection-based ModuleDiscoveryService constructor by using
    /// GetUninitializedObject + reflection to set the backing field directly.
    /// </summary>
    private static ModuleDiscoveryService MakeDiscovery(IEnumerable<IToolModule> modules)
    {
        var svc = (ModuleDiscoveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleDiscoveryService));
        var field = typeof(ModuleDiscoveryService)
            .GetField("<Modules>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(svc, (IReadOnlyList<IToolModule>)modules.ToList());
        return svc;
    }

    private void RegisterDiscovery(params IToolModule[] modules)
    {
        Services.AddSingleton(MakeDiscovery(modules));
    }

    [Fact]
    public void Tile_ResolvesToLowestSortOrderEntry()
    {
        RegisterDiscovery(MakeModule("android-devices", "Android Devices",
            new NavEntry("Device List", "/android/devices", "/i.svg", 0),
            new NavEntry("Google TV",   "/android/googletv", "/i.svg", 1)));

        var cut = Render<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='android-devices']");
        Assert.Equal("/android/devices", anchor.GetAttribute("href"));
    }

    [Fact]
    public void Tile_HonorsVisibilityPredicate_PicksFirstVisible()
    {
        // First entry is hidden; should pick second
        RegisterDiscovery(MakeModule("test-mod", "Test",
            new NavEntry("Hidden", "/hidden", "/i.svg", 0, _ => false),
            new NavEntry("Visible", "/visible", "/i.svg", 1)));

        var cut = Render<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='test-mod']");
        Assert.Equal("/visible", anchor.GetAttribute("href"));
    }

    [Fact]
    public void CamerasModule_ZeroEntries_FallsBackToSettingsCameras()
    {
        // Cameras module yields zero nav entries when no cameras are registered
        RegisterDiscovery(MakeModule("cameras", "Cameras"));

        var cut = Render<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='cameras']");
        Assert.Equal("/settings/cameras", anchor.GetAttribute("href"));
    }

    [Fact]
    public void SettingsTile_AlwaysRoutesTo_SettingsGeneral()
    {
        RegisterDiscovery(); // no modules — Settings tile is hard-coded
        var cut = Render<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='settings']");
        Assert.Equal("/settings/general", anchor.GetAttribute("href"));
    }
}
