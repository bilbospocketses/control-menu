using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class SubnetDetectionClientTests
{
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly WsScrcpyService _wsScrcpy;

    public SubnetDetectionClientTests()
    {
        _wsScrcpy = new WsScrcpyService(
            new Mock<IServiceScopeFactory>().Object,
            _mockConfig.Object,
            NullLogger<WsScrcpyService>.Instance);
    }

    private async Task ConfigureExternalAsync(string baseUrl)
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync(baseUrl);
        await _wsScrcpy.StartAsync(CancellationToken.None);
    }

    private static Mock<IHttpClientFactory> FactoryFor(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        return factory;
    }

    [Fact]
    public async Task Detect_ReturnsSubnet_OnSuccess()
    {
        await ConfigureExternalAsync("http://localhost:8000");
        var handler = new MockHttpHandler("{\"cidr\":\"192.168.86.0/24\",\"hostCount\":254,\"source\":\"gateway\"}");
        var client = new SubnetDetectionClient(FactoryFor(handler).Object, _wsScrcpy);
        var detected = await client.DetectAsync();
        Assert.NotNull(detected);
        Assert.Equal("192.168.86.0/24", detected!.Cidr);
        Assert.Equal(254, detected.HostCount);
        Assert.Equal("gateway", detected.Source);
    }

    [Fact]
    public async Task Detect_ReturnsNull_OnNon2xx()
    {
        await ConfigureExternalAsync("http://localhost:8000");
        var handler = new MockHttpHandler("", System.Net.HttpStatusCode.ServiceUnavailable);
        var client = new SubnetDetectionClient(FactoryFor(handler).Object, _wsScrcpy);
        Assert.Null(await client.DetectAsync());
    }

    [Fact]
    public async Task Detect_ReturnsNull_OnException()
    {
        await ConfigureExternalAsync("http://localhost:8000");
        var handler = new ThrowingHttpHandler();
        var client = new SubnetDetectionClient(FactoryFor(handler).Object, _wsScrcpy);
        Assert.Null(await client.DetectAsync());
    }

    [Fact]
    public async Task Detect_ReturnsNull_OnServerReturnsJsonNull()
    {
        // ws-scrcpy-web's SubnetDetector returns null when no subnet can be detected.
        await ConfigureExternalAsync("http://localhost:8000");
        var handler = new MockHttpHandler("null");
        var client = new SubnetDetectionClient(FactoryFor(handler).Object, _wsScrcpy);
        Assert.Null(await client.DetectAsync());
    }

    [Fact]
    public async Task Detect_TrimsTrailingSlash()
    {
        // BaseUrl with trailing slash shouldn't produce //api/... in the request URL.
        // Verify by capturing the request URI via a probing handler.
        await ConfigureExternalAsync("http://localhost:8000/");
        string? capturedUrl = null;
        var handler = new CapturingHttpHandler(req => { capturedUrl = req.RequestUri?.ToString(); });
        var client = new SubnetDetectionClient(FactoryFor(handler).Object, _wsScrcpy);
        await client.DetectAsync();
        Assert.Equal("http://localhost:8000/api/devices/scan/subnet", capturedUrl);
    }
}

// Small inline helper for the URL-capture test. Local to this file.
internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly Action<HttpRequestMessage> _capture;
    public CapturingHttpHandler(Action<HttpRequestMessage> capture) { _capture = capture; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _capture(request);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
