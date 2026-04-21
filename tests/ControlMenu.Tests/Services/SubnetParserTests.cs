using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class SubnetParserTests
{
    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.0/24", 254)]
    [InlineData("192.168.1.5/24", "192.168.1.0/24", 254)]  // non-network IP → normalized
    [InlineData("10.0.0.0/16", "10.0.0.0/16", 65534)]
    [InlineData("192.168.1.5/32", "192.168.1.5/32", 1)]
    [InlineData("192.168.1.0/31", "192.168.1.0/31", 2)]
    public void ParseCidr_Valid(string input, string normalized, int hostCount)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(normalized, result.Value!.Normalized);
        Assert.Equal(hostCount, result.Value.HostCount);
    }

    [Theory]
    [InlineData("192.168.1.5")]
    public void ParseBareIp_NormalizesToSlash32(string input)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal($"{input}/32", result.Value!.Normalized);
        Assert.Equal(1, result.Value.HostCount);
    }

    [Theory]
    [InlineData("192.168.1.10-192.168.1.50", "192.168.1.10-192.168.1.50", 41)]
    [InlineData("192.168.1.10-20", "192.168.1.10-192.168.1.20", 11)]              // shorthand
    [InlineData("192.168.1.0-192.168.1.255", "192.168.1.0-192.168.1.255", 254)]   // skip .0 + .255
    public void ParseRange_Valid(string input, string normalized, int hostCount)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(normalized, result.Value!.Normalized);
        Assert.Equal(hostCount, result.Value.HostCount);
    }

    [Theory]
    [InlineData("", "Unrecognized format")]
    [InlineData("not-an-ip", "Invalid start IP")]
    [InlineData("192.168.1.0/8", "Subnet too large")]  // /8 is < /16
    [InlineData("192.168.1.0/33", "Prefix must be between /16 and /32")]
    [InlineData("999.168.1.0/24", "Invalid IP address")]
    [InlineData("192.168.1.50-192.168.1.10", "Range start must be")]
    public void Parse_Invalid_ReturnsErrorWithExpectedSubstring(string input, string expectedSubstring)
    {
        var result = SubnetParser.Parse(input);
        Assert.False(result.IsSuccess);
        Assert.Contains(expectedSubstring, result.Error);
    }

    [Fact]
    public void ParseRange_TooLarge_Rejected()
    {
        // >65536 addresses — 0.0.0.0 to 1.0.0.0 is 16M+
        var result = SubnetParser.Parse("0.0.0.0-1.0.0.0");
        Assert.False(result.IsSuccess);
        Assert.Contains("Range too large", result.Error);
    }
}
