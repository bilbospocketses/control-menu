using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlMenu.Common.Config;

/// <summary>
/// Read-only view of <c>app-config.json</c>. Ported from
/// ws-scrcpy-web/common/src/config.rs (HEAD 384c6fc).
///
/// CM-specific deltas vs. upstream (documented per delta table):
/// <list type="bullet">
///   <item><description>
///     <b>Delta 3 — field set:</b> Mirrors upstream's 3 fields verbatim:
///     installMode, firstRunComplete, webPort. The CM spec adds
///     version/serviceName/trayHelperPath for the install-as-service flow,
///     but those are written by Phase 3 work; Phase 1 mirrors upstream's
///     read-only DTO shape.
///   </description></item>
///   <item><description>
///     <b>Delta 4 — no write API:</b> Read-only in Phase 1. No Save() method.
///     Writers ship in Phase 3 with the install-as-service flow.
///   </description></item>
///   <item><description>
///     <b>Env delta — error model:</b> FileNotFoundException (missing file) and
///     JsonException (malformed JSON) instead of upstream's ConfigError enum
///     (ConfigError::Missing, ConfigError::Io, ConfigError::Json). The lenient
///     Load() method catches all exceptions. The strict LoadStrict() method
///     propagates IOException for I/O failures (e.g., permission denied) as a
///     native .NET exception — this covers upstream's ConfigError::Io variant.
///   </description></item>
///   <item><description>
///     <b>Env delta — install-root wrappers omitted:</b> Upstream exposes both
///     load_from(path) and load(install_root) convenience overloads where
///     load(install_root) appends "config.json" automatically. This port
///     exposes only the direct-path form (Load(path) / LoadStrict(path)).
///     Phase 1 callers compose Path.Combine(installRoot, "app-config.json")
///     externally; the root-path sugar is deferred.
///   </description></item>
///   <item><description>
///     <b>Env delta — WebPort type:</b> int? instead of Option&lt;u16&gt;.
///     Port values fit comfortably in int; ushort? has no idiomatic precedent
///     in .NET port-handling APIs.
///   </description></item>
/// </list>
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("installMode")]
    public string? InstallMode { get; init; }

    [JsonPropertyName("firstRunComplete")]
    public bool FirstRunComplete { get; init; }

    [JsonPropertyName("webPort")]
    public int? WebPort { get; init; }

    /// <summary>
    /// Returns true when <see cref="InstallMode"/> equals <c>"service"</c>
    /// (case-sensitive, ordinal comparison).
    ///
    /// <b>CM-spec Delta 2 from upstream:</b> CM's installMode vocabulary is
    /// <c>"user" | "service"</c> (two values). Upstream ws-scrcpy-web uses a
    /// different vocabulary — <c>"user-service"</c> and <c>"system-service"</c>
    /// — and checks via ends_with("-service"). Mirroring that check here would
    /// fail to recognize CM's actual stored value <c>"service"</c>, and would
    /// incorrectly recognize upstream-style <c>"user-service"</c> as service
    /// mode if one appeared in a CM config.json. Exact-match is the correct
    /// check for CM's spec.
    /// </summary>
    public bool IsServiceMode() =>
        InstallMode is not null && InstallMode.Equals("service", StringComparison.Ordinal);

    /// <summary>
    /// Lenient load from <paramref name="path"/>. Missing or malformed file
    /// returns <c>new AppConfig()</c> (all-default). Never throws or logs.
    /// Mirrors <c>common/src/config.rs::AppConfig::load_from</c>.
    ///
    /// Callers that need feedback on the fallback path should call
    /// <see cref="LoadStrict"/> and handle exceptions themselves.
    /// </summary>
    public static AppConfig Load(string path)
    {
        try
        {
            return LoadStrict(path);
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Strict load from <paramref name="path"/>. Throws on any failure:
    /// <list type="bullet">
    ///   <item><description><see cref="FileNotFoundException"/> — file absent (mirrors ConfigError::Missing)</description></item>
    ///   <item><description><see cref="JsonException"/> — JSON parse failure (mirrors ConfigError::Json)</description></item>
    ///   <item><description><see cref="IOException"/> — I/O read failure, e.g. permission denied (mirrors ConfigError::Io)</description></item>
    /// </list>
    /// Mirrors <c>common/src/config.rs::AppConfig::load_strict_from</c>.
    ///
    /// <b>Env delta from upstream:</b> uses native .NET exception types instead
    /// of upstream's ConfigError enum.
    /// </summary>
    public static AppConfig LoadStrict(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"AppConfig file not found: {path}", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json)
            ?? throw new JsonException($"AppConfig deserialized to null: {path}");
    }
}
