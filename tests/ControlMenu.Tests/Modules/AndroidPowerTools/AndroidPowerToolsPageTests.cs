using System.Net;
using System.Net.Http;
using Bunit;
using ControlMenu.Modules.AndroidPowerTools.Pages;
using ControlMenu.Services;
using ControlMenu.Tests.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidPowerTools;

/// <summary>
/// Behaviour tests for the Power Tools page's approval wait. The page is rendered in its
/// resumed state -- a <see cref="WsScrcpyService.PendingEmbed"/> already outstanding -- because
/// that is the one entry into <c>AwaitingApproval</c> that needs no HTTP round trip from the page.
/// </summary>
public class AndroidPowerToolsPageTests : BunitContext
{
    /// <summary>bUnit's NavigationManager reports http://localhost/, so this is the origin the page
    /// will compute for itself and the one the pending request must have been made for.</summary>
    private const string SelfOrigin = "http://localhost";

    /// <summary>A WsScrcpyService holding a pending embed request. ws-scrcpy-web is faked by one
    /// handler whose body satisfies both the request parser (<c>id</c>) and the status poll
    /// (<c>status: pending</c>), so the page keeps waiting for as long as the test runs.</summary>
    private static async Task<WsScrcpyService> WsScrcpyWithPendingRequestAsync()
    {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync(It.IsAny<string>())).ReturnsAsync("http://localhost:8000");
        var provider = new ServiceCollection().AddSingleton(config.Object).BuildServiceProvider();

        var handler = new MockHttpHandler("{\"id\":\"req-1\",\"status\":\"pending\"}", HttpStatusCode.OK);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));

        var ws = new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            httpFactory.Object,
            NullLogger<WsScrcpyService>.Instance);
        await ws.StartAsync(CancellationToken.None);
        Assert.Equal("req-1", await ws.RequestEmbedPermissionAsync(SelfOrigin));
        return ws;
    }

    private static string CountdownText(IRenderedComponent<AndroidPowerToolsPage> cut) =>
        cut.FindAll("p").Single(p => p.TextContent.TrimStart().StartsWith("Time remaining"))
           .QuerySelector("strong")!.TextContent;

    [Fact]
    public async Task Countdown_advances_every_second_not_only_on_the_poll_tick()
    {
        // @FormattedRemaining recomputed only when the component re-rendered, and while waiting the
        // only trigger was StateHasChanged at the end of each poll -- so the clock stepped at the
        // poll interval and sat visibly beside ws-scrcpy-web's own true one-second countdown.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(await WsScrcpyWithPendingRequestAsync());
        Services.AddSingleton<ILogger<AndroidPowerToolsPage>>(NullLogger<AndroidPowerToolsPage>.Instance);

        var cut = Render<AndroidPowerToolsPage>();
        var before = CountdownText(cut);

        clock.Advance(TimeSpan.FromSeconds(1));

        // One second of the page's clock, and no poll has fired: the display must already differ.
        cut.WaitForAssertion(() => Assert.NotEqual(before, CountdownText(cut)), TimeSpan.FromSeconds(1));
    }
}
