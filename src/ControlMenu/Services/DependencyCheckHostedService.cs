using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Services;

public class DependencyCheckHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DependencyCheckHostedService> _logger;

    public DependencyCheckHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DependencyCheckHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to finish initializing
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var manager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
                var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                _logger.LogInformation("Running scheduled dependency version check");
                var results = await manager.CheckAllAsync();

                var updates = results.Count(r => r.Status == Data.Enums.DependencyStatus.UpdateAvailable);
                if (updates > 0)
                    _logger.LogInformation("{Count} dependency update(s) available", updates);

                // Read interval from settings (default: 24 hours)
                var intervalStr = await config.GetSettingAsync("dep-check-interval");
                var intervalSeconds = int.TryParse(intervalStr, out var parsed) ? parsed : 86400;

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dependency check cycle failed");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
