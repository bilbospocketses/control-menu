using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

public partial class CameraProxyMiddleware(RequestDelegate next, ILogger<CameraProxyMiddleware> logger)
{
    // Track which cameras have been pre-authenticated this app lifetime
    private static readonly ConcurrentDictionary<int, bool> _authenticated = new();

    private static readonly string[] _stripHeaders =
    [
        "X-Frame-Options",
        "Content-Security-Policy",
        "Content-Security-Policy-Report-Only"
    ];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

    // Content types that may contain absolute URL references needing rewriting
    private static readonly string[] _rewritableContentTypes = ["text/html", "text/javascript", "application/javascript", "text/css"];

    // Hikvision/LTS well-known root paths used in their web UI
    [GeneratedRegex(@"(?<=[""'/=])/(doc|ISAPI|SDK|PSIA|images|css|js|custom|bvw)(?=[/""])", RegexOptions.Compiled)]
    private static partial Regex AbsolutePathRegex();

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
        var proxyPrefix = $"/cameras/{cameraIndex}/proxy";

        // Create handler with digest auth but NO cookie container — cookies flow as headers
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(credentials.Value.Username, credentials.Value.Password),
            UseCookies = false, // We manage cookies transparently via headers
            AllowAutoRedirect = false,
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        // Pre-authenticate: on first access, call ISAPI to get a session cookie for the browser
        if (!_authenticated.ContainsKey(cameraIndex) || !BrowserHasSessionCookie(context.Request, cameraIndex))
        {
            var sessionCookie = await PreAuthenticateAsync(client, credentials.Value.Username, credentials.Value.Password, cameraIndex);
            if (sessionCookie is not null)
            {
                // Set the camera's session cookie on the browser, scoped to the proxy path
                context.Response.Cookies.Append($"cam{cameraIndex}_session", sessionCookie, new CookieOptions
                {
                    Path = proxyPrefix,
                    HttpOnly = false, // Camera JS needs to read it
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax
                });
                _authenticated[cameraIndex] = true;
            }
        }

        // Build target URI
        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");

        // Forward the request, passing browser cookies to camera
        var response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);

        // On 401, try re-auth and retry
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Camera {Index} returned 401, re-authenticating", cameraIndex);
            _authenticated.TryRemove(cameraIndex, out _);

            var sessionCookie = await PreAuthenticateAsync(client, credentials.Value.Username, credentials.Value.Password, cameraIndex);
            if (sessionCookie is not null)
            {
                context.Response.Cookies.Append($"cam{cameraIndex}_session", sessionCookie, new CookieOptions
                {
                    Path = proxyPrefix,
                    HttpOnly = false,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax
                });
                _authenticated[cameraIndex] = true;
            }

            response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);
        }

        await WriteProxyResponseAsync(context, response, cameraIndex);
    }

    private static bool BrowserHasSessionCookie(HttpRequest request, int cameraIndex)
    {
        return request.Cookies.ContainsKey($"cam{cameraIndex}_session");
    }

    /// <summary>
    /// Pre-authenticate with camera via digest auth to get a session cookie.
    /// Returns the session cookie value, or null if it couldn't be obtained.
    /// </summary>
    private async Task<string?> PreAuthenticateAsync(HttpClient client, string username, string password, int cameraIndex)
    {
        // Try ISAPI sessionLogin with digest auth
        try
        {
            var loginXml = $"<SessionLogin><userName>{username}</userName><password>{password}</password></SessionLogin>";
            var content = new StringContent(loginXml, System.Text.Encoding.UTF8, "application/xml");
            var response = await client.PostAsync("/ISAPI/Security/sessionLogin", content);

            if (response.IsSuccessStatusCode)
            {
                // Extract session cookie from response
                var cookie = ExtractSessionCookie(response);
                if (cookie is not null)
                {
                    logger.LogInformation("Camera {Index} pre-authenticated, got session cookie", cameraIndex);
                    return cookie;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Camera {Index} sessionLogin failed", cameraIndex);
        }

        // Fallback: just GET the root page with digest auth — camera may set a cookie
        try
        {
            var response = await client.GetAsync("/");
            var cookie = ExtractSessionCookie(response);
            if (cookie is not null)
            {
                logger.LogInformation("Camera {Index} got session cookie from root page", cameraIndex);
                return cookie;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Camera {Index} root page auth failed", cameraIndex);
        }

        logger.LogWarning("Camera {Index} pre-authentication did not yield a session cookie", cameraIndex);
        return null;
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        // Hikvision typically uses WebSession or ISAPI_SESSION cookies
        foreach (var cookie in cookies)
        {
            if (cookie.Contains("WebSession", StringComparison.OrdinalIgnoreCase) ||
                cookie.Contains("session", StringComparison.OrdinalIgnoreCase))
            {
                // Extract just the cookie value (before first ;)
                var parts = cookie.Split(';')[0].Split('=', 2);
                if (parts.Length == 2)
                    return $"{parts[0].Trim()}={parts[1].Trim()}";
            }
        }

        // If no named session cookie, take the first Set-Cookie
        var first = cookies.FirstOrDefault();
        if (first is not null)
        {
            var parts = first.Split(';')[0];
            return parts;
        }

        return null;
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(HttpClient client, HttpContext context, Uri targetUri, int cameraIndex)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        // Copy request headers (skip Host, Cookie is handled separately)
        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Forward the camera session cookie from browser to camera
        // Browser stores it as cam{N}_session, camera expects the raw cookie
        if (context.Request.Cookies.TryGetValue($"cam{cameraIndex}_session", out var sessionCookie) && !string.IsNullOrEmpty(sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
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

    private static async Task WriteProxyResponseAsync(HttpContext context, HttpResponseMessage response, int cameraIndex)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        var proxyPrefix = $"/cameras/{cameraIndex}/proxy";

        // Copy response headers
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (_stripHeaders.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            // Don't forward Set-Cookie directly — we manage cookies via our own cookie names
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                continue;

            // Rewrite Location headers for redirects
            if (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase))
            {
                var rewritten = header.Value.Select(v =>
                {
                    if (Uri.TryCreate(v, UriKind.Absolute, out var uri))
                        return $"{proxyPrefix}{uri.PathAndQuery}";
                    return $"{proxyPrefix}/{v.TrimStart('/')}";
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
            body = AbsolutePathRegex().Replace(body, $"{proxyPrefix}/$1");
            await context.Response.WriteAsync(body);
        }
        else
        {
            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    /// <summary>
    /// Clears cached auth state for a camera. Called by settings UI when credentials change.
    /// </summary>
    public static void ClearSession(int cameraIndex)
    {
        _authenticated.TryRemove(cameraIndex, out _);
    }
}
