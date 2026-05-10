using System.Diagnostics;
using System.Runtime.Versioning;
using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;

namespace ControlMenu.Launcher.Supervisor;

/// <summary>
/// Pre-apply daemon hygiene per Velopack PerMachine Gotcha 9
/// (feedback_velopack_permachine_lessons.md). Quiesces daemons that hold
/// file handles under current\ before Velopack apply renames the directory.
///
/// Phase 1: RunAsync() is invoked by ChildSupervisor on exit-75 detection but
/// Phase 1 doesn't actually fire the apply (logs intent + exits). Phase 3
/// will: ChildSupervisor → PreApplyHygiene.RunAsync → UpdateManager.ApplyUpdatesAndExit.
///
/// All errors swallowed — best-effort; the apply attempt proceeds either
/// way. Never throws.
///
/// Sequence (Gotcha 9 verbatim):
///   1. adb kill-server (graceful shutdown of adb daemon).
///   2. taskkill /F /IM adb.exe /T (belt-and-suspenders for any stragglers).
///   3. 250ms settle so the kernel finishes releasing file handles before
///      Velopack probes for current\.
///
/// adb is the only daemon CM currently spawns that holds long-running handles
/// under current\ (scrcpy and sqlite3 are short-lived; go2rtc was relocated by
/// Task 12 to anchor at its binary dir, NOT current\). Future deps that
/// hold long-running handles should be added to this hygiene sequence.
///
/// Note on taskkill.exe: it is an OS builtin (System32) and is PATH-resolved
/// per the CLAUDE.md OS-builtins exception. All other CM binary dependencies
/// must be invoked from the local dependencies folder.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PreApplyHygiene
{
    /// <summary>
    /// Run the hygiene sequence. Returns when complete; never throws.
    /// </summary>
    public static async Task RunAsync(IDataPathResolver paths, CancellationToken ct = default)
    {
        LauncherLogger.Info("pre-apply hygiene: starting");

        // 1. adb kill-server — graceful shutdown of adb daemon.
        var adbPath = Path.Combine(paths.GetDependenciesDir(), "platform-tools", "adb.exe");
        if (File.Exists(adbPath))
        {
            await TryRunAsync(adbPath, "kill-server", ct);
        }
        else
        {
            LauncherLogger.Info($"pre-apply hygiene: adb not at expected path {adbPath}; skipping kill-server");
        }

        // 2. Belt-and-suspenders: taskkill any remaining adb processes.
        //    taskkill.exe is an OS builtin per CLAUDE.md exception; PATH-resolved is acceptable.
        await TryRunAsync("taskkill.exe", "/F /IM adb.exe /T", ct);

        // 3. 250ms settle so the kernel finishes releasing handles before
        //    Velopack probes for current\.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
        catch (OperationCanceledException)
        {
            LauncherLogger.Info("pre-apply hygiene: settle delay cancelled");
        }

        LauncherLogger.Info("pre-apply hygiene: done");
    }

    /// <summary>
    /// Spawn a process and wait for exit. All errors swallowed and logged.
    /// Best-effort by design — hygiene must not block the apply attempt.
    /// </summary>
    private static async Task TryRunAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                LauncherLogger.Error($"pre-apply hygiene: Process.Start returned null for {exe} {args}");
                return;
            }

            await p.WaitForExitAsync(ct);
            LauncherLogger.Info($"pre-apply hygiene: {exe} {args} -> exit {p.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            LauncherLogger.Info($"pre-apply hygiene: {exe} {args} cancelled");
        }
        catch (Exception ex)
        {
            LauncherLogger.Info($"pre-apply hygiene: {exe} {args} threw {ex.GetType().Name}: {ex.Message} (continuing)");
        }
    }
}
