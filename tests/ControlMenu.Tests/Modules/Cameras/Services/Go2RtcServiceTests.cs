using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Tests.Modules.Cameras.Services;

public class Go2RtcServiceTests
{
    [Fact]
    public void TryBuildStreamLine_PercentEncodesCredentialMetacharacters()
    {
        // A password with @ : / ? must be encoded so it can't restructure the RTSP URL or
        // inject a go2rtc YAML line.
        var line = Go2RtcService.TryBuildStreamLine(null, "192.168.1.10", 554, "admin", "p@ss:w/rd?", out var skip);
        Assert.Null(skip);
        Assert.Equal("rtsp://admin:p%40ss%3Aw%2Frd%3F@192.168.1.10:554", line);
    }

    [Fact]
    public void TryBuildStreamLine_UsesRtspStreamUrl_ReplacingItsCredentials()
    {
        var line = Go2RtcService.TryBuildStreamLine(
            "rtsp://old:creds@10.0.0.5:8554/Streaming/Channels/101", "192.168.1.10", 554, "admin", "secret", out var skip);
        Assert.Null(skip);
        Assert.Equal("rtsp://admin:secret@10.0.0.5:8554/Streaming/Channels/101", line);
    }

    [Fact]
    public void TryBuildStreamLine_Skips_WhenRtspStreamUrlIsInvalid()
    {
        var line = Go2RtcService.TryBuildStreamLine("not a url", "192.168.1.10", 554, "admin", "secret", out var skip);
        Assert.Null(line);
        Assert.NotNull(skip);
    }

    [Theory]
    [InlineData("192.168.1.10 evil")]
    [InlineData("10.0.0.5\nexec: something")]
    public void TryBuildStreamLine_Skips_WhenHostCarriesWhitespace(string ip)
    {
        var line = Go2RtcService.TryBuildStreamLine(null, ip, 554, "admin", "secret", out var skip);
        Assert.Null(line);
        Assert.NotNull(skip);
    }
}
