using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using ControlMenu.Common.Logging;

namespace ControlMenu.Launcher;

/// <summary>
/// Install-root ACL grant for the running user (Windows-only). Ported from
/// ws-scrcpy-web/launcher/src/install_acl.rs (HEAD 384c6fc).
///
/// Background: Velopack's PerMachine install model only works if the install
/// root has Authenticated Users:Modify. Without it, Velopack's writability
/// self-test fails, falls back to LocalAppData for state, and the elevated
/// Update.exe re-launch silently dies during the swap step. The ACL grant
/// during MSI install gets stripped by component-permission processing —
/// granting at first non-hook launcher start is what survives.
///
/// Migration deltas vs. upstream:
/// - Sentinel filename: .controlmenu-write-test (CM equivalent of upstream's
///   .ws-scrcpy-write-test).
/// - icacls invocation via Process.Start with Verb="runas" instead of
///   ShellExecuteExW direct call. .NET's Process+UseShellExecute=true wraps
///   ShellExecuteExW under the hood; equivalent UAC behavior.
/// - Wait timeout: 30s instead of upstream's INFINITE. Defensive — if icacls
///   wedges, the launcher should not hang indefinitely. Accepts the trade-off
///   that an icacls hang past 30s leaves the install-root grant unverified;
///   in-app updater would degrade in that edge case but the launcher itself
///   continues normally.
/// - UAC dismissal handling: catches Win32Exception with NativeErrorCode
///   ERROR_CANCELLED (1223 / 0x4C7) and degrades gracefully (logs + returns),
///   matching the upstream caller's "log + continue" pattern but internalizing
///   it instead of propagating Err.
///   NOTE: NativeErrorCode is the raw Win32 error code 1223 (0x4C7), NOT the
///   COM HRESULT 0x800704C7. Plan comment cited HRESULT form; corrected here.
/// - Result&lt;()&gt; → void; failures logged via LauncherLogger.Error.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallAcl
{
    private const string SentinelPrefix = ".controlmenu-write-test-";
    private const string AclSidAuthUsers = "*S-1-5-11";

    // Win32 ERROR_CANCELLED = 1223 (0x4C7). Win32Exception.NativeErrorCode
    // returns the raw Win32 code — NOT the COM HRESULT form (0x800704C7).
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Test whether the running user can write to <paramref name="path"/> by creating then deleting
    /// a sentinel file. The sentinel name is unique per call (a fresh GUID), so concurrent launcher
    /// self-tests — multiple instances can run at once — and Velopack's own probe never delete each
    /// other's sentinel mid-flight. (A fixed name made the probe race-prone / DoS-able: an external
    /// watcher or a second instance could remove the file between the write and the delete, or the
    /// delete could hit a foreign sentinel.) The file is removed in a finally, so a write that
    /// succeeds but can't be deleted leaves nothing behind.
    /// </summary>
    public static bool IsWritable(string path)
    {
        var testPath = Path.Combine(path, SentinelPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(testPath, []);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(testPath)) File.Delete(testPath); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensure the install root has Authenticated Users:Modify (OI)(CI). If
    /// already writable, returns without prompting. Otherwise invokes
    /// icacls.exe via runas-elevated Process.Start; UAC prompt fires.
    /// Failure (UAC dismissed, no admin available) is logged and swallowed —
    /// the app works without this grant; only the in-app updater is degraded.
    /// </summary>
    public static void EnsureWritable(string installRoot)
    {
        if (IsWritable(installRoot))
        {
            LauncherLogger.Info($"install-root already writable: {installRoot}");
            return;
        }

        LauncherLogger.Info(
            "install-root not writable to current user; " +
            "requesting elevation to grant Authenticated Users:Modify (one-time UAC)");

        var exitCode = RunIcaclsElevated(installRoot);
        if (exitCode is null)
        {
            // Already logged; the failure was UAC dismissal or spawn failure.
            return;
        }
        if (exitCode != 0)
        {
            LauncherLogger.Error(
                $"elevated icacls exited with code {exitCode}; install-root may still lack Authenticated Users:Modify");
            return;
        }

        if (!IsWritable(installRoot))
        {
            LauncherLogger.Error(
                "icacls reported success but install-root still not writable to running user");
            return;
        }

        LauncherLogger.Info(
            $"install-root grant applied on {installRoot}; in-app updater should now function");
    }

    /// <returns>Exit code if icacls ran (0 = success); null if launch failed (UAC dismissed, etc.).</returns>
    private static int? RunIcaclsElevated(string installRoot)
    {
        // Args format MUST match upstream byte-for-byte (install_acl.rs:99-102):
        //   "<install_root>" /grant *S-1-5-11:(OI)(CI)M /T /C /Q
        // (OI)(CI)M = object + container inheritance + Modify perm
        // /T = recurse to existing children
        // /C = continue on per-file errors
        // /Q = suppress success chatter
        var args = $"\"{installRoot}\" /grant {AclSidAuthUsers}:(OI)(CI)M /T /C /Q";

        // icacls.exe is a Windows OS-builtin (CLAUDE.md PATH exception). It operates on
        // the absolute path in `args` and does not depend on working directory, so no
        // WorkingDirectory override is needed here (Gotcha 9 does not apply).
        var psi = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = args,
            Verb = "runas",          // upstream's lpVerb="runas" — triggers UAC
            UseShellExecute = true,  // required for Verb=runas
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,  // upstream's nShow=SW_HIDE (0)
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                LauncherLogger.Error("icacls.exe Process.Start returned null");
                return null;
            }

            // Delta vs. upstream: 30s timeout instead of INFINITE (WaitForSingleObject).
            // Defensive — if icacls wedges, the launcher should not hang indefinitely.
            var exited = p.WaitForExit(TimeSpan.FromSeconds(30));
            if (!exited)
            {
                LauncherLogger.Error(
                    "icacls.exe did not exit within 30s; abandoning (in-app updater will be degraded)");
                return null;
            }

            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // UAC prompt dismissed by user. NativeErrorCode == 1223 (ERROR_CANCELLED, 0x4C7).
            // Degrade gracefully — app continues; in-app updater will be degraded until
            // the user re-grants (they'll get a new UAC prompt on the next launcher start
            // where the install root is still not writable).
            LauncherLogger.Error(
                "UAC prompt dismissed; install-root grant skipped (in-app updater will be degraded until user re-grants)");
            return null;
        }
        catch (Exception ex)
        {
            LauncherLogger.Error($"install-root ACL grant failed: {ex.Message}");
            return null;
        }
    }
}
