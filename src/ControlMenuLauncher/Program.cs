using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;
using ControlMenu.Common.Seeding;
using ControlMenu.Launcher.Hooks;
using ControlMenu.Launcher.Supervisor;

namespace ControlMenu.Launcher;

internal static class Program
{
    private const string Version = "1.1.0";

    private static int Main(string[] args)
    {
        // Composition root: derive paths first so the logger has somewhere to land.
        IDataPathResolver paths;
        try
        {
            paths = DataPathResolverFactory.CreateFromCurrentProcess();
            Directory.CreateDirectory(paths.GetLogsDir());
        }
        catch (Exception ex)
        {
            // Last-resort: write to %TEMP% so we have a paper trail before exiting.
            // main.rs has a similar fallback for resolve_install_root failures.
            var fallbackLog = Path.Combine(Path.GetTempPath(), "ControlMenuLauncher-bootstrap-failure.log");
            try
            {
                File.AppendAllText(fallbackLog, $"{DateTime.UtcNow:o} bootstrap failed: {ex}\n");
            }
            catch
            {
                // Best-effort: if %TEMP% is also unwritable, swallow silently.
            }
            return 1;
        }

        // Phase 3 marker: --print-active-session shortcut goes here, BEFORE
        // LauncherLogger.Info/Info, so service polls don't generate launcher-start
        // log noise. (main.rs:18-43)

        LauncherLogger.Info($"ControlMenuLauncher v{Version} starting");
        LauncherLogger.Info($"argv: [{string.Join(", ", args.Select(a => $"\"{a}\""))}]");

        // Phase 3 marker: elevated_runner::handle(args) dispatch goes here,
        // BEFORE Velopack hook dispatch. (main.rs:58-66)

        // 1. Velopack lifecycle hook dispatch — BEFORE VelopackApp.Build().Run().
        //    Mirrors main.rs:68-75. Catches unknown --veloapp-* flags here too
        //    (Gotcha 4) so VelopackApp.Run() never silently consumes them.
        var hookExit = VelopackHookDispatcher.HandleVelopackHook(args, paths);
        if (hookExit is int hookCode)
        {
            LauncherLogger.Info($"hook handler exiting with code {hookCode}");
            return hookCode;
        }

        // 2. Install-root ACL grant via runas-elevated icacls.
        //    Mirrors main.rs:77-108. Velopack PerMachine Gotchas 2 + 3.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                InstallAcl.EnsureWritable(paths.GetInstallRoot());
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"install-root ACL grant top-level failure (in-app updater degraded): {ex.Message}");
            }
        }

        // 3. Single-instance guard. Acquired AFTER hook dispatch (hooks are
        //    short-lived single-shots that legitimately race with a running
        //    instance). Mirrors main.rs:110-133.
        var mutexName = SingleInstance.CurrentMutexName();
        var instance = SingleInstance.Acquire(mutexName);
        if (instance is null)
        {
            LauncherLogger.Info("another ControlMenuLauncher instance is already running; exiting");
            return 0;
        }

        // Hydrate pre-seeded dependencies from current\seed\dependencies\ into
        // <dataRoot>\dependencies\ (parity with ws-scrcpy-web's seed/node\
        // bootstrap). Idempotent — already-present leaves are preserved so
        // user-updated deps survive a Velopack swap of the seed bundle.
        try
        {
            var hydrate = SeedHydrator.Hydrate(paths.GetCurrentDir(), paths.GetDependenciesDir());
            if (hydrate.Copied > 0 || hydrate.Skipped > 0 || hydrate.Pruned > 0)
            {
                LauncherLogger.Info($"seed hydrate: copied={hydrate.Copied} skipped={hydrate.Skipped} pruned={hydrate.Pruned}");
            }
        }
        catch (Exception ex)
        {
            LauncherLogger.Error($"seed hydrate failed (continuing — wizard may fail if deps missing): {ex.Message}");
        }

        // Phase 3 marker: --local-takeover override (post-uninstall handoff)
        // sets is_service_mode = false here. (main.rs:183-194)

        // Out of scope: job_object::release() (main.rs:206-220) — CM does not
        // adopt the Job Object pattern (no Node child).

        try
        {
            // 4. VelopackApp init. MUST be the first executable code path on
            //    the normal-launch branch per SP3 P2 Contract 5 + Gotcha 1
            //    (auto-apply default true → infinite Update.exe re-fire loop
            //    after a successful apply, because the .nupkg stays in
            //    packages\). Mirror main.rs:135-156.
            //
            //    .NET API: VelopackApp.Build().SetAutoApplyOnStartup(false).Run()
            //    Run() takes NO arguments — the .NET SDK reads argv automatically
            //    via Environment.GetCommandLineArgs(). Context7 confirmed:
            //    `public void Run()` (Velopack 0.0.1298). The Rust `.run()` is
            //    equivalent. Use .SetArgs(args) only if argv override is needed.
            Velopack.VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .Run();

            // Phase 2 marker: tray::spawn(is_service_mode) goes here, BEFORE
            // supervisor::run blocking loop. (main.rs:158-196)

            // 5. Phase 1 supervisor: directly spawn ControlMenu.exe child.
            //    Phase 3 expands with crash-restart loop and Velopack apply
            //    orchestration. Mirrors main.rs:198-204 stub portion.
            return ChildSupervisor.Run(paths);
        }
        finally
        {
            instance.Dispose();
            LauncherLogger.Info("ControlMenuLauncher exiting");
        }
    }
}
