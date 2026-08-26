using System.Diagnostics;
using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;

namespace ControlMenu.Launcher.Supervisor;

/// <summary>
/// Phase 1 stub. Spawns ControlMenu.exe child, waits for exit, dispatches
/// on exit-code 75 (Velopack apply requested by ControlMenu.exe via
/// VelopackUpdateService.RequestApplyUpdate).
///
/// Phase 3 expands this into a crash-restart loop with full Velopack apply
/// orchestration (mirrors launcher/src/supervisor.rs:1-198 from
/// ws-scrcpy-web HEAD 384c6fc).
///
/// Phase 3 will: exit-75 → PreApplyHygiene.RunAsync → UpdateManager.ApplyUpdatesAndExit
/// → Servy restart-delay handles relaunch.
/// </summary>
public static class ChildSupervisor
{
    /// <summary>
    /// Special exit code: ControlMenu.exe asks the launcher to apply a
    /// Velopack update. Mirrors VelopackUpdateService.RequestApplyUpdate's
    /// Environment.ExitCode = 75. Keep this constant in sync between
    /// ControlMenu.Services.Update.VelopackUpdateService and this class.
    /// </summary>
    public const int ExitCodeApplyUpdate = 75;

    public static int Run(IDataPathResolver paths, JobObject? job = null)
    {
        var childExe = Path.Combine(paths.GetCurrentDir(), "ControlMenu.exe");
        if (!File.Exists(childExe))
        {
            LauncherLogger.Error($"child not found: {childExe}");
            return 1;
        }

        LauncherLogger.Info($"spawning child: {childExe}");
        var psi = new ProcessStartInfo
        {
            FileName = childExe,
            WorkingDirectory = paths.GetCurrentDir(),
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        try
        {
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");

            // Put the child — and, by job-membership inheritance, its grandchildren
            // (go2rtc/adb/scrcpy) — into the launcher's kill-on-close job so nothing
            // orphans if the launcher is killed. Adopt immediately after spawn, before
            // the child has a chance to spawn its own descendants. Mirrors
            // ws-scrcpy-web supervisor.rs job_object::adopt(&child).
            if (job is not null && OperatingSystem.IsWindows() && job.Adopt(p))
            {
                LauncherLogger.Info($"job_object: child PID {p.Id} adopted into kill-on-close job");
            }

            LauncherLogger.Info($"child PID: {p.Id}");
            p.WaitForExit();
            var code = p.ExitCode;
            LauncherLogger.Info($"child exited with code {code}");

            if (code == ExitCodeApplyUpdate)
            {
                LauncherLogger.Info("child requested apply-update via exit-75; running pre-apply hygiene");
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        // Blocking on purpose. The supervisor is synchronous by design (it is
                        // built around Process.WaitForExit) and this is a console host with no
                        // SynchronizationContext, so there is no context to deadlock against.
                        // Making the whole WaitForExit chain async to avoid one blocking call
                        // here would be a large change for no behavioural gain.
                        PreApplyHygiene.RunAsync(paths).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        // Defensive: PreApplyHygiene swallows internally, but if its public surface
                        // throws unexpectedly we log + continue rather than fail the launcher.
                        LauncherLogger.Error($"pre-apply hygiene threw: {ex}");
                    }
                }
                LauncherLogger.Info(
                    "Phase 1: pre-apply hygiene complete; Velopack apply orchestration lands in Phase 3. " +
                    "Launcher will exit with code 75; user must manually relaunch via Start Menu.");
            }
            return code;
        }
        catch (Exception ex)
        {
            LauncherLogger.Error($"supervisor failed: {ex}");
            return 1;
        }
    }
}
