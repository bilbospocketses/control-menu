using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyCheckHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_CallsCheckAllOnStart()
    {
        var mockManager = new Mock<IDependencyManagerService>();
        mockManager.Setup(m => m.CheckAllAsync())
            .ReturnsAsync(Array.Empty<DependencyCheckResult>());

        var mockConfig = new Mock<IConfigurationService>();
        mockConfig.Setup(c => c.GetSettingAsync("dep-check-interval", null))
            .ReturnsAsync("86400");

        var services = new ServiceCollection();
        services.AddScoped(_ => mockManager.Object);
        services.AddScoped(_ => mockConfig.Object);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new DependencyCheckHostedService(
            scopeFactory, NullLogger<DependencyCheckHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _ = service.StartAsync(cts.Token);

        // Wait for the initial check (10s startup delay + execution)
        await Task.Delay(12000, cts.Token);
        await service.StopAsync(CancellationToken.None);

        mockManager.Verify(m => m.CheckAllAsync(), Times.AtLeastOnce);
    }
}
