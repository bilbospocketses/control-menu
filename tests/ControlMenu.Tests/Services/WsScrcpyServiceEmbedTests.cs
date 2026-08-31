using System.Net;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace ControlMenu.Tests.Services;

/// <summary>
/// The framing decision is read from the same headers the browser evaluates. Getting it wrong is
/// costly in both directions: a false "blocked" hides a working page behind a nag, and a false
/// "allowed" renders an iframe the browser refuses with "localhost refused to connect", which
/// reads as the server being down.
/// </summary>
public class WsScrcpyServiceEmbedTests
{
    private const string SelfOrigin = "http://localhost:5159";

    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<ILogger<WsScrcpyService>> _mockLogger = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();

    public WsScrcpyServiceEmbedTests()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync("http://localhost:8000");
    }

    private WsScrcpyService CreateService(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new EmbedScriptedHandler(respond);
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var services = new ServiceCollection();
        services.AddScoped(_ => _mockConfig.Object);
        var provider = services.BuildServiceProvider();
        return new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _mockHttpFactory.Object,
            _mockLogger.Object);
    }

    private static HttpResponseMessage WithHeaders(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }

    [Fact]
    public async Task CheckEmbedAsync_ReportsUnreachable_WhenTheServerDoesNotAnswer()
    {
        var service = CreateService(_ => throw new HttpRequestException("connection refused"));

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.False(result.Reachable);
        Assert.False(result.CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_BlocksOnSameOrigin()
    {
        var service = CreateService(_ => WithHeaders(("X-Frame-Options", "SAMEORIGIN")));

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.True(result.Reachable);
        Assert.False(result.CanEmbed);
        Assert.Contains("SAMEORIGIN", result.Reason);
    }

    [Fact]
    public async Task CheckEmbedAsync_BlocksOnDeny()
    {
        var service = CreateService(_ => WithHeaders(("X-Frame-Options", "DENY")));

        Assert.False((await service.CheckEmbedAsync(SelfOrigin)).CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_TreatsAnErrorStatusAsNotEmbeddable()
    {
        // An error body says nothing about framing, so reading headers off it defaulted to
        // "embeddable" — a host-allowlist 403 then rendered as an iframe full of JSON error.
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"error":"forbidden"}"""),
        });

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.True(result.Reachable);
        Assert.False(result.CanEmbed);
        Assert.Contains("403", result.Reason);
    }

    [Fact]
    public async Task CheckEmbedAsync_ReportsUnreachable_ForAMalformedConfiguredUrl()
    {
        // A relative URI with no BaseAddress makes HttpClient throw InvalidOperationException,
        // which is not HttpRequestException — the old filter let it escape into the Blazor
        // circuit instead of showing this page's own "not reachable" panel.
        // (A scheme-typo like "localhost:8000" throws NotSupportedException on the same path in
        // production; it cannot be reproduced here because a stubbed handler bypasses
        // HttpClient's scheme validation. IsTransportFailure covers both.)
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync("not a url at all");
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.False(result.Reachable);
        Assert.False(result.CanEmbed);
    }

    [Theory]
    [InlineData("http://localhost:8000/", "http://localhost:8000")]
    [InlineData("  http://localhost:8000  ", "http://localhost:8000")]
    [InlineData("", "http://localhost:8000")]
    [InlineData("   ", "http://localhost:8000")]
    public async Task GetBaseUrlAsync_NormalisesTheStoredValue(string stored, string expected)
    {
        // Consumers concatenate onto this, so a trailing slash produces "//embed-request" — a
        // different path server-side, and one Node's URL parser rejects outright.
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync(stored);
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Equal(expected, await service.GetBaseUrlAsync());
    }

    [Fact]
    public async Task RequestEmbedPermissionAsync_ReturnsNull_WhenTheUrlIsMalformed()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync("not a url at all");
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Null(await service.RequestEmbedPermissionAsync(SelfOrigin));
    }

    [Fact]
    public async Task CheckEmbedAsync_AllowsWhenNoFramingHeadersAreSent()
    {
        var service = CreateService(_ => WithHeaders());

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.True(result.Reachable);
        Assert.True(result.CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_AllowsWhenFrameAncestorsListsUs()
    {
        var service = CreateService(_ => WithHeaders(
            ("X-Frame-Options", "SAMEORIGIN"),
            ("Content-Security-Policy", "frame-ancestors 'self' http://localhost:5159")));

        // CSP frame-ancestors supersedes X-Frame-Options wherever it is understood, so the
        // still-present SAMEORIGIN must not veto an origin the policy allows.
        Assert.True((await service.CheckEmbedAsync(SelfOrigin)).CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_BlocksWhenFrameAncestorsOmitsUs()
    {
        var service = CreateService(_ => WithHeaders(
            ("Content-Security-Policy", "frame-ancestors 'self' http://localhost:9999")));

        var result = await service.CheckEmbedAsync(SelfOrigin);

        Assert.False(result.CanEmbed);
        Assert.Contains("http://localhost:9999", result.Reason);
    }

    [Fact]
    public async Task CheckEmbedAsync_FindsFrameAncestorsAmongOtherDirectives()
    {
        var service = CreateService(_ => WithHeaders(
            ("Content-Security-Policy", "default-src 'self'; frame-ancestors 'self' http://localhost:5159; img-src *")));

        Assert.True((await service.CheckEmbedAsync(SelfOrigin)).CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_IgnoresATrailingSlashOnAListedOrigin()
    {
        var service = CreateService(_ => WithHeaders(
            ("Content-Security-Policy", "frame-ancestors http://localhost:5159/")));

        Assert.True((await service.CheckEmbedAsync(SelfOrigin)).CanEmbed);
    }

    [Fact]
    public async Task CheckEmbedAsync_FallsBackToXFrameOptions_WhenCspSaysNothingAboutFraming()
    {
        var service = CreateService(_ => WithHeaders(
            ("X-Frame-Options", "SAMEORIGIN"),
            ("Content-Security-Policy", "default-src 'self'")));

        // A CSP without a frame-ancestors directive says nothing about framing,
        // so X-Frame-Options still decides.
        Assert.False((await service.CheckEmbedAsync(SelfOrigin)).CanEmbed);
    }

    [Fact]
    public async Task RequestEmbedPermissionAsync_PostsTheOriginAndReturnsTheId()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var service = CreateService(req =>
        {
            captured = req;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"abc-123","status":"pending"}"""),
            };
        });

        var id = await service.RequestEmbedPermissionAsync(SelfOrigin);

        Assert.Equal("abc-123", id);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("/embed-request", captured.RequestUri!.ToString());
        Assert.Contains(SelfOrigin, body);
    }

    [Fact]
    public async Task RequestEmbedPermissionAsync_ReturnsNull_WhenTheServerRefuses()
    {
        // An older ws-scrcpy-web has no such endpoint, and a non-loopback caller is rejected.
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await service.RequestEmbedPermissionAsync(SelfOrigin));
    }

    [Fact]
    public async Task GetEmbedRequestStatusAsync_ReturnsTheReportedStatus()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"abc-123","status":"approved"}"""),
        });

        Assert.Equal("approved", await service.GetEmbedRequestStatusAsync("abc-123"));
    }

    [Fact]
    public async Task GetEmbedRequestStatusAsync_ReturnsNull_WhenTheServerIsUnreachable()
    {
        var service = CreateService(_ => throw new HttpRequestException("down"));

        // Null is "we do not know", which the page must not treat as a decision —
        // a restarting server would otherwise look like a denial.
        Assert.Null(await service.GetEmbedRequestStatusAsync("abc-123"));
    }
}

internal sealed class EmbedScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(respond(request));
    }
}
