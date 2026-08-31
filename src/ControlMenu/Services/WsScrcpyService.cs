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
        var stored = await config.GetSettingAsync("wsscrcpy-url");

        // Repair rather than propagate. Every consumer concatenates onto this ("{base}/embed-request",
        // base + "/"), so a stored trailing slash produces "//embed-request" — which is a different
        // path to the server and which Node's URL parser rejects outright. `?? DefaultUrl` also did
        // not cover an empty or whitespace-only stored value.
        var url = string.IsNullOrWhiteSpace(stored) ? DefaultUrl : stored.Trim().TrimEnd('/');
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
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        try
        {
            var baseUrl = await GetBaseUrlAsync(cancellationToken);
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
        catch (Exception ex) when (IsTransportFailure(ex) || ex is JsonException)
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
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            var baseUrl = await GetBaseUrlAsync(cancellationToken);
            var url = $"{baseUrl}/embed-request/{Uri.EscapeDataString(requestId)}";
            var body = await http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (Exception ex) when (IsTransportFailure(ex) || ex is JsonException)
        {
            // Null is "we could not find out", never a decision — see the caller.
            return null;
        }
    }

    /// <summary>
    /// Everything HttpClient throws for a request that cannot be made or completed, including the
    /// URI-shaped failures a hand-typed setting produces: an unparseable value
    /// (<see cref="UriFormatException"/>), an unsupported scheme such as <c>localhost:8000</c> read
    /// as scheme <c>localhost</c> (<see cref="NotSupportedException"/>), and a relative URI with no
    /// BaseAddress (<see cref="InvalidOperationException"/>). Filtering on HttpRequestException
    /// alone let those escape into the Blazor circuit.
    /// </summary>
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or UriFormatException
            or NotSupportedException
            or InvalidOperationException;

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
        var baseUrl = BaseUrl;
        HttpResponseMessage response;
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            // Inside the try: resolving the URL touches the database, and building the request
            // throws for a malformed one — neither was covered when this sat above the try.
            baseUrl = await GetBaseUrlAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/");
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return new EmbedCheck(false, false, $"ws-scrcpy-web did not respond at {baseUrl} ({ex.Message}).");
        }

        using (response)
        {
            // An error status says nothing about framing, and reading headers off it would default
            // to "embeddable" — so a 403 from the host allowlist, a 404, or a 500 all rendered as a
            // working iframe full of an error body. Report the status instead.
            if (!response.IsSuccessStatusCode)
            {
                return new EmbedCheck(true, false,
                    $"ws-scrcpy-web answered {(int)response.StatusCode} ({response.ReasonPhrase}) at {baseUrl}/.");
            }

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

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            // Probe whatever is configured right now, not whatever was configured at startup —
            // and inside the try, since both resolving and parsing it can throw.
            var baseUrl = await GetBaseUrlAsync(cancellationToken);
            using var req = new HttpRequestMessage(HttpMethod.Head, baseUrl + "/");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return false;
        }
    }
}
