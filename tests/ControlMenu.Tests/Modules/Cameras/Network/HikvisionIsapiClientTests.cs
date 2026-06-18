using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class HikvisionIsapiClientTests
{
    /// <summary>
    /// Builds a client whose <see cref="IHttpClientFactory"/> hands out an HttpClient backed by a
    /// mocked transport. <paramref name="responses"/> are returned in order (the last one repeats),
    /// so a challenge→retry flow can be simulated. Captures each request's Authorization header.
    /// </summary>
    private static (HikvisionIsapiClient Sut, List<string?> AuthHeaders) CreateClient(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        var authHeaders = new List<string?>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                if (req.Content is not null) await req.Content.LoadIntoBufferAsync();
                authHeaders.Add(req.Headers.TryGetValues("Authorization", out var v) ? string.Concat(v) : null);
                return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            });
        var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(http);
        return (new HikvisionIsapiClient(factory.Object, NullLogger<HikvisionIsapiClient>.Instance), authHeaders);
    }

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    private const string DeviceXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DeviceInfo>
          <deviceName>Back Porch</deviceName>
          <model>CMIP7352W</model>
          <serialNumber>CMIP7352W20170428AAWR756864499</serialNumber>
          <firmwareVersion>V5.6.2</firmwareVersion>
          <macAddress>14:2f:fd:02:1a:36</macAddress>
          <telecontrolID>1</telecontrolID>
        </DeviceInfo>
        """;

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsParsedFields_OnSuccess()
    {
        var (sut, _) = CreateClient(Xml(DeviceXml));

        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, "admin", "secret", default);

        Assert.NotNull(info);
        Assert.Equal("Back Porch", info.DeviceName);
        Assert.Equal("CMIP7352W", info.Model);
        Assert.Equal("CMIP7352W20170428AAWR756864499", info.SerialNumber);
        Assert.Equal("V5.6.2", info.FirmwareVersion);
        Assert.Equal("14-2f-fd-02-1a-36", info.MacAddress);
        Assert.Equal(1, info.TelecontrolId);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsRecordWithNulls_WhenFieldsMissing()
    {
        var (sut, _) = CreateClient(Xml("<DeviceInfo><deviceName>Cam</deviceName></DeviceInfo>"));
        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, "u", "p", default);
        Assert.NotNull(info);
        Assert.Equal("Cam", info.DeviceName);
        Assert.Null(info.MacAddress);
        Assert.Null(info.TelecontrolId);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_On401WithoutUsableChallenge()
    {
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") });
        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, "admin", "wrong", default);
        Assert.Null(info);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_On404()
    {
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, "admin", "secret", default);
        Assert.Null(info);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_OnInvalidXml()
    {
        var (sut, _) = CreateClient(Xml("not xml"));
        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, "admin", "secret", default);
        Assert.Null(info);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_AnswersDigestChallenge_AndSendsNoPreemptiveCredentials()
    {
        const string user = "admin";
        const string password = "p@ss:word";
        const string realm = "IP Camera";
        const string nonce = "0a0b0c0d0e0f";

        var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
        challenge.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Digest",
            $"realm=\"{realm}\", nonce=\"{nonce}\", qop=\"auth\""));

        var (sut, authHeaders) = CreateClient(challenge, Xml(DeviceXml));

        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, user, password, default);

        Assert.NotNull(info);                       // succeeded only if the digest was correct (2nd response)
        Assert.Equal(2, authHeaders.Count);
        Assert.Null(authHeaders[0]);                // first request: no preemptive credentials
        Assert.NotNull(authHeaders[1]);
        Assert.StartsWith("Digest", authHeaders[1]);
        Assert.True(ValidateDigest(authHeaders[1], user, password, realm, nonce, "GET", "/ISAPI/System/deviceInfo"),
            $"Digest response did not validate. Sent: {authHeaders[1]}");
    }

    [Fact]
    public async Task GetDeviceInfoAsync_AnswersBasicChallenge_WhenDigestNotOffered()
    {
        const string user = "admin";
        const string password = "secret";

        var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
        challenge.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Basic", "realm=\"cam\""));

        var (sut, authHeaders) = CreateClient(challenge, Xml(DeviceXml));

        var info = await sut.GetDeviceInfoAsync("192.168.1.10", 80, user, password, default);

        Assert.NotNull(info);
        Assert.Null(authHeaders[0]);                // not preemptive
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        Assert.Equal(expected, authHeaders[1]);
    }

    /// <summary>Independently recomputes the RFC 2617 Digest response to verify the client's value.</summary>
    private static bool ValidateDigest(string? header, string user, string password, string realm, string nonce, string method, string uri)
    {
        if (header is null) return false;
        var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Regex.Matches(header, "(\\w+)\\s*=\\s*(?:\"([^\"]*)\"|([^,]*))").Cast<System.Text.RegularExpressions.Match>())
            p[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value.Trim();

        if (p.GetValueOrDefault("username") != user || p.GetValueOrDefault("uri") != uri) return false;

        static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
        var ha1 = Md5($"{user}:{realm}:{password}");
        var ha2 = Md5($"{method}:{uri}");
        var qop = p.GetValueOrDefault("qop");
        var expected = string.IsNullOrEmpty(qop)
            ? Md5($"{ha1}:{nonce}:{ha2}")
            : Md5($"{ha1}:{nonce}:{p.GetValueOrDefault("nc")}:{p.GetValueOrDefault("cnonce")}:{qop}:{ha2}");
        return string.Equals(expected, p.GetValueOrDefault("response"), StringComparison.OrdinalIgnoreCase);
    }
}
