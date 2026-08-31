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

    private const string DefaultUrl = "http://localhost:8000";

    /// <summary>
    /// Last resolved URL. This is a cache for synchronous readers -- call
    /// <see cref="GetBaseUrlAsync"/> when the value must be current.
    /// </summary>
    public string BaseUrl { get; private set; } = DefaultUrl;

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
        var url = await GetBaseUrlAsync(cancellationToken);
        _serviceReady = true;
        _logger.LogInformation("ws-scrcpy-web external URL: {Url}", url);
    }

    /// <summary>
    /// Re-reads <c>wsscrcpy-url</c> from configuration and refreshes <see cref="BaseUrl"/>.
    /// The URL used to be resolved once in <see cref="StartAsync"/> and cached for the life of the
    /// process, so changing it in Settings had no effect until the app was restarted -- the Power
    /// Tools page kept pointing at the default port 8000 whatever was configured.
    /// </summary>
    public async Task<string> GetBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var url = (await config.GetSettingAsync("wsscrcpy-url")) ?? DefaultUrl;
        BaseUrl = url;
        return url;
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

        // Probe whatever is configured right now, not whatever was configured at startup.
        var baseUrl = await GetBaseUrlAsync(cancellationToken);

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, baseUrl + "/");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
