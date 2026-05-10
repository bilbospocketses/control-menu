using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;

namespace ControlMenu.Launcher;

/// <summary>
/// Single-instance guard for ControlMenuLauncher. Ported from
/// ws-scrcpy-web/launcher/src/single_instance.rs (HEAD 384c6fc).
///
/// Two mutex names — Local\ControlMenu-SingleInstance-User and
/// Local\ControlMenu-SingleInstance-Admin — let one non-elevated and
/// one elevated launcher coexist (legitimate workflow: admin instance
/// for service install/uninstall while normal instance keeps running).
/// Same-integrity duplicates are blocked because both contenders try the
/// same suffixed name.
///
/// Migration deltas vs upstream:
/// - Mutex base name: ControlMenu-SingleInstance (CM app name) vs upstream
///   WsScrcpyWeb-SingleInstance. Same hyphen-separator convention.
/// - Wraps System.Threading.Mutex (which wraps CreateMutexW). The named-mutex
///   semantics with Local\ prefix are honored by the .NET ctor.
/// - Elevation detection uses WindowsPrincipal.IsInRole(Administrator) instead
///   of OpenProcessToken + GetTokenInformation(TokenElevation). Near-equivalent
///   under default conditions; UAC-aware nuances may produce edge cases — log
///   if elevation detection ever surprises.
/// - Result&lt;Option&lt;Guard&gt;&gt; → null-on-collision return shape (Acquire returns
///   null when the mutex already exists, otherwise a disposable handle).
/// - AbandonedMutexException treated as "we win" (matches upstream's Windows
///   crashed-owner behavior).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexBase = @"Local\ControlMenu-SingleInstance";

    private readonly Mutex _mutex;
    private readonly bool _ownsLock;

    private SingleInstance(Mutex mutex, bool ownsLock)
    {
        _mutex = mutex;
        _ownsLock = ownsLock;
    }

    /// <summary>
    /// Build the canonical mutex name for THIS process's elevation level.
    /// Mirrors single_instance.rs::current_mutex_name (line 169-172).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string CurrentMutexName()
    {
        var suffix = IsElevated() ? "Admin" : "User";
        return $"{MutexBase}-{suffix}";
    }

    /// <summary>
    /// Try to acquire the named mutex. Returns a handle on success, or
    /// <c>null</c> if another instance already holds the mutex (caller
    /// should exit cleanly with code 0).
    /// Mirrors single_instance.rs::acquire (line 111-132).
    ///
    /// Implementation note: the Mutex ctor's createdNew parameter maps
    /// directly to CreateMutexW + GetLastError() == ERROR_ALREADY_EXISTS
    /// (upstream's collision-detection mechanism). If createdNew is false,
    /// the mutex name pre-existed in the kernel namespace — another instance
    /// owns it. We do NOT use WaitOne for collision detection because
    /// System.Threading.Mutex is re-entrant on Windows: a thread that already
    /// owns the mutex can WaitOne again without blocking, which would cause
    /// the same-process "second call returns null" test to see a false success.
    /// We still call WaitOne(zero) to take ownership when createdNew is true,
    /// and handle AbandonedMutexException for the crashed-prior-owner case.
    /// </summary>
    public static SingleInstance? Acquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name: name, createdNew: out var createdNew);

        // createdNew == false means CreateMutexW found ERROR_ALREADY_EXISTS:
        // another instance already owns this mutex name. Return null so the
        // caller exits cleanly with code 0 — matches upstream Ok(None).
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        // We created the mutex (createdNew == true) but don't own it yet
        // (initiallyOwned: false). Acquire ownership now.
        bool ownsLock;
        try
        {
            ownsLock = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed without releasing. We "win" the mutex —
            // matches upstream's Windows crashed-owner behavior.
            ownsLock = true;
        }

        if (!ownsLock)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, ownsLock);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_ownsLock) _mutex.ReleaseMutex();
        }
        catch
        {
            // Process is exiting; swallow.
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
