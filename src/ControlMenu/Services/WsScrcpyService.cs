// src/ControlMenu/Services/WsScrcpyService.cs
using ControlMenu.Data;

namespace ControlMenu.Services;

/// <summary>
/// External-mode-only ws-scrcpy-web client. Reads the user-configured URL from
/// the IConfigurationService at startup and exposes it via BaseUrl. Does not
/// spawn or supervise any process — ws-scrcpy-web is run externally by the user.
/// </summary>
public class WsScrcpyService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WsScrcpyService> _logger;
    private bool _serviceReady;

    public string BaseUrl { get; private set; } = "http://localhost:8000";

    /// <summary>True once the service has resolved a URL at startup. Does NOT
    /// guarantee ws-scrcpy-web is currently reachable — callers HTTP to BaseUrl
    /// and handle connection failures naturally.</summary>
    public bool IsRunning => _serviceReady;

    public WsScrcpyService(IServiceScopeFactory scopeFactory, ILogger<WsScrcpyService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var url = (await config.GetSettingAsync("wsscrcpy-url")) ?? "http://localhost:8000";
        BaseUrl = url;
        _serviceReady = true;
        _logger.LogInformation("ws-scrcpy-web external URL: {Url}", url);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _serviceReady = false;
        return Task.CompletedTask;
    }
}
