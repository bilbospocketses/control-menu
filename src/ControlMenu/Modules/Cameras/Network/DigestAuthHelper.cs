using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlMenu.Modules.Cameras.Network;

/// <summary>
/// Shared construction of HTTP/RTSP authentication headers for the camera probes
/// (<see cref="HikvisionIsapiClient"/>, <see cref="RtspProbeClient"/>). MD5 is mandated by
/// the RFC 2617 Digest wire format — it is the protocol's hash, not used for confidentiality,
/// the same constraint as the WS-Security SHA-1 digest in <see cref="OnvifClient"/>.
/// </summary>
internal static class DigestAuthHelper
{
    /// <summary>Parses a <c>WWW-Authenticate</c> challenge's <c>key=value</c> parameters (quoted or bare).</summary>
    public static Dictionary<string, string> ParseChallengeParams(string parameters)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(parameters, "(\\w+)\\s*=\\s*(?:\"([^\"]*)\"|([^,]*))"))
            dict[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value.Trim();
        return dict;
    }

    /// <summary>Builds an RFC 2617 Digest <c>Authorization</c> header value (including the "Digest " scheme).</summary>
    public static string BuildDigest(
        IReadOnlyDictionary<string, string> challenge, string user, string password, string method, string digestUri)
    {
        var realm = challenge.GetValueOrDefault("realm", "");
        var nonce = challenge.GetValueOrDefault("nonce", "");
        var opaque = challenge.GetValueOrDefault("opaque");
        var qopRaw = challenge.GetValueOrDefault("qop");
        var algorithm = challenge.GetValueOrDefault("algorithm");

        var ha1 = Md5Hex($"{user}:{realm}:{password}");
        var ha2 = Md5Hex($"{method}:{digestUri}");

        var sb = new StringBuilder("Digest ")
            .Append($"username=\"{user}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{digestUri}\"");

        // qop may be a list (e.g. "auth,auth-int"); we implement "auth".
        var useQop = qopRaw is not null &&
            qopRaw.Split(',').Any(q => q.Trim().Equals("auth", StringComparison.OrdinalIgnoreCase));

        string response;
        if (useQop)
        {
            const string nc = "00000001";
            var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            response = Md5Hex($"{ha1}:{nonce}:{nc}:{cnonce}:auth:{ha2}");
            sb.Append($", qop=auth, nc={nc}, cnonce=\"{cnonce}\"");
        }
        else
        {
            response = Md5Hex($"{ha1}:{nonce}:{ha2}");
        }

        sb.Append($", response=\"{response}\"");
        if (!string.IsNullOrEmpty(algorithm)) sb.Append($", algorithm={algorithm}");
        if (!string.IsNullOrEmpty(opaque)) sb.Append($", opaque=\"{opaque}\"");
        return sb.ToString();
    }

    /// <summary>Builds an RFC 7617 Basic <c>Authorization</c> header value (including the "Basic " scheme).</summary>
    public static string BuildBasic(string user, string password)
        => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    private static string Md5Hex(string s) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
