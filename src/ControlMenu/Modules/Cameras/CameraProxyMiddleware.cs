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
        var client = GetOrCreateClient(cameraIndex, baseUri);

        // Ensure session exists
        if (!_cookieJars.TryGetValue(cameraIndex, out var jar) || jar.Count == 0)
        {
            var loggedIn = await TryLoginAsync(client, baseUri, credentials.Value.Username, credentials.Value.Password, cameraIndex);
            if (!loggedIn)
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsync($"Failed to authenticate with camera {cameraIndex}");
                return;
            }
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
            client = GetOrCreateClient(cameraIndex, baseUri);

            var loggedIn = await TryLoginAsync(client, baseUri, credentials.Value.Username, credentials.Value.Password, cameraIndex);
            if (!loggedIn)
            {
                context.Response.StatusCode = 502;
                await context.Response.WriteAsync($"Failed to re-authenticate with camera {cameraIndex}");
                return;
            }

            response = await ForwardRequestAsync(client, context, targetUri);
        }

        await WriteProxyResponseAsync(context, response, cameraIndex);
    }

    private static HttpClient GetOrCreateClient(int cameraIndex, Uri baseUri)
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
            return new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(15)
            };
        });
    }

    private async Task<bool> TryLoginAsync(HttpClient client, Uri baseUri, string username, string password, int cameraIndex)
    {
        var loginXml = $"<SessionLogin><userName>{username}</userName><password>{password}</password></SessionLogin>";

        // Try ISAPI sessionLogin first
        try
        {
            var content = new StringContent(loginXml, System.Text.Encoding.UTF8, "application/xml");
            var response = await client.PostAsync("/ISAPI/Security/sessionLogin", content);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Camera {Index} authenticated via sessionLogin", cameraIndex);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera {Index} sessionLogin failed, trying userCheck", cameraIndex);
        }

        // Fallback to userCheck
        try
        {
            var content = new StringContent(loginXml, System.Text.Encoding.UTF8, "application/xml");
            var response = await client.PostAsync("/ISAPI/Security/userCheck", content);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Camera {Index} authenticated via userCheck", cameraIndex);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera {Index} userCheck also failed", cameraIndex);
        }

        return false;
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

        await response.Content.CopyToAsync(context.Response.Body);
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
