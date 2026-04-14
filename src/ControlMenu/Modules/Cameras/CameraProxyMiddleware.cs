using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

/// <summary>
/// Reverse proxy middleware for Hikvision/LTS cameras. Performs ISAPI session login
/// to get a WebSession cookie, forwards it to the browser, and proxies all requests
/// with digest auth. Strips iframe-blocking headers and rewrites absolute paths.
/// </summary>
public partial class CameraProxyMiddleware(RequestDelegate next, ILogger<CameraProxyMiddleware> logger)
{
    // Cache session cookies per camera so we don't re-login on every request
    private static readonly ConcurrentDictionary<int, string> _sessionCookies = new();

    private static readonly string[] _stripHeaders =
    [
        "X-Frame-Options",
        "Content-Security-Policy",
        "Content-Security-Policy-Report-Only"
    ];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

    private static readonly string[] _rewritableContentTypes = ["text/html", "text/javascript", "application/javascript", "text/css"];

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
        var (username, password) = credentials.Value;

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            UseCookies = false,
            AllowAutoRedirect = false,
            PreAuthenticate = false
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        // Ensure we have a session cookie (ISAPI session login)
        if (!_sessionCookies.ContainsKey(cameraIndex))
        {
            var sessionCookie = await PerformSessionLoginAsync(client, username, password, cameraIndex);
            if (sessionCookie is not null)
                _sessionCookies[cameraIndex] = sessionCookie;
        }

        var targetUri = new Uri(baseUri, $"/{proxyPath}{context.Request.QueryString}");
        var response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);

        // On 401, clear session and retry with fresh login
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Camera {Index} returned 401, re-authenticating", cameraIndex);
            _sessionCookies.TryRemove(cameraIndex, out _);
            var sessionCookie = await PerformSessionLoginAsync(client, username, password, cameraIndex);
            if (sessionCookie is not null)
                _sessionCookies[cameraIndex] = sessionCookie;
            response = await ForwardRequestAsync(client, context, targetUri, cameraIndex);
        }

        await WriteProxyResponseAsync(context, response, cameraIndex, proxyPrefix);
    }

    /// <summary>
    /// Performs the Hikvision ISAPI session login challenge-response flow.
    /// Returns the full Set-Cookie header value on success, or null on failure.
    /// </summary>
    private async Task<string?> PerformSessionLoginAsync(HttpClient client, string username, string password, int cameraIndex)
    {
        try
        {
            // Step 1: Get capabilities (challenge, salt, iterations, sessionID)
            var capResponse = await client.GetAsync("/ISAPI/Security/sessionLogin/capabilities");
            if (!capResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Camera {Index} sessionLogin capabilities returned {Status}", cameraIndex, capResponse.StatusCode);
                return null;
            }

            var capXml = await capResponse.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(capXml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var sessionID = doc.Root?.Element(ns + "sessionID")?.Value;
            var challenge = doc.Root?.Element(ns + "challenge")?.Value;
            var salt = doc.Root?.Element(ns + "salt")?.Value;
            var iterationsStr = doc.Root?.Element(ns + "iterations")?.Value;
            var isIrreversible = doc.Root?.Element(ns + "isIrreversible")?.Value;

            if (sessionID is null || challenge is null)
            {
                logger.LogWarning("Camera {Index} capabilities missing sessionID or challenge", cameraIndex);
                return null;
            }

            var iterations = int.TryParse(iterationsStr, out var iter) ? iter : 100;

            // Step 2: Hash password using Hikvision's challenge-response
            string hashedPassword;
            if (string.Equals(isIrreversible, "true", StringComparison.OrdinalIgnoreCase) && salt is not null)
            {
                // Irreversible mode: SHA-256 with salt and iterations
                hashedPassword = HashPasswordIrreversible(username, password, salt, challenge, iterations);
            }
            else
            {
                // Legacy mode: simple SHA-256(password) + challenge
                hashedPassword = HashPasswordLegacy(password, challenge);
            }

            // Step 3: POST session login with hashed password
            var loginXml = $@"<SessionLogin>
<userName>{username}</userName>
<password>{hashedPassword}</password>
<sessionID>{sessionID}</sessionID>
</SessionLogin>";

            var loginContent = new StringContent(loginXml, Encoding.UTF8, "application/xml");
            var loginResponse = await client.PostAsync("/ISAPI/Security/sessionLogin", loginContent);

            if (loginResponse.IsSuccessStatusCode)
            {
                // Extract Set-Cookie header
                if (loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    var cookie = cookies.FirstOrDefault(c =>
                        c.Contains("WebSession", StringComparison.OrdinalIgnoreCase));
                    if (cookie is not null)
                    {
                        var cookieValue = cookie.Split(';')[0]; // Just name=value
                        logger.LogInformation("Camera {Index} ISAPI session login succeeded", cameraIndex);
                        return cookieValue;
                    }
                }

                logger.LogWarning("Camera {Index} session login succeeded but no WebSession cookie in response", cameraIndex);
            }
            else
            {
                var body = await loginResponse.Content.ReadAsStringAsync();
                logger.LogWarning("Camera {Index} session login returned {Status}: {Body}",
                    cameraIndex, loginResponse.StatusCode, body.Length > 200 ? body[..200] : body);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera {Index} ISAPI session login failed", cameraIndex);
        }

        return null;
    }

    /// <summary>
    /// Hikvision irreversible password hashing: SHA256(SHA256(username + salt + password) + challenge)
    /// with multiple iterations.
    /// </summary>
    private static string HashPasswordIrreversible(string username, string password, string salt, string challenge, int iterations)
    {
        // Initial hash: SHA256(password)
        var passwordHash = Sha256Hex(password);

        // Iterate: SHA256(username + salt + passwordHash)
        var result = passwordHash;
        for (var i = 1; i < iterations; i++)
        {
            result = Sha256Hex(username + salt + result);
        }

        // Final: SHA256(result + challenge)
        return Sha256Hex(result + challenge);
    }

    /// <summary>
    /// Legacy (reversible) password hashing: SHA256(password + challenge)
    /// </summary>
    private static string HashPasswordLegacy(string password, string challenge)
    {
        return Sha256Hex(password + challenge);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(
        HttpClient client, HttpContext context, Uri targetUri, int cameraIndex)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            // Replace browser cookies with camera session cookie
            if (string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Attach the session cookie from our cache
        if (_sessionCookies.TryGetValue(cameraIndex, out var sessionCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
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

    public static void ClearSession(int cameraIndex)
    {
        _sessionCookies.TryRemove(cameraIndex, out _);
    }
}
