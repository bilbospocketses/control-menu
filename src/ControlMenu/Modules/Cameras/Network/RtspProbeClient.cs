using System.Net.Sockets;
using System.Text;

namespace ControlMenu.Modules.Cameras.Network;

public class RtspProbeClient : IRtspProbeClient
{
    private readonly ILogger<RtspProbeClient> _logger;

    public RtspProbeClient(ILogger<RtspProbeClient> logger) => _logger = logger;

    public async Task<bool> ProbeTcpAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RtspDescribeResult> DescribeAsync(string rtspUrl, TimeSpan timeout, CancellationToken ct)
    {
        var uri = new Uri(rtspUrl);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, uri.Port, cts.Token);
            using var stream = client.GetStream();

            var requestUri = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";

            // Never send credentials preemptively. The previous implementation base64'd
            // the userinfo into an `Authorization: Basic` header on the very first request,
            // which (a) leaks a trivially-reversible password to whatever answers on the
            // socket — including a hostile device answering network discovery — and (b) is
            // cleartext over the unencrypted TCP/554 channel. We send an unauthenticated
            // DESCRIBE first and only respond to an explicit Digest challenge.
            var firstResponse = await SendDescribeAsync(stream, requestUri, cseq: 1, authorization: null, cts.Token);
            var result = ParseRtspResponse(firstResponse);

            if (result.StatusCode == 401 && !string.IsNullOrEmpty(uri.UserInfo))
            {
                var challenge = ExtractDigestChallenge(firstResponse);
                // Answer ONLY a Digest challenge. We deliberately do not fall back to Basic:
                // Basic would put the recoverable password back on the wire. A Basic-only
                // server therefore stays a 401 here (its credentials are surfaced to the user).
                if (challenge is not null)
                {
                    var (user, password) = SplitUserInfo(uri.UserInfo);
                    var authorization = DigestAuthHelper.BuildDigest(challenge, user, password, "DESCRIBE", requestUri);
                    var authedResponse = await SendDescribeAsync(stream, requestUri, cseq: 2, authorization, cts.Token);
                    result = ParseRtspResponse(authedResponse);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log only non-credential Uri components — never the raw rtspUrl string,
            // which could include userinfo (user:password@) per RFC 3986. CodeQL's
            // taint flow analysis (cs/cleartext-storage-of-sensitive-information)
            // recognizes Uri.Scheme/Host/Port/AbsolutePath as safe properties.
            _logger.LogDebug(ex, "RTSP DESCRIBE failed for {Scheme}://{Host}:{Port}{Path}",
                uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath);
            return new RtspDescribeResult(false, 0, ex.Message, null);
        }
    }

    private static async Task<string> SendDescribeAsync(
        NetworkStream stream, string requestUri, int cseq, string? authorization, CancellationToken ct)
    {
        var request = new StringBuilder()
            .Append("DESCRIBE ").Append(requestUri).Append(" RTSP/1.0\r\n")
            .Append("CSeq: ").Append(cseq).Append("\r\n")
            .Append("Accept: application/sdp\r\n");
        if (!string.IsNullOrEmpty(authorization))
            request.Append("Authorization: ").Append(authorization).Append("\r\n");
        request.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), ct);
        return await ReadResponseAsync(stream, ct);
    }

    /// <summary>
    /// Reads a full RTSP response. A single <c>ReadAsync</c> is not guaranteed to return the
    /// whole message, so we loop until the headers are complete and the declared
    /// Content-Length body has arrived (or the peer closes / a 64 KiB safety cap is hit).
    /// </summary>
    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[8192];
        var sb = new StringBuilder();
        var headerEnd = -1;
        var contentLength = 0;

        while (sb.Length < 65536)
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) break;
            sb.Append(Encoding.ASCII.GetString(buf, 0, read));

            if (headerEnd < 0)
            {
                headerEnd = sb.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0)
                    contentLength = ParseContentLength(sb.ToString(0, headerEnd));
            }
            if (headerEnd >= 0 && sb.Length - (headerEnd + 4) >= contentLength)
                break;
        }
        return sb.ToString();
    }

    private static int ParseContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out var n) ? n : 0;
        }
        return 0;
    }

    /// <summary>Parses the first <c>WWW-Authenticate: Digest …</c> challenge, if any.</summary>
    private static IReadOnlyDictionary<string, string>? ExtractDigestChallenge(string response)
    {
        foreach (var line in response.Split("\r\n"))
        {
            if (!line.StartsWith("WWW-Authenticate:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line["WWW-Authenticate:".Length..].Trim();
            if (value.StartsWith("Digest", StringComparison.OrdinalIgnoreCase))
                return DigestAuthHelper.ParseChallengeParams(value["Digest".Length..]);
        }
        return null;
    }

    /// <summary>Splits and percent-decodes <c>user:password</c> userinfo from the RTSP URL.</summary>
    private static (string User, string Password) SplitUserInfo(string userInfo)
    {
        var idx = userInfo.IndexOf(':');
        return idx < 0
            ? (Uri.UnescapeDataString(userInfo), "")
            : (Uri.UnescapeDataString(userInfo[..idx]), Uri.UnescapeDataString(userInfo[(idx + 1)..]));
    }

    private static RtspDescribeResult ParseRtspResponse(string text)
    {
        var lines = text.Split("\r\n");
        if (lines.Length == 0 || !lines[0].StartsWith("RTSP/"))
            return new RtspDescribeResult(false, 0, "Invalid RTSP response", null);

        var statusParts = lines[0].Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var code))
            return new RtspDescribeResult(false, 0, "Bad status line", null);
        var reason = statusParts.Length >= 3 ? statusParts[2] : "";

        var bodyStart = text.IndexOf("\r\n\r\n");
        var body = bodyStart >= 0 ? text[(bodyStart + 4)..] : null;

        return new RtspDescribeResult(code == 200, code, reason, body);
    }

}
