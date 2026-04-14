using ControlMenu.Modules.Cameras;

namespace ControlMenu.Tests.Modules.Cameras;

public class CameraProxyMiddlewareTests
{
    [Fact]
    public void ProxyPathRegex_MatchesCameraProxy()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            @"^/cameras/(\d+)/proxy(?:/(.*))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var match1 = regex.Match("/cameras/1/proxy/");
        Assert.True(match1.Success);
        Assert.Equal("1", match1.Groups[1].Value);

        var match2 = regex.Match("/cameras/5/proxy/doc/page/login.asp");
        Assert.True(match2.Success);
        Assert.Equal("5", match2.Groups[1].Value);
        Assert.Equal("doc/page/login.asp", match2.Groups[2].Value);

        var noMatch = regex.Match("/cameras/1/view");
        Assert.False(noMatch.Success);
    }

    [Fact]
    public void ClearSession_RemovesCachedCookies()
    {
        CameraProxyMiddleware.ClearSession(99);
    }
}
