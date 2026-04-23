using Microsoft.AspNetCore.Components;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeNavigationManager : NavigationManager
{
    public List<(string Uri, bool Replace)> Navigations { get; } = new();

    public FakeNavigationManager()
    {
        Initialize("http://localhost/", "http://localhost/");
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add((uri, options.ReplaceHistoryEntry));
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Navigations.Add((uri, false));
    }
}
