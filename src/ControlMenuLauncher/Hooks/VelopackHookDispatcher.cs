using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;

namespace ControlMenu.Launcher.Hooks;

public enum HookKind { Install, Updated, Uninstall, Obsolete, Unknown }

public sealed class HookFlag
{
    public HookKind Kind { get; init; }
    public string? RawFlag { get; init; }
    public string? VersionArg { get; init; }
}

/// <summary>
/// Velopack lifecycle hook arg dispatcher. Ported from
/// ws-scrcpy-web/launcher/src/hooks.rs (HEAD 384c6fc).
///
/// Parses --veloapp-* flags BEFORE VelopackApp.Build().Run() and runs the
/// side effect synchronously (or, in Phase 1, logs and returns 0). The
/// catch-all for unknown flags (Gotcha 4) breaks the Update.exe respawn
/// loop that surfaced in v0.1.22 of ws-scrcpy-web.
///
/// Phase 1 deliberate deltas:
/// - HandleInstall: noop+log. data_root creation handled by IDataPathResolver
///   bootstrap (Task 6); ACL grant handled by InstallAcl.EnsureWritable
///   (called from Program.cs); skeleton app-config.json deferred to Phase 3
///   install-as-service.
/// - HandleUpdated: noop+log. Servy CLI invocations land in Phase 3.
/// - HandleUninstall: noop+log. Servy + tray helper cleanup land in Phase 3.
/// - HandleObsolete: identical to upstream (log + return 0; must not block swap).
/// - HandleUnknown: identical to upstream (log at ERROR + return 0; preserves Gotcha 4).
///
/// Migration env deltas:
/// - HookKind.Unknown carries raw flag via HookFlag.RawFlag (separate field)
///   instead of an enum-with-data variant (idiomatic .NET; enums can't carry data).
/// - Option&lt;i32&gt; → int? return on HandleVelopackHook.
/// - Result&lt;()&gt; → exception or noop pattern.
/// </summary>
public static class VelopackHookDispatcher
{
    private const string FlagInstall = "--veloapp-install";
    private const string FlagUpdated = "--veloapp-updated";
    private const string FlagUninstall = "--veloapp-uninstall";
    private const string FlagObsolete = "--veloapp-obsolete";
    private const string FlagPrefix = "--veloapp-";

    /// <summary>
    /// Pure parser. Scans args for a Velopack hook flag. Recognized flags
    /// take precedence over Unknown — we never catch-all over a known flag.
    /// Mirrors hooks.rs::parse_hook_flag.
    /// </summary>
    public static HookFlag? ParseHookFlag(IReadOnlyList<string> args)
    {
        string? unknown = null;
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a)
            {
                case FlagInstall:   return new HookFlag { Kind = HookKind.Install,   RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagUpdated:   return new HookFlag { Kind = HookKind.Updated,   RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagUninstall: return new HookFlag { Kind = HookKind.Uninstall, RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagObsolete:  return new HookFlag { Kind = HookKind.Obsolete,  RawFlag = a, VersionArg = NextOrNull(args, i) };
                default:
                    if (a.StartsWith(FlagPrefix, StringComparison.Ordinal) && unknown is null)
                        unknown = a;
                    break;
            }
        }
        return unknown is null ? null : new HookFlag { Kind = HookKind.Unknown, RawFlag = unknown };
    }

    private static string? NextOrNull(IReadOnlyList<string> args, int i) =>
        (i + 1 < args.Count) ? args[i + 1] : null;

    /// <summary>
    /// Public entry. If argv contains a Velopack hook flag, handle synchronously
    /// and return an exit code. Otherwise return null (caller proceeds to
    /// normal launch). Mirrors hooks.rs::handle_velopack_hook.
    /// </summary>
    public static int? HandleVelopackHook(IReadOnlyList<string> args, IDataPathResolver paths)
    {
        var flag = ParseHookFlag(args);
        if (flag is null) return null;

        LauncherLogger.Info($"hook: dispatching {flag.Kind} (raw: {flag.RawFlag})");

        return flag.Kind switch
        {
            HookKind.Install   => HandleInstall(paths, flag.VersionArg),
            HookKind.Updated   => HandleUpdated(paths, flag.VersionArg),
            HookKind.Uninstall => HandleUninstall(paths, flag.VersionArg),
            HookKind.Obsolete  => HandleObsolete(),
            HookKind.Unknown   => HandleUnknown(flag.RawFlag!),
            _ => 0,
        };
    }

    private static int HandleInstall(IDataPathResolver paths, string? version)
    {
        // Phase 1 delta: noop+log. Upstream writes skeleton config.json + grants
        // ACL on data_root + install_root. CM defers all three:
        //   - data_root creation: handled by IDataPathResolver bootstrap in Program.cs (Task 6).
        //   - install-root ACL grant: handled by InstallAcl.EnsureWritable in launcher's
        //     first-run path (Task 10 wires this).
        //   - skeleton app-config.json: deferred to Phase 3 install-as-service flow.
        LauncherLogger.Info($"hook(install): version={version}; install_root={paths.GetInstallRoot()} (Phase 1: noop)");
        return 0;
    }

    private static int HandleUpdated(IDataPathResolver paths, string? version)
    {
        // Phase 1 delta: noop+log. Upstream calls servy-cli restart in service mode.
        // Servy bundling lands in Phase 3.
        LauncherLogger.Info($"hook(updated): version={version}; install_root={paths.GetInstallRoot()} (Phase 1: noop)");
        return 0;
    }

    private static int HandleUninstall(IDataPathResolver paths, string? version)
    {
        // Phase 1 delta: noop+log. Upstream calls servy-cli stop+uninstall in service mode,
        // clears HKCU Run-key for tray, taskkills tray helper. Servy + tray helper land in Phase 3.
        // User data (config/deps/logs) is preserved by construction here — we don't touch it.
        LauncherLogger.Info($"hook(uninstall): version={version}; install_root={paths.GetInstallRoot()} (Phase 1: noop)");
        return 0;
    }

    private static int HandleObsolete()
    {
        // Velopack invokes the OLD launcher with --veloapp-obsolete <old-version>
        // immediately before swapping current\ to the new version. Hook is a chance
        // to clean up state specific to the old version. Must NOT block the swap.
        // Mirrors hooks.rs::on_obsolete.
        LauncherLogger.Info("hook(obsolete): exiting cleanly so Update.exe can swap current\\");
        return 0;
    }

    private static int HandleUnknown(string rawFlag)
    {
        // Catch-all per Gotcha 4 (feedback_velopack_permachine_lessons.md). Without
        // this, an unknown --veloapp-* flag would fall through to VelopackApp.Run()
        // which silently consumes it and exits, triggering the v0.1.22-style
        // Update.exe respawn loop. Log loudly + exit 0 → Update.exe sees success
        // and stops retrying.
        LauncherLogger.Error(
            $"hook: unknown velopack flag {rawFlag} — exiting 0 to avoid Update.exe respawn loop. Add a handler in VelopackHookDispatcher.cs and ship a fix.");
        return 0;
    }
}
