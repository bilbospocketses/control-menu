namespace ControlMenu.Common.Config;

/// <summary>
/// Writable-state root resolution for the ControlMenu data directory.
/// Ported from ws-scrcpy-web/common/src/config.rs (HEAD 384c6fc) —
/// <c>data_root_for_windows</c> and <c>data_root_from_env</c>.
///
/// CM-specific deltas vs. upstream:
/// <list type="bullet">
///   <item><description>
///     <b>Delta 1 — app name:</b> Returns <c>&lt;programdata&gt;\ControlMenu</c>
///     rather than upstream's <c>&lt;programdata&gt;\WsScrcpyWeb</c>. This is
///     CM's data directory by construction.
///   </description></item>
///   <item><description>
///     <b>Env delta — return types:</b> Methods return <c>string</c> and
///     <c>string?</c> rather than <c>PathBuf</c> and <c>Option&lt;PathBuf&gt;</c>.
///     .NET path APIs operate on strings; callers can wrap in Path APIs as needed.
///   </description></item>
///   <item><description>
///     <b>Env delta — OS guard:</b> <see cref="DataRootFromEnv"/> uses a
///     runtime <see cref="OperatingSystem.IsWindows()"/> check rather than
///     Rust's compile-time <c>#[cfg(windows)]</c>. Behavior is identical:
///     returns null on non-Windows.
///   </description></item>
/// </list>
/// </summary>
public static class AppConfigPaths
{
    /// <summary>
    /// CM app name used as the subdirectory under ProgramData.
    /// Delta 1 from upstream (upstream uses "WsScrcpyWeb").
    /// </summary>
    private const string AppDataDirName = "ControlMenu";

    /// <summary>
    /// Fallback ProgramData path used when the environment variable is absent
    /// or empty. Mirrors upstream's hard-coded fallback <c>C:\ProgramData</c>.
    /// </summary>
    private const string DefaultProgramData = @"C:\ProgramData";

    /// <summary>
    /// Returns the writable-state root: <c>&lt;programData&gt;\ControlMenu</c>.
    /// Falls back to <c>C:\ProgramData\ControlMenu</c> when
    /// <paramref name="programData"/> is null or empty.
    ///
    /// Mirrors <c>config.rs::data_root_for_windows</c>. Accepts <c>null</c>
    /// (mirrors Rust's <c>Option::None</c>) and empty string (mirrors Rust's
    /// <c>.filter(|s| !s.is_empty())</c>) identically — both fall back to the
    /// default path.
    /// </summary>
    public static string DataRootForWindows(string? programData)
    {
        var pd = string.IsNullOrEmpty(programData) ? DefaultProgramData : programData;
        return Path.Combine(pd, AppDataDirName);
    }

    /// <summary>
    /// Convenience wrapper that reads <c>PROGRAMDATA</c> from the process
    /// environment and delegates to <see cref="DataRootForWindows"/>.
    ///
    /// Returns <c>null</c> on non-Windows (mirrors upstream's <c>None</c>
    /// branch — non-Windows callers should fall back to install-root for
    /// data-root semantics until a Linux migration target is defined).
    ///
    /// Mirrors <c>config.rs::data_root_from_env</c>.
    /// </summary>
    public static string? DataRootFromEnv()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var pd = Environment.GetEnvironmentVariable("PROGRAMDATA");
        return DataRootForWindows(pd);
    }
}
