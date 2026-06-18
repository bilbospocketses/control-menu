using ControlMenu.Common.Paths;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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

    [Fact]
    public async Task FindExecutableAsync_ReturnsNull_WhenGo2RtcNotInstalled()
    {
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("cameras", "go2rtc", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DependencyNotInstalledException("cameras", "go2rtc", @"C:\deps\go2rtc.exe"));

        var path = await BuildService(resolver.Object).FindExecutableAsync();

        Assert.Null(path);
    }

    [Fact]
    public async Task FindExecutableAsync_ReturnsResolvedPath_WhenInstalled()
    {
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("cameras", "go2rtc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(@"C:\deps\go2rtc\go2rtc.exe");

        var path = await BuildService(resolver.Object).FindExecutableAsync();

        Assert.Equal(@"C:\deps\go2rtc\go2rtc.exe", path);
    }

    private static Go2RtcService BuildService(IDependencyPathResolver resolver)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IDependencyPathResolver))).Returns(resolver);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var dataPath = new Mock<IDataPathResolver>();
        dataPath.Setup(d => d.GetConfigDir()).Returns(Path.GetTempPath());

        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.Setup(l => l.ApplicationStopping).Returns(CancellationToken.None);

        return new Go2RtcService(
            scopeFactory.Object,
            NullLogger<Go2RtcService>.Instance,
            new Mock<ICameraChangeNotifier>().Object,
            lifetime.Object,
            dataPath.Object);
    }
}
