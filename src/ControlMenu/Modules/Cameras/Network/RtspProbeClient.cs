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
            var request = new StringBuilder()
                .Append("DESCRIBE ").Append(requestUri).Append(" RTSP/1.0\r\n")
                .Append("CSeq: 1\r\n")
                .Append("Accept: application/sdp\r\n");

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(uri.UserInfo));
                request.Append("Authorization: Basic ").Append(basic).Append("\r\n");
            }
            request.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(request.ToString()), cts.Token);

            var buf = new byte[8192];
            var read = await stream.ReadAsync(buf, cts.Token);
            var responseText = Encoding.ASCII.GetString(buf, 0, read);
            return ParseRtspResponse(responseText);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RTSP DESCRIBE failed for {Url}", RedactUrlCredentials(rtspUrl));
            return new RtspDescribeResult(false, 0, ex.Message, null);
        }
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

    // Replace any userinfo (user:password@) in the URL with "***@" before logging.
    // Prevents cleartext credential exposure per CodeQL rule
    // cs/cleartext-storage-of-sensitive-information.
    private static string RedactUrlCredentials(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            return url.Replace(uri.UserInfo + "@", "***@");
        return url;
    }
}
