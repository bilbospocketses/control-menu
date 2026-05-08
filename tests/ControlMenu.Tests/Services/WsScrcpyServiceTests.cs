// tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs
// NOTE: Managed-mode tests removed in Task 1 (WsScrcpyDeployMode / GetDeployModeAsync no longer exist).
// Remaining tests cover external-only behaviour. Full test refresh in Task 5.
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace ControlMenu.Tests.Services;

public class WsScrcpyServiceTests
{
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<ILogger<WsScrcpyService>> _mockLogger = new();

    private WsScrcpyService CreateService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _mockConfig.Object);
        var provider = services.BuildServiceProvider();
        return new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Mock<IHttpClientFactory>().Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task StartAsync_SetsBaseUrlFromSetting_AndReadyFlag()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync("http://ws-scrcpy:8000");
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal("http://ws-scrcpy:8000", svc.BaseUrl);
        Assert.True(svc.IsRunning);
    }

    [Fact]
    public async Task StartAsync_DefaultsBaseUrl_WhenUrlSettingMissing()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync((string?)null);
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal("http://localhost:8000", svc.BaseUrl);
        Assert.True(svc.IsRunning);
    }

    [Fact]
    public async Task StopAsync_ClearsReadyFlag()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
            .ReturnsAsync("http://ws-scrcpy:8000");
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        Assert.True(svc.IsRunning);
        await svc.StopAsync(CancellationToken.None);
        Assert.False(svc.IsRunning);
    }
}
