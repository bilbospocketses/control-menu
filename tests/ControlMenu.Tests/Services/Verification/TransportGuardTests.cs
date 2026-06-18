using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class TransportGuardTests
{
    private static readonly string[] GitHub = ["github.com", "*.githubusercontent.com"];

    [Theory]
    [InlineData("https://github.com/x/y/releases/download/v1/a.zip", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]               // wildcard CDN
    [InlineData("http://github.com/x", false)]                                     // not HTTPS
    [InlineData("https://evil.com/github.com", false)]                             // host not allowlisted
    [InlineData("https://evil-githubusercontent.com/x", false)]                    // subdomain spoof (no leading dot)
    [InlineData("https://objects.githubusercontent.com.evil.com/x", false)]       // suffix spoof
    public void IsAllowedFinalUri_EnforcesSchemeAndHost(string uri, bool expected)
        => Assert.Equal(expected, TransportGuard.IsAllowedFinalUri(new Uri(uri), GitHub));
}
