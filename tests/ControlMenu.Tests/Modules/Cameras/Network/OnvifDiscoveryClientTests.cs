using ControlMenu.Modules.Cameras.Network;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class OnvifDiscoveryClientTests
{
    [Fact]
    public void ResolveTrustedServiceUrl_RewritesHostToResponder_WhenXAddrsLies()
    {
        // A spoofed device answers from 192.168.1.50 but advertises its ONVIF service on a
        // foreign host — credentials must go to the responder, never the advertised host.
        var url = OnvifDiscoveryClient.ResolveTrustedServiceUrl(
            "192.168.1.50", "http://10.0.0.1:8080/onvif/device_service");
        Assert.Equal("http://192.168.1.50:8080/onvif/device_service", url);
    }

    [Fact]
    public void ResolveTrustedServiceUrl_PreservesPath_WithDefaultPort()
    {
        var url = OnvifDiscoveryClient.ResolveTrustedServiceUrl(
            "192.168.1.50", "http://192.168.1.50/onvif/device_service");
        Assert.Equal("http://192.168.1.50/onvif/device_service", url);
    }

    [Fact]
    public void ResolveTrustedServiceUrl_PicksFirstHttpUrl_FromSpaceSeparatedXAddrs()
    {
        var url = OnvifDiscoveryClient.ResolveTrustedServiceUrl(
            "192.168.1.50", "http://10.0.0.1/onvif/device_service https://10.0.0.1/onvif");
        Assert.Equal("http://192.168.1.50/onvif/device_service", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://10.0.0.1/x")]
    [InlineData("not a url")]
    public void ResolveTrustedServiceUrl_ReturnsNull_WhenNoHttpUrl(string xaddrs)
    {
        Assert.Null(OnvifDiscoveryClient.ResolveTrustedServiceUrl("192.168.1.50", xaddrs));
    }
}
