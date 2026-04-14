using System.Net;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

/// <summary>
/// Reverse proxy middleware for Hikvision/LTS cameras. Forwards requests from
/// /cameras/{index}/proxy/** to the camera's IP with HTTP Digest Authentication.
/// Strips iframe-blocking headers and rewrites absolute paths in HTML/JS/CSS
/// so the camera's web UI works inside an iframe.
/// </summary>
public partial class CameraProxyMiddleware(RequestDelegate next, ILogger<CameraProxyMiddleware> logger)
{
    private static readonly string[] _stripHeaders =
    [
        "X-Frame-Options",
        "Content-Security-Policy",
        "Content-Security-Policy-Report-Only"
    ];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

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

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(credentials.Value.Username, credentials.Value.Password),
            UseCookies = false,
            AllowAutoRedirect = false,
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");
        var response = await ForwardRequestAsync(client, context, targetUri);

        await WriteProxyResponseAsync(context, response, cameraIndex, proxyPrefix);
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(
        HttpClient client, HttpContext context, Uri targetUri)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (context.Request.ContentLength > 0 || context.Request.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    }

    private static async Task WriteProxyResponseAsync(
        HttpContext context, HttpResponseMessage response, int cameraIndex, string proxyPrefix)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (_stripHeaders.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            // Forward Set-Cookie with rewritten path
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var val in header.Value)
                {
                    var rewritten = Regex.Replace(val, @"[Pp]ath=/[^;]*", $"Path={proxyPrefix}/");
                    if (!rewritten.Contains("Path=", StringComparison.OrdinalIgnoreCase))
                        rewritten += $"; Path={proxyPrefix}/";
                    context.Response.Headers.Append("Set-Cookie", rewritten);
                }
                continue;
            }

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
    /// Clears cached state for a camera. Called by settings UI when credentials change.
    /// </summary>
    public static void ClearSession(int cameraIndex)
    {
        // Stateless — each request creates a fresh HttpClient with digest auth.
        // Kept for API compatibility with settings UI.
    }
}
