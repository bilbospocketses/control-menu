namespace ControlMenu.Services.Verification;

public static class TransportGuard
{
    public static bool IsAllowedFinalUri(Uri finalUri, string[] allowedHosts)
    {
        if (finalUri.Scheme != Uri.UriSchemeHttps) return false;
        if (allowedHosts.Length == 0) return false;
        var host = finalUri.Host;
        foreach (var allowed in allowedHosts)
        {
            if (allowed.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = allowed[1..]; // ".githubusercontent.com"
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
