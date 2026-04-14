using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

/// <summary>
/// Reverse proxy middleware for Hikvision/LTS cameras.
/// Performs ISAPI session login, injects auth info into sessionStorage via script injection,
/// and proxies all requests with digest auth + session cookie.
/// </summary>
public partial class CameraProxyMiddleware(RequestDelegate next, ILogger<CameraProxyMiddleware> logger)
{
    private static readonly ConcurrentDictionary<int, CameraSession> _sessions = new();

    private record CameraSession(
        string Cookie, string SessionID, string Challenge,
        int Iterations, string Random, string Username);

    private static readonly string[] _stripHeaders =
        ["X-Frame-Options", "Content-Security-Policy", "Content-Security-Policy-Report-Only"];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

    private static readonly string[] _rewritableContentTypes =
        ["text/html", "text/javascript", "application/javascript", "text/css"];

    [GeneratedRegex(@"(?<=[""'/=])/(doc|ISAPI|SDK|PSIA|images|css|js|custom|bvw)(?=[/""])", RegexOptions.Compiled)]
    private static partial Regex AbsolutePathRegex();

    // Camera root paths that the JS constructs as absolute URLs (bypass the /cameras/X/proxy/ prefix)
    [GeneratedRegex(@"^/(ISAPI|SDK|PSIA|doc)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CameraRootPathRegex();

    // Extract camera index from Referer header
    [GeneratedRegex(@"/cameras/(\d+)/proxy/")]
    private static partial Regex RefererCameraRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Primary match: explicit proxy path /cameras/{index}/proxy/**
        var match = ProxyPathRegex().Match(path);

        // Secondary match: bare /ISAPI/**, /SDK/** etc. with a camera Referer header
        if (!match.Success && CameraRootPathRegex().IsMatch(path))
        {
            var referer = context.Request.Headers.Referer.ToString();
            var refMatch = RefererCameraRegex().Match(referer);
            if (refMatch.Success)
            {
                // Redirect to the proxy path so all logic flows through one path
                var camIdx = refMatch.Groups[1].Value;
                context.Response.Redirect($"/cameras/{camIdx}/proxy{path}{context.Request.QueryString}", false);
                return;
            }
        }

        if (!match.Success) { await next(context); return; }

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
        var (username, password) = credentials.Value;

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            UseCookies = false, AllowAutoRedirect = false, PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        // Ensure we have a session
        if (!_sessions.ContainsKey(cameraIndex))
            await LoginAndCacheSession(client, username, password, cameraIndex);

        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");
        var response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Camera {Index} returned 401, re-authenticating", cameraIndex);
            _sessions.TryRemove(cameraIndex, out _);
            await LoginAndCacheSession(client, username, password, cameraIndex);
            response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);
        }

        await WriteProxyResponseAsync(context, response, cameraIndex, proxyPrefix, username);
    }

    private async Task LoginAndCacheSession(HttpClient client, string username, string password, int cameraIndex)
    {
        var session = await PerformSessionLoginAsync(client, username, password, cameraIndex);
        if (session is not null)
            _sessions[cameraIndex] = session;
    }

    private async Task<CameraSession?> PerformSessionLoginAsync(
        HttpClient client, string username, string password, int cameraIndex)
    {
        try
        {
            var random = Random.Shared.Next(10000000, 99999999).ToString();
            var capResponse = await client.GetAsync(
                $"/ISAPI/Security/sessionLogin/capabilities?username={Uri.EscapeDataString(username)}&random={random}");
            if (!capResponse.IsSuccessStatusCode) return null;

            var doc = XDocument.Parse(await capResponse.Content.ReadAsStringAsync());
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var sessionID = doc.Root?.Element(ns + "sessionID")?.Value;
            var challenge = doc.Root?.Element(ns + "challenge")?.Value;
            var salt = doc.Root?.Element(ns + "salt")?.Value;
            var isIrreversible = doc.Root?.Element(ns + "isIrreversible")?.Value;
            var sessionIDVersion = doc.Root?.Element(ns + "sessionIDVersion")?.Value ?? "2";
            var isValidLongTerm = doc.Root?.Element(ns + "isSessionIDValidLongTerm")?.Value ?? "false";
            var iterations = int.TryParse(doc.Root?.Element(ns + "iterations")?.Value, out var iter) ? iter : 100;

            if (sessionID is null || challenge is null) return null;

            var hash = string.Equals(isIrreversible, "true", StringComparison.OrdinalIgnoreCase) && salt is not null
                ? HashIrreversible(username, password, salt, challenge, iterations)
                : HashLegacy(password, challenge, iterations);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var loginXml = $"<SessionLogin><userName>{username}</userName><password>{hash}</password><sessionID>{sessionID}</sessionID><isSessionIDValidLongTerm>{isValidLongTerm}</isSessionIDValidLongTerm><sessionIDVersion>{sessionIDVersion}</sessionIDVersion></SessionLogin>";

            using var plainClient = new HttpClient { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromSeconds(15) };
            var loginResponse = await plainClient.PostAsync(
                $"/ISAPI/Security/sessionLogin?timeStamp={timestamp}",
                new StringContent(loginXml, Encoding.UTF8, "application/xml"));

            if (loginResponse.IsSuccessStatusCode &&
                loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                var cookie = cookies.FirstOrDefault(c => c.Contains("WebSession", StringComparison.OrdinalIgnoreCase));
                if (cookie is not null)
                {
                    var cookieValue = cookie.Split(';')[0];
                    logger.LogInformation("Camera {Index} session login succeeded", cameraIndex);
                    return new CameraSession(cookieValue, sessionID, challenge, iterations, random, username);
                }
            }

            var body = await loginResponse.Content.ReadAsStringAsync();
            logger.LogWarning("Camera {Index} session login: {Status} {Body}",
                cameraIndex, loginResponse.StatusCode, body.Length > 200 ? body[..200] : body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera {Index} session login failed", cameraIndex);
        }
        return null;
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(
        HttpClient client, HttpContext context, Uri targetUri, int cameraIndex)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Attach session cookie
        if (_sessions.TryGetValue(cameraIndex, out var session))
            request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);

        if (context.Request.ContentLength > 0 || context.Request.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    }

    private static async Task WriteProxyResponseAsync(
        HttpContext context, HttpResponseMessage response, int cameraIndex, string proxyPrefix, string username)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        // Determine if this is text content we should rewrite
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var isTextContent = _rewritableContentTypes.Any(ct =>
            contentType.Contains(ct, StringComparison.OrdinalIgnoreCase));

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (_stripHeaders.Any(h => h.Equals(header.Key, StringComparison.OrdinalIgnoreCase))) continue;
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;

            // Only strip Content-Length for text content (we rewrite the body, changing length)
            // For binary content, preserve it so the browser knows the exact size
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && isTextContent) continue;

            if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
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

            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                var rewritten = header.Value.Select(v =>
                    Uri.TryCreate(v, UriKind.Absolute, out var uri)
                        ? $"{proxyPrefix}{uri.PathAndQuery}"
                        : $"{proxyPrefix}/{v.TrimStart('/')}");
                context.Response.Headers.Append(header.Key, rewritten.ToArray());
                continue;
            }

            context.Response.Headers.Append(header.Key, header.Value.ToArray());
        }

        if (isTextContent)
        {
            var body = await response.Content.ReadAsStringAsync();
            body = AbsolutePathRegex().Replace(body, $"{proxyPrefix}/$1");

            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                _sessions.TryGetValue(cameraIndex, out var session))
            {
                body = InjectAuthScript(body, session, cameraIndex);
            }

            await context.Response.WriteAsync(body);
        }
        else
        {
            // Binary pass-through: stream directly without reading into memory
            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    /// <summary>
    /// Injects a script that sets sessionStorage "authInfo" and "localPluginAuthInfo"
    /// matching what the camera's webAuth.js login flow would set.
    /// This must run BEFORE the camera's own scripts.
    /// </summary>
    private static string InjectAuthScript(string html, CameraSession session, int cameraIndex)
    {
        var user = EscapeJs(session.Username);
        var authInfo = "{\"isLogin\":true,\"authType\":4,\"auth\":\"\",\"userInfo\":\"" + user + ":\",\"szAESKey\":\"\"}";
        var authInfoB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authInfo));

        var pluginInfo = "{\"sessionID\":\"" + EscapeJs(session.SessionID) +
            "\",\"user\":\"" + user +
            "\",\"challenge\":\"" + EscapeJs(session.Challenge) +
            "\",\"iterations\":" + session.Iterations +
            ",\"szRandom\":\"" + EscapeJs(session.Random) + "\"}";
        var pluginInfoB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pluginInfo));

        var script = "\n<script type=\"text/javascript\">\n" +
            "sessionStorage.setItem(\"authInfo\",\"" + authInfoB64 + "\");\n" +
            "sessionStorage.setItem(\"localPluginAuthInfo\",\"" + pluginInfoB64 + "\");\n" +
            "</script>\n";

        // Insert right after <head> opening tag so it runs first
        var idx = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return html.Insert(idx + 6, script);

        // Fallback: insert at start of body
        idx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var bodyEnd = html.IndexOf('>', idx);
            if (bodyEnd >= 0)
                return html.Insert(bodyEnd + 1, script);
        }

        return html + script;
    }

    // --- Hashing ---

    private static string HashIrreversible(string username, string password, string salt, string challenge, int iterations)
    {
        var result = Sha256Hex(username + salt + password);
        result = Sha256Hex(result + challenge);
        for (var i = 2; i < iterations; i++)
            result = Sha256Hex(result);
        return result;
    }

    private static string HashLegacy(string password, string challenge, int iterations)
    {
        var result = Sha256Hex(password) + challenge;
        for (var i = 1; i < iterations; i++)
            result = Sha256Hex(result);
        return result;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string EscapeJs(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");

    public static void ClearSession(int cameraIndex) =>
        _sessions.TryRemove(cameraIndex, out _);
}
