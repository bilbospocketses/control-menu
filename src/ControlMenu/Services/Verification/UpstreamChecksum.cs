using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlMenu.Services.Verification;

public static class UpstreamChecksum
{
    public static string? ExtractExpectedHash(ChecksumFormat format, string payload, string assetFileName)
        => format switch
        {
            ChecksumFormat.SqliteDownloadPage => SqlitePage(payload, assetFileName),
            ChecksumFormat.InTotoJsonl        => InToto(payload, assetFileName),
            ChecksumFormat.Sha256SumsFile     => Sha256Sums(payload, assetFileName),
            _ => null
        };

    private static string? SqlitePage(string html, string asset)
    {
        // Find the asset reference, then the nearest "(SHA3-256: <hex>)" that follows it.
        var idx = html.IndexOf(asset, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var m = Regex.Match(html[idx..], @"SHA3-256:\s*([0-9a-fA-F]{64})");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string? InToto(string jsonl, string asset)
    {
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("subject", out var subjects)) continue;
            foreach (var s in subjects.EnumerateArray())
            {
                if (s.TryGetProperty("name", out var n) && n.GetString() == asset
                    && s.TryGetProperty("digest", out var d)
                    && d.TryGetProperty("sha256", out var h))
                    return h.GetString()?.ToLowerInvariant();
            }
        }
        return null;
    }

    private static string? Sha256Sums(string text, string asset)
    {
        // "<hex>  <filename>" lines.
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim().EndsWith(asset, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }
}
