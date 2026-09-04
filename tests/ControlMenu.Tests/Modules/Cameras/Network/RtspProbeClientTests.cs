using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class RtspProbeClientTests
{
    private readonly RtspProbeClient _sut = new(NullLogger<RtspProbeClient>.Instance);

    [Fact]
    public async Task ProbeTcpAsync_ReturnsTrue_WhenPortListening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var result = await _sut.ProbeTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(1), default);
            Assert.True(result);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeTcpAsync_ReturnsFalse_WhenPortClosed()
    {
        // Port 1 is privileged + virtually never open
        var result = await _sut.ProbeTcpAsync("127.0.0.1", 1, TimeSpan.FromMilliseconds(500), default);
        Assert.False(result);
    }

    [Fact]
    public async Task ProbeTcpAsync_GivesUpAtTheTimeout_WhenTheHostDropsSyns()
    {
        // 192.0.2.1 is RFC 5737 TEST-NET-1: nothing answers, so the SYN is dropped rather than
        // refused, and a raw connect sits in the OS retransmit path for ~21s on Windows (measured
        // 2026-09-04). The liveness tick probes cameras one after another, so a probe that ran to
        // that limit would stall the whole tick for every powered-off camera. This pins the
        // timeout as the bound: on net10 the token cancels ConnectAsync at 0.5s, 15 runs of 15.
        var sw = Stopwatch.StartNew();
        var result = await _sut.ProbeTcpAsync("192.0.2.1", 554, TimeSpan.FromMilliseconds(500), default);
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"probe took {sw.Elapsed.TotalSeconds:F1}s against a 0.5s timeout");
    }

    [Fact]
    public async Task DescribeAsync_GivesUpAtTheTimeout_WhenTheHostDropsSyns()
    {
        // Same connect, two methods down: a scan that DESCRIBEs a camera that has since gone
        // dark must fail at the timeout, not at the OS's retransmit limit. Pinned for the same
        // reason as the probe above.
        var sw = Stopwatch.StartNew();
        var result = await _sut.DescribeAsync("rtsp://192.0.2.1:554/stream", TimeSpan.FromMilliseconds(500), default);
        sw.Stop();

        Assert.False(result.Success);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"describe took {sw.Elapsed.TotalSeconds:F1}s against a 0.5s timeout");
    }

    [Fact]
    public async Task DescribeAsync_Returns200_WithSdpBody_WhenServerAccepts()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[4096];
            // Drain the client request. The count is checked rather than discarded: a zero-length
            // read means the client hung up without sending, and replying to that would make the
            // assertions below fail for a reason that has nothing to do with the code under test.
            if (await stream.ReadAsync(buf) == 0) return;
            var resp = "RTSP/1.0 200 OK\r\nCSeq: 1\r\nContent-Type: application/sdp\r\nContent-Length: 22\r\n\r\nv=0\r\no=- 0 0 IN IP4 0\r\n";
            var bytes = Encoding.ASCII.GetBytes(resp);
            await stream.WriteAsync(bytes);
        });
        try
        {
            var result = await _sut.DescribeAsync($"rtsp://127.0.0.1:{port}/test", TimeSpan.FromSeconds(2), default);
            Assert.True(result.Success);
            Assert.Equal(200, result.StatusCode);
            Assert.NotNull(result.Sdp);
            Assert.Contains("v=0", result.Sdp);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DescribeAsync_Returns401_OnUnauthorized()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[4096];
            // Drain the client request. The count is checked rather than discarded: a zero-length
            // read means the client hung up without sending, and replying to that would make the
            // assertions below fail for a reason that has nothing to do with the code under test.
            if (await stream.ReadAsync(buf) == 0) return;
            var resp = "RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\nWWW-Authenticate: Basic realm=\"cam\"\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp));
        });
        try
        {
            var result = await _sut.DescribeAsync($"rtsp://user:pass@127.0.0.1:{port}/test", TimeSpan.FromSeconds(2), default);
            Assert.False(result.Success);
            Assert.Equal(401, result.StatusCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DescribeAsync_AuthenticatesWithDigest_AndNeverSendsPreemptiveOrBasicCredentials()
    {
        const string user = "admin";
        const string password = "p@ss:word";   // ':' and '@' exercise userinfo splitting/decoding
        const string realm = "IP Camera";
        const string nonce = "0a0b0c0d0e0f1011";
        const string opaque = "5ccc069c403ebaf9f0171e9517f40e41";

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        string firstRequest = "";
        string? authHeader = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                using var client = await listener.AcceptTcpClientAsync(serverCts.Token);
                using var stream = client.GetStream();

                // 1) First DESCRIBE — must carry NO credentials (no preemptive auth).
                firstRequest = await ReadHeadersAsync(stream, serverCts.Token);
                var challenge = $"RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\n" +
                    $"WWW-Authenticate: Digest realm=\"{realm}\", nonce=\"{nonce}\", qop=\"auth\", opaque=\"{opaque}\"\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(challenge), serverCts.Token);

                // 2) Second DESCRIBE — must answer the Digest challenge.
                var secondRequest = await ReadHeadersAsync(stream, serverCts.Token);
                authHeader = secondRequest.Split("\r\n")
                    .FirstOrDefault(l => l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));

                var ok = ValidateDigest(authHeader, user, password, realm, nonce, "DESCRIBE");
                var resp = ok
                    ? "RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\nContent-Length: 22\r\n\r\nv=0\r\no=- 0 0 IN IP4 0\r\n"
                    : "RTSP/1.0 401 Unauthorized\r\nCSeq: 2\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), serverCts.Token);
            }
            catch (OperationCanceledException) { /* client returned early — assertions below report why */ }
        });

        try
        {
            var encUser = Uri.EscapeDataString(user);
            var encPass = Uri.EscapeDataString(password);
            var result = await _sut.DescribeAsync(
                $"rtsp://{encUser}:{encPass}@127.0.0.1:{port}/Streaming/Channels/101",
                TimeSpan.FromSeconds(3), default);
            await serverTask;

            Assert.DoesNotContain("Authorization", firstRequest, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(authHeader);
            Assert.DoesNotContain("Basic", authHeader);   // never transmit Basic (reversible password)
            Assert.True(result.Success, $"Expected 200 after Digest auth. Authorization sent: {authHeader}");
            Assert.Equal(200, result.StatusCode);
            Assert.Contains("v=0", result.Sdp);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Reads from the stream until the end of the RTSP message headers (CRLF CRLF).</summary>
    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[4096];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) break;
            sb.Append(Encoding.ASCII.GetString(buf, 0, read));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Independently recomputes the RFC 2617 Digest response from the client's Authorization
    /// header and compares — proves the client's digest is correct without sharing its code.
    /// </summary>
    private static bool ValidateDigest(string? header, string user, string password, string realm, string nonce, string method)
    {
        if (header is null) return false;
        var value = header[(header.IndexOf(':') + 1)..].Trim();
        if (!value.StartsWith("Digest", StringComparison.OrdinalIgnoreCase)) return false;

        var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(value, "(\\w+)\\s*=\\s*(?:\"([^\"]*)\"|([^,]*))"))
            p[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value.Trim();

        if (p.GetValueOrDefault("username") != user) return false;

        static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
        var ha1 = Md5($"{user}:{realm}:{password}");
        var ha2 = Md5($"{method}:{p.GetValueOrDefault("uri")}");
        var qop = p.GetValueOrDefault("qop");
        var expected = string.IsNullOrEmpty(qop)
            ? Md5($"{ha1}:{nonce}:{ha2}")
            : Md5($"{ha1}:{nonce}:{p.GetValueOrDefault("nc")}:{p.GetValueOrDefault("cnonce")}:{qop}:{ha2}");
        return string.Equals(expected, p.GetValueOrDefault("response"), StringComparison.OrdinalIgnoreCase);
    }
}
