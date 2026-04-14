using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

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

        // HttpClient with digest auth, cookies flow transparently via headers
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(credentials.Value.Username, credentials.Value.Password),
            UseCookies = false,
            AllowAutoRedirect = false,
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");

        var response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);

        await WriteProxyResponseAsync(context, response, cameraIndex, proxyPrefix,
            credentials.Value.Username, credentials.Value.Password);
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(
        HttpClient client, HttpContext context, Uri targetUri, int cameraIndex)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        // Forward all headers except Host (rewritten by HttpClient)
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
        HttpContext context, HttpResponseMessage response, int cameraIndex,
        string proxyPrefix, string username, string password)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        // Copy response headers
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (_stripHeaders.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            // Forward Set-Cookie from camera to browser (rewrite path)
            if (string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var val in header.Value)
                {
                    // Rewrite Path= to proxy path so browser sends cookie back through proxy
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

        // Read body and handle text content
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (_rewritableContentTypes.Any(ct => contentType.Contains(ct, StringComparison.OrdinalIgnoreCase)))
        {
            var body = await response.Content.ReadAsStringAsync();

            // Rewrite absolute paths to go through proxy
            body = AbsolutePathRegex().Replace(body, $"{proxyPrefix}/$1");

            // Auto-login injection: if this is the login page, inject script to auto-fill and submit
            if (body.Contains("id=\"username\"") && body.Contains("id=\"password\"") && body.Contains("login()"))
            {
                var autoLoginScript = $@"
<script>
(function() {{
    // Wait for AngularJS to initialize, then auto-login
    var attempts = 0;
    var timer = setInterval(function() {{
        attempts++;
        var scope = null;
        try {{
            var el = document.querySelector('[ng-controller=""loginController""]');
            if (el) scope = angular.element(el).scope();
        }} catch(e) {{}}
        if (scope && scope.login) {{
            clearInterval(timer);
            scope.$apply(function() {{
                scope.username = '{EscapeJs(username)}';
                scope.password = '{EscapeJs(password)}';
            }});
            setTimeout(function() {{ scope.login(); }}, 200);
        }} else if (attempts > 50) {{
            clearInterval(timer);
        }}
    }}, 100);
}})();
</script>";
                body = body.Replace("</html>", autoLoginScript + "\n</html>");
            }

            await context.Response.WriteAsync(body);
        }
        else
        {
            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    private static string EscapeJs(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");

    /// <summary>
    /// Clears cached auth state for a camera. Called by settings UI when credentials change.
    /// </summary>
    public static void ClearSession(int cameraIndex)
    {
        // No cached state to clear in current implementation,
        // but kept for API compatibility with settings UI
    }
}
