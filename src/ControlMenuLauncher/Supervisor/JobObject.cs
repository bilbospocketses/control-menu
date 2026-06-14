using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlMenu.Common.Logging;

namespace ControlMenu.Launcher.Supervisor;

/// <summary>
/// Windows Job Object that owns the supervised ControlMenu.exe child and its
/// descendants (go2rtc, adb, scrcpy, magick, …). Ported from ws-scrcpy-web
/// launcher/src/job_object.rs (HEAD 384c6fc).
///
/// Without it, killing the launcher (Servy stop, Task Manager, MSI uninstall)
/// can leave the child + grandchildren resident. The job is created with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE and the launcher holds the only handle
/// for its lifetime: when the launcher exits — graceful OR killed — the OS
/// closes the last handle, the job destructs, and Windows terminates every
/// process in it. Job membership inherits, so grandchildren are captured too.
///
/// Graceful exit is the exception: <see cref="ReleaseKillOnClose"/> clears the
/// flag before the launcher exits so Velopack's Update.exe grandchild (spawned
/// to swap the package AFTER the launcher exits) is not terminated mid-extract.
/// Hard-kill paths skip that call, so the kernel's kill-on-close stays the
/// safety net for them.
///
/// Lifetime: hold the instance for the launcher's whole run (Program owns it,
/// ChildSupervisor adopts the child into it). On the graceful path call
/// <see cref="ReleaseKillOnClose"/> then dispose — the job dissolves harmlessly.
/// On abnormal termination the OS closes the handle and kill-on-close fires.
/// Construction/operation failures are logged and surfaced as null/false; the
/// launcher keeps running (graceful degradation) rather than refusing to start.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class JobObject : IDisposable
{
    // JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private IntPtr _handle;

    private JobObject(IntPtr handle) => _handle = handle;

    /// <summary>
    /// Create a process-wide Job Object with KILL_ON_JOB_CLOSE. Returns null
    /// (logged) if the kernel calls fail — the caller continues without a job.
    /// </summary>
    public static JobObject? CreateKillOnClose()
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            LauncherLogger.Error($"job_object: CreateJobObjectW failed (win32 {Marshal.GetLastWin32Error()})");
            return null;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info,
                Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            LauncherLogger.Error($"job_object: SetInformationJobObject(kill-on-close) failed (win32 {Marshal.GetLastWin32Error()})");
            CloseHandle(handle);
            return null;
        }

        return new JobObject(handle);
    }

    /// <summary>
    /// Assign <paramref name="child"/> to this kill-on-close job. Job membership
    /// inherits, so the child's own descendants land in the job automatically.
    /// Returns false (logged) on failure; the caller continues without
    /// kill-on-close for that child.
    /// </summary>
    public bool Adopt(Process child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        if (!AssignProcessToJobObject(_handle, child.Handle))
        {
            LauncherLogger.Error(
                $"job_object: AssignProcessToJobObject failed (win32 {Marshal.GetLastWin32Error()}); " +
                $"child PID {child.Id} not in kill-on-close job");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Clear KILL_ON_JOB_CLOSE so that — when the launcher exits and its last
    /// handle closes — the job dissolves WITHOUT killing its remaining members
    /// (notably Velopack's Update.exe during an apply). Returns true when the
    /// flag was cleared, false (logged) on kernel failure. Idempotent.
    /// </summary>
    public bool ReleaseKillOnClose()
    {
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0; // no kill-on-close, no other limits
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info,
                Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            LauncherLogger.Error($"job_object: ReleaseKillOnClose SetInformationJobObject failed (win32 {Marshal.GetLastWin32Error()})");
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    // --- Win32 interop ---

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation, int cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
