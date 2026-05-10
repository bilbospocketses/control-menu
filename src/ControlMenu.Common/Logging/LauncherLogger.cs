using ControlMenu.Common.Config;

namespace ControlMenu.Common.Logging;

/// <summary>
/// File logger for the Velopack launcher and tray helper. Ported from
/// ws-scrcpy-web/launcher/src/log.rs (HEAD 384c6fc).
///
/// Shape mirrors upstream's stateless design:
///   - NO Init/cached writer/rotation. Every Info/Error call resolves the path,
///     opens-appends-closes, AND writes to Console.Error.
///   - Path resolution per-call via LogPath() helper (private). Probes
///     <c>AppConfigPaths.DataRootFromEnv() + "/logs/launcher.log"</c> first;
///     falls back to <c>&lt;exeDir&gt;/launcher.log</c> if data_root unavailable.
///   - Best-effort everything: failed mkdir falls through; failed open silently
///     swallowed; logger never throws.
///   - Line format: "yyyy-MM-dd HH:mm:ss.fff [LEVEL] message" (LEVEL = INFO/ERROR,
///     no padding so [INFO] and [ERROR] differ in width — matches upstream).
///
/// CM-specific deltas vs. upstream:
///   - DateTimeOffset.UtcNow.ToString instead of upstream's hand-rolled civil_from_days
///     (env: .NET has the formatter built in; Rust avoids dragging in chrono).
///   - Console.Error.WriteLine instead of upstream's eprintln! (env: .NET equivalent).
///   - PathBuf → string (env).
/// </summary>
public static class LauncherLogger
{
    public static void Info(string msg) => Append("INFO", msg);
    public static void Error(string msg) => Append("ERROR", msg);

    private static void Append(string level, string msg)
    {
        var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"{ts} [{level}] {msg}";

        var path = LogPath();
        if (path is not null)
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Best-effort: failed file write must not propagate (matches upstream).
            }
        }

        // Always also write to stderr — matches upstream's eprintln!.
        try { Console.Error.WriteLine(line); } catch { /* best-effort */ }
    }

    private static string? LogPath()
    {
        // Primary: <dataRoot>/logs/launcher.log (Windows; PROGRAMDATA-rooted via AppConfigPaths).
        var dataRoot = AppConfigPaths.DataRootFromEnv();
        if (dataRoot is not null)
        {
            var logsDir = Path.Combine(dataRoot, "logs");
            try
            {
                Directory.CreateDirectory(logsDir);
                if (Directory.Exists(logsDir))
                    return Path.Combine(logsDir, "launcher.log");
            }
            catch
            {
                // Fall through to exe_dir fallback.
            }
        }

        // Fallback: <exeDir>/launcher.log (dev convenience; matches upstream).
        try
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is null) return null;
            var dir = Path.GetDirectoryName(exe);
            if (dir is null) return null;
            return Path.Combine(dir, "launcher.log");
        }
        catch
        {
            return null;
        }
    }
}
