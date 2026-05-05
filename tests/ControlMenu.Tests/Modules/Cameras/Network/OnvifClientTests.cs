using System.Net;
using System.Net.Http;
using System.Text;
using ControlMenu.Modules.Cameras.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class OnvifClientTests
{
    private static OnvifClient CreateClient(HttpResponseMessage response, out List<HttpRequestMessage> sentRequests)
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                if (req.Content is not null) await req.Content.LoadIntoBufferAsync();
                captured.Add(req);
                return response;
            });
        sentRequests = captured;
        var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(http);
        return new OnvifClient(factory.Object, NullLogger<OnvifClient>.Instance);
    }

    [Fact]
    public async Task GetDeviceInformationAsync_PostsSoapEnvelope_WithUsernameTokenDigest()
    {
        var soapResponse = """
            <?xml version="1.0" encoding="UTF-8"?>
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                           xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
              <soap:Body>
                <tds:GetDeviceInformationResponse>
                  <tds:Manufacturer>Hikvision</tds:Manufacturer>
                  <tds:Model>DS-2CD2143G0-I</tds:Model>
                  <tds:FirmwareVersion>V5.6.5</tds:FirmwareVersion>
                  <tds:SerialNumber>ABC123</tds:SerialNumber>
                  <tds:HardwareId>88</tds:HardwareId>
                </tds:GetDeviceInformationResponse>
              </soap:Body>
            </soap:Envelope>
            """;
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(soapResponse, Encoding.UTF8, "application/soap+xml")
        };
        var sut = CreateClient(resp, out var requests);

        var info = await sut.GetDeviceInformationAsync("http://192.168.1.50/onvif/device_service", "admin", "secret", default);

        Assert.Equal("Hikvision", info.Manufacturer);
        Assert.Equal("DS-2CD2143G0-I", info.Model);
        Assert.Single(requests);
        var body = await requests[0].Content!.ReadAsStringAsync();
        Assert.True(body.Contains("<tds:GetDeviceInformation/>") || body.Contains("<tds:GetDeviceInformation />"),
            $"Expected GetDeviceInformation element in body: {body[..Math.Min(200, body.Length)]}...");
        Assert.Contains("<wsse:UsernameToken>", body);
        Assert.Contains("<wsse:Username>admin</wsse:Username>", body);
        Assert.Contains("<wsse:Password Type=", body);
    }

    [Fact]
    public async Task GetStreamUriAsync_ParsesUriFromResponse()
    {
        var soap = """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                           xmlns:trt="http://www.onvif.org/ver10/media/wsdl">
              <soap:Body>
                <trt:GetStreamUriResponse>
                  <trt:MediaUri>
                    <tt:Uri xmlns:tt="http://www.onvif.org/ver10/schema">rtsp://192.168.1.50:554/Streaming/Channels/101</tt:Uri>
                  </trt:MediaUri>
                </trt:GetStreamUriResponse>
              </soap:Body>
            </soap:Envelope>
            """;
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(soap) };
        var sut = CreateClient(resp, out _);

        var uri = await sut.GetStreamUriAsync("http://192.168.1.50/onvif/device_service", "profile_1", "admin", "secret", default);

        Assert.Equal("rtsp://192.168.1.50:554/Streaming/Channels/101", uri);
    }

    [Fact]
    public async Task SoapFault_AuthFailed_ThrowsOnvifAuthenticationException()
    {
        var fault = """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
              <soap:Body>
                <soap:Fault>
                  <soap:Code><soap:Value>soap:Sender</soap:Value>
                    <soap:Subcode><soap:Value xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">wsse:FailedAuthentication</soap:Value></soap:Subcode>
                  </soap:Code>
                  <soap:Reason><soap:Text>Auth failed</soap:Text></soap:Reason>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;
        var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(fault) };
        var sut = CreateClient(resp, out _);

        await Assert.ThrowsAsync<OnvifAuthenticationException>(() =>
            sut.GetDeviceInformationAsync("http://192.168.1.50/onvif/device_service", "admin", "wrong", default));
    }

    [Fact]
    public async Task Http401_ThrowsOnvifAuthenticationException()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
        var sut = CreateClient(resp, out _);

        await Assert.ThrowsAsync<OnvifAuthenticationException>(() =>
            sut.GetDeviceInformationAsync("http://192.168.1.50/onvif/device_service", "admin", "wrong", default));
    }
}
