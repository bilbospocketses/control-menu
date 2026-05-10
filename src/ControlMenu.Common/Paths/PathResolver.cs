// Install-root derivation, ported from ws-scrcpy-web/launcher/src/paths.rs (HEAD 384c6fc).
//
// CM-specific deltas vs. upstream Paths struct:
//   - Upstream Paths struct has 5 fields (install_root, data_root, deps_path,
//     restart_marker, old_node). CM ports only install-root derivation here;
//     data_root + deps_path live on IDataPathResolver (Task 5); restart_marker
//     + old_node are ws-scrcpy-web-specific and have NO CM equivalent.
//   - Upstream `from_env()` reads PROGRAMDATA env var. CM reads PROGRAMDATA in
//     AppConfigPaths.DataRootFromEnv (Task 3); data_root derivation is entirely
//     IDataPathResolver's concern, not PathResolver's.
//   - Upstream `from_env()` reads DEPS_PATH env var. CM does not expose a
//     deps-path override env var; there is no CM equivalent of DEPS_PATH.
//   - Upstream uses anyhow::Result<T>; CM throws InvalidOperationException on
//     derivation failure (idiomatic .NET; not a paraphrase divergence).
//   - Upstream uses std::env::current_exe(); CM uses
//     Process.GetCurrentProcess().MainModule!.FileName (env delta).
//   - GetCurrentDir() is a CM addition with no upstream equivalent. It
//     codifies the Velopack `current/` subdirectory layout documented in the
//     paths.rs header comment (production layout: <installRoot>/current/).
//
// Identifier naming: upstream Rust uses snake_case (exe_dir, install_root);
// C# locals use camelCase (exeDir, installRoot). Error message text is
// preserved semantically; "exe_dir" → "exeDir" in messages is a naming-
// convention delta, not a paraphrase bug.

using System.Diagnostics;

namespace ControlMenu.Common.Paths;

public static class PathResolver
{
    public static string DeriveInstallRoot(string exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            throw new ArgumentException("exePath must be non-empty", nameof(exePath));

        var exeDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException("exe has no parent dir");
        var installRoot = Path.GetDirectoryName(exeDir)
            ?? throw new InvalidOperationException("exeDir has no parent (cannot derive install_root)");

        return installRoot;
    }

    public static string DeriveInstallRootFromCurrentProcess()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("could not determine current process exe path");
        return DeriveInstallRoot(exe);
    }

    public static string GetCurrentDir(string installRoot) => Path.Combine(installRoot, "current");
}
