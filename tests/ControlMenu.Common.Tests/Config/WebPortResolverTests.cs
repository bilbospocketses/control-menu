using ControlMenu.Common.Config;
using Xunit;

namespace ControlMenu.Common.Tests.Config;

public class WebPortResolverTests
{
    [Fact]
    public void GetKestrelUrl_NoConfiguredPort_ReturnsDefault5159()
    {
        var cfg = new AppConfig();
        Assert.Equal("http://localhost:5159", WebPortResolver.GetKestrelUrl(cfg));
    }

    [Fact]
    public void GetKestrelUrl_ConfiguredPort_ReturnsThatPort()
    {
        var cfg = new AppConfig { WebPort = 7000 };
        Assert.Equal("http://localhost:7000", WebPortResolver.GetKestrelUrl(cfg));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void GetKestrelUrl_OutOfRangePort_FallsBackToDefault(int badPort)
    {
        var cfg = new AppConfig { WebPort = badPort };
        Assert.Equal("http://localhost:5159", WebPortResolver.GetKestrelUrl(cfg));
    }
}
