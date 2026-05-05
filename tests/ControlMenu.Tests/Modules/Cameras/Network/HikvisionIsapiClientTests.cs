using System.Net;
using System.Net.Http;
using System.Text;
using ControlMenu.Modules.Cameras.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class HikvisionIsapiClientTests
{
    private static (HikvisionIsapiClient Sut, List<HttpRequestMessage> Sent) CreateClient(HttpResponseMessage response)
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
        var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(http);
        return (new HikvisionIsapiClient(factory.Object, NullLogger<HikvisionIsapiClient>.Instance), captured);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsParsedFields_OnSuccess()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <DeviceInfo>
              <deviceName>Back Porch</deviceName>
              <model>CMIP7352W</model>
              <serialNumber>CMIP7352W20170428AAWR756864499</serialNumber>
              <firmwareVersion>V5.6.2</firmwareVersion>
              <firmwareReleasedDate>build 211202</firmwareReleasedDate>
              <macAddress>14:2f:fd:02:1a:36</macAddress>
              <telecontrolID>1</telecontrolID>
            </DeviceInfo>
            """;
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        });

        var info = await sut.GetDeviceInfoAsync("192.168.86.10", 80, "admin", "secret", default);

        Assert.NotNull(info);
        Assert.Equal("Back Porch", info.DeviceName);
        Assert.Equal("CMIP7352W", info.Model);
        Assert.Equal("CMIP7352W20170428AAWR756864499", info.SerialNumber);
        Assert.Equal("V5.6.2", info.FirmwareVersion);
        Assert.Equal("14-2f-fd-02-1a-36", info.MacAddress);
        Assert.Equal(1, info.TelecontrolId);
        Assert.Equal("build 211202", info.FirmwareBuildDate);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsRecordWithNulls_WhenFieldsMissing()
    {
        var xml = "<DeviceInfo><deviceName>Cam</deviceName></DeviceInfo>";
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        var info = await sut.GetDeviceInfoAsync("192.168.86.10", 80, "u", "p", default);
        Assert.NotNull(info);
        Assert.Equal("Cam", info.DeviceName);
        Assert.Null(info.MacAddress);
        Assert.Null(info.TelecontrolId);
        Assert.Null(info.FirmwareBuildDate);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_AddsBasicAuthHeader()
    {
        var (sut, captured) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<DeviceInfo/>")
        });

        await sut.GetDeviceInfoAsync("192.168.86.10", 80, "admin", "secret", default);

        Assert.Single(captured);
        Assert.Equal("Basic", captured[0].Headers.Authorization?.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(captured[0].Headers.Authorization!.Parameter!));
        Assert.Equal("admin:secret", decoded);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_On401()
    {
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") });
        var info = await sut.GetDeviceInfoAsync("192.168.86.10", 80, "admin", "wrong", default);
        Assert.Null(info);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_On404()
    {
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        var info = await sut.GetDeviceInfoAsync("192.168.86.10", 80, "admin", "secret", default);
        Assert.Null(info);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ReturnsNull_OnInvalidXml()
    {
        var (sut, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not xml")
        });
        var info = await sut.GetDeviceInfoAsync("192.168.86.10", 80, "admin", "secret", default);
        Assert.Null(info);
    }
}
