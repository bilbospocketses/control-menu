// tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs
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
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetDeployModeAsync_DefaultsToManaged_WhenSettingAbsent()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>()))
            .ReturnsAsync((string?)null);
        var svc = CreateService();
        Assert.Equal(WsScrcpyDeployMode.Managed, await svc.GetDeployModeAsync());
    }

    [Fact]
    public async Task GetDeployModeAsync_ReturnsExternal_WhenSettingIsExternal()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>()))
            .ReturnsAsync("external");
        var svc = CreateService();
        Assert.Equal(WsScrcpyDeployMode.External, await svc.GetDeployModeAsync());
    }

    [Theory]
    [InlineData("External")]
    [InlineData("EXTERNAL")]
    [InlineData("eXtErNaL")]
    public async Task GetDeployModeAsync_CaseInsensitive(string value)
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>()))
            .ReturnsAsync(value);
        var svc = CreateService();
        Assert.Equal(WsScrcpyDeployMode.External, await svc.GetDeployModeAsync());
    }

    [Fact]
    public async Task StartAsync_External_SetsBaseUrlFromSetting_AndReadyFlag()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("http://ws-scrcpy:8000");
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal("http://ws-scrcpy:8000", svc.BaseUrl);
        Assert.True(svc.IsRunning);
    }

    [Fact]
    public async Task StartAsync_External_DefaultsBaseUrl_WhenUrlSettingMissing()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync((string?)null);
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        Assert.Equal("http://localhost:8000", svc.BaseUrl);
        Assert.True(svc.IsRunning);
    }

    [Fact]
    public async Task StartAsync_External_DoesNotSpawnProcess()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("http://fake:8000");
        var svc = CreateService();
        await svc.StartAsync(CancellationToken.None);
        // External mode: no child process spawned; IsRunning flips true after the config read.
        Assert.True(svc.IsRunning);
    }
}
