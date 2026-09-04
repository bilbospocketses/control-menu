using System.Net.Http;
using Bunit;
using ControlMenu.Components.Shared;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Components.Shared;

/// <summary>
/// Behavior tests for <see cref="ScrcpyMirror"/>'s iframe focus path (Item 44).
/// The inline mirror focuses its iframe on load; on net10 the old
/// <c>HTMLElement.prototype.focus.call</c> interop identifier no longer resolves and the
/// resulting JSException tore down the Blazor circuit. The focus must (a) use the working
/// <see cref="Microsoft.AspNetCore.Components.ElementReference"/> focus path and (b) never
/// let an interop failure propagate out of the event handler.
/// </summary>
public class ScrcpyMirrorTests : BunitContext
{
    // ElementReference.FocusAsync() marshals to this Blazor internal interop identifier.
    private const string FocusInterop = "Blazor._internal.domWrapper.focus";

    /// <summary>A WsScrcpyService whose IsRunning reports true (so the inline iframe renders).
    /// IsRunning flips true only inside StartAsync, so we start a real instance over a minimal
    /// real ServiceProvider rather than mock the non-virtual property.</summary>
    private static WsScrcpyService StartedWsScrcpy(Mock<IConfigurationService>? config = null)
    {
        config ??= new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync(It.IsAny<string>())).ReturnsAsync("http://localhost:8000");

        var sp = new ServiceCollection()
            .AddSingleton(config.Object)
            .BuildServiceProvider();

        var ws = new WsScrcpyService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHttpClientFactory>(),
            NullLogger<WsScrcpyService>.Instance);
        ws.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return ws;
    }

    private IRenderedComponent<ScrcpyMirror> RenderInlineMirror()
    {
        Services.AddSingleton(StartedWsScrcpy());
        return Render<ScrcpyMirror>(p => p
            .Add(c => c.Udid, "1.2.3.4:5555")
            .Add(c => c.Inline, true));
    }

    [Fact]
    public void OnLoad_focuses_iframe_via_ElementReference_focus_path()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderInlineMirror();
        cut.Find("iframe").TriggerEvent("onload", EventArgs.Empty);

        // Fails while the component uses the broken HTMLElement.prototype.focus.call identifier.
        JSInterop.VerifyInvoke(FocusInterop);
    }

    [Fact]
    public void OnLoad_focus_interop_failure_does_not_tear_down_circuit()
    {
        // Strict mode: model net10's failing focus interop by making it throw.
        JSInterop.SetupVoid(FocusInterop, _ => true)
                 .SetException(new InvalidOperationException("focus interop unavailable"));

        var cut = RenderInlineMirror();

        var ex = Record.Exception(() => cut.Find("iframe").TriggerEvent("onload", EventArgs.Empty));

        Assert.Null(ex);
    }

    [Fact]
    public void Inline_iframe_uses_the_url_configured_now_not_the_one_cached_at_startup()
    {
        // Mirroring is the primary ws-scrcpy-web surface, and it built its src from the URL
        // resolved once at startup -- so changing the setting pointed Power Tools at the new
        // address while the mirror silently kept framing the old one until a restart.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var config = new Mock<IConfigurationService>();
        var ws = StartedWsScrcpy(config);

        // The user edits the URL in Settings. No restart.
        config.Setup(c => c.GetSettingAsync(It.IsAny<string>())).ReturnsAsync("http://localhost:9100");

        Services.AddSingleton(ws);
        var cut = Render<ScrcpyMirror>(p => p
            .Add(c => c.Udid, "1.2.3.4:5555")
            .Add(c => c.Inline, true));

        Assert.StartsWith("http://localhost:9100/embed.html", cut.Find("iframe").GetAttribute("src"));
    }
}
