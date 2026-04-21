using System.Text.RegularExpressions;

namespace ControlMenu.Services.Network;

public readonly record struct ParseResult<T>(bool IsSuccess, T? Value, string Error)
{
    public static ParseResult<T> Ok(T value) => new(true, value, "");
    public static ParseResult<T> Fail(string reason) => new(false, default, reason);
}

public static class SubnetParser
{
    private const string CHEAT = "See the subnet cheat sheet at /help/subnets.html for help.";

    public static ParseResult<ParsedSubnet> Parse(string input)
    {
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return Unrecognized();

        if (raw.Contains('/')) return ParseCidr(raw);
        if (raw.Contains('-')) return ParseRange(raw);
        if (IsValidIp(raw))
        {
            return ParseResult<ParsedSubnet>.Ok(new ParsedSubnet(raw, $"{raw}/32", 1));
        }
        return Unrecognized();
    }

    private static ParseResult<ParsedSubnet> ParseCidr(string input)
    {
        var parts = input.Split('/');
        if (parts.Length != 2) return Unrecognized();
        var (ipPart, prefixPart) = (parts[0], parts[1]);
        if (string.IsNullOrEmpty(ipPart) || string.IsNullOrEmpty(prefixPart)) return Unrecognized();
        if (!IsValidIp(ipPart)) return Fail($"Invalid IP address \"{ipPart}\". {CHEAT}");

        if (!int.TryParse(prefixPart, out var prefix) || prefix < 0 || prefix > 32)
            return Fail($"Prefix must be between /16 and /32. {CHEAT}");
        if (prefix < 16)
            return Fail("Subnet too large — maximum prefix is /16 (65,534 hosts). " +
                        "If you need to cover more than that, add multiple /16 entries " +
                        $"(one per subnet) using the 'add subnet' button. {CHEAT}");

        uint ipInt = IpToInt(ipPart);
        int maskBits = 32 - prefix;
        uint netmask = maskBits == 32 ? 0u : (0xFFFFFFFFu << maskBits);
        uint networkInt = ipInt & netmask;
        string normalizedIp = IntToIp(networkInt);
        string normalized = $"{normalizedIp}/{prefix}";

        int hostCount = prefix switch
        {
            32 => 1,
            31 => 2,
            _ => (int)(Math.Pow(2, maskBits) - 2),
        };
        return ParseResult<ParsedSubnet>.Ok(new ParsedSubnet(input, normalized, hostCount));
    }

    private static ParseResult<ParsedSubnet> ParseRange(string input)
    {
        int dashIdx = input.IndexOf('-');
        var startStr = input[..dashIdx].Trim();
        var endStr = input[(dashIdx + 1)..].Trim();

        if (!IsValidIp(startStr)) return Fail($"Invalid start IP \"{startStr}\". {CHEAT}");

        string endIp;
        if (IsValidIp(endStr)) endIp = endStr;
        else if (Regex.IsMatch(endStr, @"^\d{1,3}$"))
        {
            var sp = startStr.Split('.');
            endIp = $"{sp[0]}.{sp[1]}.{sp[2]}.{endStr}";
            if (!IsValidIp(endIp)) return Fail($"Invalid end octet \"{endStr}\". {CHEAT}");
        }
        else return Fail($"Invalid end of range \"{endStr}\". {CHEAT}");

        uint startInt = IpToInt(startStr);
        uint endInt = IpToInt(endIp);
        if (startInt > endInt)
            return Fail($"Range start must be ≤ end (got {startStr} > {endIp}). {CHEAT}");

        long literalCount = (long)endInt - startInt + 1;
        if (literalCount > 65536)
            return Fail($"Range too large — maximum is 65,536 addresses (the size of a /16 CIDR block). " +
                        $"Got {literalCount:N0}. For larger scans, split into multiple entries " +
                        $"or use CIDR notation like 10.0.0.0/16. {CHEAT}");

        bool skipFirst = (startInt & 0xFF) == 0;
        bool skipLast = (endInt & 0xFF) == 0xFF;
        uint scanStart = skipFirst ? startInt + 1 : startInt;
        uint scanEnd = skipLast ? endInt - 1 : endInt;
        int hostCount = scanEnd >= scanStart ? (int)(scanEnd - scanStart + 1) : 0;

        return ParseResult<ParsedSubnet>.Ok(
            new ParsedSubnet(input, $"{IntToIp(startInt)}-{IntToIp(endInt)}", hostCount));
    }

    private static ParseResult<ParsedSubnet> Unrecognized() =>
        Fail($"Unrecognized format. Try CIDR (192.168.1.0/24), a single IP (192.168.1.5), " +
             $"or a range (192.168.1.10-50). {CHEAT}");

    private static ParseResult<ParsedSubnet> Fail(string reason) =>
        ParseResult<ParsedSubnet>.Fail(reason);

    private static bool IsValidIp(string s)
    {
        var parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (var p in parts)
        {
            if (!Regex.IsMatch(p, @"^\d{1,3}$")) return false;
            if (!int.TryParse(p, out var n) || n < 0 || n > 255) return false;
        }
        return true;
    }

    private static uint IpToInt(string ip)
    {
        var parts = ip.Split('.').Select(int.Parse).ToArray();
        return (uint)((parts[0] << 24) | (parts[1] << 16) | (parts[2] << 8) | parts[3]);
    }

    private static string IntToIp(uint n) =>
        $"{(n >> 24) & 0xFF}.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";
}
