// src/ControlMenu/Services/WsScrcpyService.cs
using System.Text;
using System.Text.Json;
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

    /// <summary>
    /// Asks ws-scrcpy-web for permission to embed this app, returning the request id to poll.
    /// The request only raises a prompt there — a human has to approve it — so this grants
    /// nothing on its own. Returns null when the server refused or could not be reached.
    /// </summary>
    public async Task<string?> RequestEmbedPermissionAsync(string embedderOrigin, CancellationToken cancellationToken = default)
    {
        var baseUrl = await GetBaseUrlAsync(cancellationToken);

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        try
        {
            var payload = JsonSerializer.Serialize(new { origin = embedderOrigin, appName = "Control Menu" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{baseUrl}/embed-request", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ws-scrcpy-web refused the embed request: {Status}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not ask ws-scrcpy-web for embed permission");
            return null;
        }
    }

    /// <summary>
    /// Outcome of an embed request: <c>pending</c>, <c>approved</c>, <c>denied</c>,
    /// <c>expired</c>, or <c>unknown</c>. Returns null when the server could not be reached,
    /// which is distinct from a decision and must not be treated as one.
    /// </summary>
    public async Task<string?> GetEmbedRequestStatusAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var baseUrl = await GetBaseUrlAsync(cancellationToken);

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            var url = $"{baseUrl}/embed-request/{Uri.EscapeDataString(requestId)}";
            var body = await http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether ws-scrcpy-web will let <paramref name="embedderOrigin"/> put it in an iframe,
    /// decided from the same headers the browser evaluates rather than by watching a frame fail.
    /// </summary>
    /// <param name="Reachable">False when the server did not answer at all.</param>
    /// <param name="CanEmbed">True when the browser will allow the frame.</param>
    /// <param name="Reason">Why not, when it will not.</param>
    public record EmbedCheck(bool Reachable, bool CanEmbed, string? Reason);

    /// <summary>
    /// Reads the framing headers from ws-scrcpy-web and decides whether an iframe on
    /// <paramref name="embedderOrigin"/> will render. A blocked frame gives the browser error
    /// "refused to connect", which is indistinguishable from the server being down, so this is
    /// checked before rendering rather than diagnosed afterwards.
    /// </summary>
    public async Task<EmbedCheck> CheckEmbedAsync(string embedderOrigin, CancellationToken cancellationToken = default)
    {
        var baseUrl = await GetBaseUrlAsync(cancellationToken);

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/");
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new EmbedCheck(false, false, $"ws-scrcpy-web did not respond at {baseUrl}.");
        }

        using (response)
        {
            var csp = FirstHeader(response, "Content-Security-Policy");
            var xfo = FirstHeader(response, "X-Frame-Options");

            // CSP frame-ancestors supersedes X-Frame-Options wherever a browser
            // understands it, so it is checked first and its verdict is final.
            var frameAncestors = ExtractFrameAncestors(csp);
            if (frameAncestors is not null)
            {
                var allowed = frameAncestors.Any(source =>
                    string.Equals(source, embedderOrigin, StringComparison.OrdinalIgnoreCase));
                return allowed
                    ? new EmbedCheck(true, true, null)
                    : new EmbedCheck(true, false,
                        $"ws-scrcpy-web allows framing from {string.Join(", ", frameAncestors)}, which does not include {embedderOrigin}.");
            }

            if (xfo is not null &&
                (xfo.Contains("DENY", StringComparison.OrdinalIgnoreCase)
                 || xfo.Contains("SAMEORIGIN", StringComparison.OrdinalIgnoreCase)))
            {
                return new EmbedCheck(true, false,
                    $"ws-scrcpy-web sends X-Frame-Options: {xfo.Trim()}, so it refuses to be framed by {embedderOrigin}.");
            }

            return new EmbedCheck(true, true, null);
        }
    }

    private static string? FirstHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.FirstOrDefault()
        : null;

    /// <summary>
    /// The sources listed by a CSP `frame-ancestors` directive, or null when the policy has no
    /// such directive (in which case CSP says nothing about framing and X-Frame-Options decides).
    /// </summary>
    private static IReadOnlyList<string>? ExtractFrameAncestors(string? csp)
    {
        if (string.IsNullOrWhiteSpace(csp)) return null;

        foreach (var directive in csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = directive.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;
            if (!string.Equals(parts[0], "frame-ancestors", StringComparison.OrdinalIgnoreCase)) continue;

            // Trim trailing slashes so "http://host:5159/" matches "http://host:5159".
            return parts.Skip(1).Select(s => s.TrimEnd('/')).ToList();
        }

        return null;
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
