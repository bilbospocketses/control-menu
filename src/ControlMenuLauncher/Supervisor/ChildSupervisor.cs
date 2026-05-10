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
/// Note for the Phase 3 port: the apply-update flow needs pre-apply daemon
/// hygiene (Task 13's PreApplyHygiene helper) BEFORE Velopack's apply call,
/// to prevent file-handle conflicts during current\ rename.
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

    public static int Run(IDataPathResolver paths)
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
            LauncherLogger.Info($"child PID: {p.Id}");
            p.WaitForExit();
            var code = p.ExitCode;
            LauncherLogger.Info($"child exited with code {code}");

            if (code == ExitCodeApplyUpdate)
            {
                LauncherLogger.Info(
                    "child requested apply-update via exit-75; Phase 1: log + exit. " +
                    "Phase 3 will run pre-apply hygiene + invoke Velopack apply orchestration here.");
                // Phase 1 ends here. Phase 3 will: invoke PreApplyHygiene.RunAsync
                // (Task 13) → invoke UpdateManager.ApplyUpdatesAndExit → Servy
                // restart-delay handles relaunch.
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
