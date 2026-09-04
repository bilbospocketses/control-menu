using System.Net;
using System.Net.Http;
using Bunit;
using ControlMenu.Modules.AndroidPowerTools.Pages;
using ControlMenu.Services;
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
    private static async Task<WsScrcpyService> WsScrcpyWithPendingRequestAsync(CountingJsonHandler? handler = null)
    {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync(It.IsAny<string>())).ReturnsAsync("http://localhost:8000");
        var provider = new ServiceCollection().AddSingleton(config.Object).BuildServiceProvider();

        handler ??= new CountingJsonHandler();
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

    [Fact]
    public async Task Status_poll_runs_on_the_injected_clock_not_a_wall_clock_timer()
    {
        // The countdown moved to TimeProvider in #128 while the poll stayed on a raw
        // System.Threading.Timer, leaving the page half on the injected clock. A poll a test
        // cannot drive is a poll a test cannot pin -- the next attempt would sleep or race.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new CountingJsonHandler();
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(await WsScrcpyWithPendingRequestAsync(handler));
        Services.AddSingleton<ILogger<AndroidPowerToolsPage>>(NullLogger<AndroidPowerToolsPage>.Instance);

        var cut = Render<AndroidPowerToolsPage>();
        Assert.Equal(0, handler.StatusPolls);

        clock.Advance(TimeSpan.FromSeconds(3));   // the page's PollInterval

        cut.WaitForAssertion(() => Assert.Equal(1, handler.StatusPolls), TimeSpan.FromSeconds(1));
    }

    /// <summary>Answers every request with JSON both the embed request and the status poll parse,
    /// and counts the status polls so a test can see whether the poll timer fired.</summary>
    private sealed class CountingJsonHandler : HttpMessageHandler
    {
        public int StatusPolls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.StartsWith("/embed-request/", StringComparison.Ordinal))
                Interlocked.Increment(ref StatusPolls);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"req-1\",\"status\":\"pending\"}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
