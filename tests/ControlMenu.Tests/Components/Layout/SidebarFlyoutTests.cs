using Bunit;
using Bunit.TestDoubles;
using ControlMenu.Components.Layout;
using ControlMenu.Modules;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace ControlMenu.Tests.Components.Layout;

public class SidebarFlyoutTests : TestContext
{
    private readonly Mock<IDeviceTypeCache> _cache = new();
    private readonly Mock<ICameraChangeNotifier> _cameraNotifier = new();

    public SidebarFlyoutTests()
    {
        _cache.SetupAdd(c => c.CacheUpdated += It.IsAny<Action?>());
        _cache.SetupRemove(c => c.CacheUpdated -= It.IsAny<Action?>());
        _cache.Setup(c => c.RefreshAsync()).Returns(Task.CompletedTask);

        _cameraNotifier.SetupAdd(n => n.CamerasChanged += It.IsAny<Action?>());
        _cameraNotifier.SetupRemove(n => n.CamerasChanged -= It.IsAny<Action?>());

        Services.AddSingleton(_cache.Object);
        Services.AddSingleton(_cameraNotifier.Object);
        Services.AddSingleton<IServiceProvider>(sp => sp);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<double>("controlMenu.getModuleIconRectTop", _ => true).SetResult(123.0);
        JSInterop.Setup<int>("controlMenu.subscribeFlyoutResize", _ => true).SetResult(1);
        JSInterop.SetupVoid("controlMenu.unsubscribeFlyoutResize", _ => true);
    }

    private void RegisterDiscovery(params IToolModule[] modules)
    {
        var discovery = MakeDiscovery(modules);
        var existing = Services.FirstOrDefault(d => d.ServiceType == typeof(ModuleDiscoveryService));
        if (existing is not null) Services.Remove(existing);
        Services.AddSingleton(discovery);
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

    [Fact]
    public void Collapsed_ClickIcon_OpensFlyoutForModule()
    {
        RegisterDiscovery(
            MakeModule("test-mod-1", "Test Module One", "bi-gear",
                new NavEntry("Page A", "/test-mod-1/a", "bi-circle")),
            MakeModule("test-mod-2", "Test Module Two", "bi-box",
                new NavEntry("Page B", "/test-mod-2/b", "bi-square")));

        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();

        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        var flyout = cut.Find(".sidebar-flyout");
        Assert.NotNull(flyout);
        Assert.Contains("Test Module One", flyout.TextContent);
        Assert.Contains("Page A", flyout.TextContent);
    }

    [Fact]
    public void Collapsed_ClickDifferentIcon_SwapsActiveFlyout()
    {
        RegisterDiscovery(
            MakeModule("test-mod-1", "Test Module One", "bi-gear",
                new NavEntry("Page A", "/test-mod-1/a", "bi-circle")),
            MakeModule("test-mod-2", "Test Module Two", "bi-box",
                new NavEntry("Page B", "/test-mod-2/b", "bi-square")));

        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();

        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();
        Assert.Contains("Test Module One", cut.Find(".sidebar-flyout-header").TextContent);

        cut.Find("[data-flyout-anchor=\"test-mod-2\"]").Click();
        Assert.Contains("Test Module Two", cut.Find(".sidebar-flyout-header").TextContent);

        Assert.Single(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void Collapsed_ClickBackdrop_ClosesFlyout()
    {
        RegisterDiscovery(
            MakeModule("test-mod-1", "Test Module One", "bi-gear",
                new NavEntry("Page A", "/test-mod-1/a", "bi-circle")));

        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();
        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        Assert.NotEmpty(cut.FindAll(".sidebar-flyout"));

        cut.Find(".sidebar-flyout-backdrop").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void NotCollapsed_ClickIcon_DoesNotOpenFlyout()
    {
        RegisterDiscovery(
            MakeModule("test-mod-1", "Test Module One", "bi-gear",
                new NavEntry("Page A", "/test-mod-1/a", "bi-circle")));

        var cut = RenderComponent<Sidebar>();
        // Sidebar starts expanded — clicking the group header toggles expansion, not flyout.
        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void Collapsed_EmptyModule_ClickIcon_NoFlyout()
    {
        RegisterDiscovery(
            MakeModule("empty-mod", "Empty", "bi-x"));  // no nav entries

        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();
        cut.Find("[data-flyout-anchor=\"empty-mod\"]").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IToolModule MakeModule(string id, string displayName, string icon,
        params NavEntry[] entries)
    {
        return new InlineModule(id, displayName, icon, entries);
    }

    private sealed class InlineModule : IToolModule
    {
        private readonly NavEntry[] _entries;

        public InlineModule(string id, string displayName, string icon, NavEntry[] entries)
        {
            Id = id;
            DisplayName = displayName;
            Icon = icon;
            _entries = entries;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Icon { get; }
        public int SortOrder => 0;
        public IEnumerable<ModuleDependency> Dependencies => [];
        public IEnumerable<ConfigRequirement> ConfigRequirements => [];
        public IEnumerable<NavEntry> GetNavEntries() => _entries;
        public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
    }
}
