using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

public partial class CameraProxyMiddleware(RequestDelegate next, ILogger<CameraProxyMiddleware> logger)
{
    private static readonly ConcurrentDictionary<int, CookieContainer> _cookieJars = new();
    private static readonly ConcurrentDictionary<int, HttpClient> _httpClients = new();

    private static readonly string[] _stripHeaders =
    [
        "X-Frame-Options",
        "Content-Security-Policy",
        "Content-Security-Policy-Report-Only"
    ];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        var match = ProxyPathRegex().Match(context.Request.Path.Value ?? "");
        if (!match.Success)
        {
            await next(context);
            return;
        }

        var cameraIndex = int.Parse(match.Groups[1].Value);
        var proxyPath = match.Groups[2].Success ? match.Groups[2].Value : "";

        // Resolve scoped services
        var cameraService = context.RequestServices.GetRequiredService<ICameraService>();

        var camera = await cameraService.GetCameraAsync(cameraIndex);
        if (camera is null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync($"Camera {cameraIndex} not configured");
            return;
        }

        var credentials = await cameraService.GetCredentialsAsync(cameraIndex);
        if (credentials is null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync($"Camera {cameraIndex} has no credentials configured");
            return;
        }

        var baseUri = new Uri($"http://{camera.IpAddress}:{camera.Port}");
        var client = GetOrCreateClient(cameraIndex, baseUri, credentials.Value.Username, credentials.Value.Password);

        // Try session login if no cookies cached (some firmware needs it in addition to digest auth)
        if (!_cookieJars.TryGetValue(cameraIndex, out var jar) || jar.Count == 0)
        {
            await TrySessionLoginAsync(client, credentials.Value.Username, credentials.Value.Password, cameraIndex);
        }

        // Build target URI
        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");

        // Forward the request
        var response = await ForwardRequestAsync(client, context, targetUri);

        // On 401, clear session, re-login, retry once
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Camera {Index} returned 401, re-authenticating", cameraIndex);
            ClearSession(cameraIndex);
            client = GetOrCreateClient(cameraIndex, baseUri, credentials.Value.Username, credentials.Value.Password);
            await TrySessionLoginAsync(client, credentials.Value.Username, credentials.Value.Password, cameraIndex);

            response = await ForwardRequestAsync(client, context, targetUri);
        }

        await WriteProxyResponseAsync(context, response, cameraIndex);
    }

    private static HttpClient GetOrCreateClient(int cameraIndex, Uri baseUri, string? username = null, string? password = null)
    {
        return _httpClients.GetOrAdd(cameraIndex, _ =>
        {
            var jar = _cookieJars.GetOrAdd(cameraIndex, _ => new CookieContainer());
            var handler = new HttpClientHandler
            {
                CookieContainer = jar,
                AllowAutoRedirect = false,
                UseCookies = true
            };

            // Hikvision/LTS cameras use HTTP Digest Authentication for ISAPI
            if (username is not null && password is not null)
            {
                handler.Credentials = new NetworkCredential(username, password);
                handler.PreAuthenticate = false; // Let the 401 challenge flow naturally
            }

            return new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(15)
            };
        });
    }

    /// <summary>
    /// Attempts session-based login (some Hikvision firmware uses this on top of digest auth).
    /// Best-effort — if it fails, digest auth credentials on the HttpClient may still work.
    /// </summary>
    private async Task TrySessionLoginAsync(HttpClient client, string username, string password, int cameraIndex)
    {
        var loginXml = $"<SessionLogin><userName>{username}</userName><password>{password}</password></SessionLogin>";

        try
        {
            var content = new StringContent(loginXml, System.Text.Encoding.UTF8, "application/xml");
            var response = await client.PostAsync("/ISAPI/Security/sessionLogin", content);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Camera {Index} session login succeeded", cameraIndex);
                return;
            }
            logger.LogDebug("Camera {Index} sessionLogin returned {Status}, relying on digest auth", cameraIndex, response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Camera {Index} sessionLogin not available, relying on digest auth", cameraIndex);
        }
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(HttpClient client, HttpContext context, Uri targetUri)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        // Copy request headers (skip Host)
        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Copy request body for POST/PUT
        if (context.Request.ContentLength > 0 || context.Request.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    }

    // Content types that may contain absolute URL references needing rewriting
    private static readonly string[] _rewritableContentTypes = ["text/html", "text/javascript", "application/javascript", "text/css"];

    // Hikvision/LTS well-known root paths used in their web UI
    [GeneratedRegex(@"(?<=[""'/=])/(doc|ISAPI|SDK|PSIA|images|css|js|custom|bvw)(?=[/""])", RegexOptions.Compiled)]
    private static partial Regex AbsolutePathRegex();

    private static async Task WriteProxyResponseAsync(HttpContext context, HttpResponseMessage response, int cameraIndex)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        // Copy response headers, stripping iframe-blocking and transfer-encoding
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (_stripHeaders.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip Content-Length — we may rewrite the body, changing its length
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            // Rewrite Location headers for redirects
            if (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase))
            {
                var rewritten = header.Value.Select(v =>
                {
                    if (Uri.TryCreate(v, UriKind.Absolute, out var uri))
                        return $"/cameras/{cameraIndex}/proxy{uri.PathAndQuery}";
                    return $"/cameras/{cameraIndex}/proxy/{v.TrimStart('/')}";
                });
                context.Response.Headers.Append(header.Key, rewritten.ToArray());
                continue;
            }

            context.Response.Headers.Append(header.Key, header.Value.ToArray());
        }

        // For text content (HTML, JS, CSS), rewrite absolute paths to go through proxy
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (_rewritableContentTypes.Any(ct => contentType.Contains(ct, StringComparison.OrdinalIgnoreCase)))
        {
            var body = await response.Content.ReadAsStringAsync();
            var prefix = $"/cameras/{cameraIndex}/proxy";
            body = AbsolutePathRegex().Replace(body, $"{prefix}/$1");
            await context.Response.WriteAsync(body);
        }
        else
        {
            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    /// <summary>
    /// Clears cached session cookies for a camera. Called by settings UI when credentials change.
    /// </summary>
    public static void ClearSession(int cameraIndex)
    {
        _cookieJars.TryRemove(cameraIndex, out _);
        if (_httpClients.TryRemove(cameraIndex, out var client))
            client.Dispose();
    }
}
