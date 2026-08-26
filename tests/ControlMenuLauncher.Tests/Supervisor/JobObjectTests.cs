using System.Diagnostics;
using System.Runtime.Versioning;
using ControlMenu.Launcher.Supervisor;
using Xunit;

namespace ControlMenu.Launcher.Tests;

/// <summary>
/// Unit coverage for the kill-on-close Job Object port (mirrors ws-scrcpy-web
/// launcher/src/job_object.rs tests). Real KILL_ON_JOB_CLOSE-vs-release
/// termination behavior is verified via VM smoke, not here (it needs a real
/// grandchild surviving launcher exit) — these cover the create/adopt/release
/// surface deterministically.
/// </summary>
[SupportedOSPlatform("windows")]
public class JobObjectTests
{
    [Fact]
    public void CreateKillOnClose_ReturnsInstance()
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.NotNull(job);
    }

    [Fact]
    public void ReleaseKillOnClose_Succeeds_AndIsIdempotent()
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.NotNull(job);

        Assert.True(job!.ReleaseKillOnClose(), "first release clears the kill-on-close flag");
        Assert.True(job.ReleaseKillOnClose(), "release is idempotent");
    }

    [Fact]
    public void Adopt_PlacesFreshChildInJob()
    {
        using var job = JobObject.CreateKillOnClose();
        Assert.NotNull(job);

        // Interactive cmd.exe blocks reading the redirected (never-closed) stdin,
        // so the child stays alive until we kill it — long enough to adopt.
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        })!;
        try
        {
            // Modern Windows (all supported CI runners) permits nested job objects,
            // so adopting a child already in the runner's job still succeeds.
            Assert.True(job!.Adopt(child), "adopt should succeed for a fresh child");

            // Clear kill-on-close before the test process drops its job handle so
            // the kernel doesn't terminate the child via kill-on-close on dispose.
            Assert.True(job.ReleaseKillOnClose());
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch { /* already gone */ }
            child.WaitForExit(5000);
        }
    }
}
