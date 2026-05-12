namespace ControlMenu.Common.Config;

/// <summary>
/// Resolves the Kestrel binding URL from <see cref="AppConfig"/>.
///
/// Default port 5159 mirrors <c>Properties/launchSettings.json</c> (dev) and
/// every reference in the docs. launchSettings.json is dev-only — published
/// builds must wire the port explicitly, which is what this helper exists for.
/// </summary>
public static class WebPortResolver
{
    public const int DefaultPort = 5159;

    public static string GetKestrelUrl(AppConfig config)
    {
        var port = config.WebPort is int p && p > 0 && p < 65536 ? p : DefaultPort;
        return $"http://localhost:{port}";
    }
}
