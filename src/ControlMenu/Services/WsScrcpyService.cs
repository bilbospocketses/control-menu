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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WsScrcpyService> _logger;
    private bool _serviceReady;

    public string BaseUrl { get; private set; } = "http://localhost:8000";

    /// <summary>True once the service has resolved a URL at startup. Does NOT
    /// guarantee ws-scrcpy-web is currently reachable — call ProbeAsync for that.</summary>
    public bool IsRunning => _serviceReady;

    public WsScrcpyService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, ILogger<WsScrcpyService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
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

    /// <summary>HTTP probe against BaseUrl/. Returns true if a response (any status)
    /// comes back inside the timeout, false on connection refused / DNS failure /
    /// timeout. Use this to gate UI that embeds the ws-scrcpy-web iframe.</summary>
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!_serviceReady) return false;

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, BaseUrl + "/");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
