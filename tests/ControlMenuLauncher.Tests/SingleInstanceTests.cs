using System.Runtime.Versioning;
using ControlMenu.Launcher;
using Xunit;

namespace ControlMenu.Launcher.Tests;

[SupportedOSPlatform("windows")]
public class SingleInstanceTests
{
    [Fact]
    public void CurrentMutexName_HasControlMenuHyphenedBaseAndElevationSuffix()
    {
        var name = SingleInstance.CurrentMutexName();
        Assert.StartsWith(@"Local\ControlMenu-SingleInstance-", name);
        Assert.True(name.EndsWith("-User") || name.EndsWith("-Admin"),
            $"expected hyphenated User/Admin suffix; got: {name}");
    }

    [Fact]
    public void Acquire_FirstCall_ReturnsHandle()
    {
        var name = $@"Local\ControlMenu-SingleInstanceTest-{Guid.NewGuid():N}";
        using var handle = SingleInstance.Acquire(name);
        Assert.NotNull(handle);
    }

    [Fact]
    public void Acquire_TwiceSameNameSameProcess_SecondReturnsNull()
    {
        var name = $@"Local\ControlMenu-SingleInstanceTest-{Guid.NewGuid():N}";
        using var first = SingleInstance.Acquire(name);
        Assert.NotNull(first);
        var second = SingleInstance.Acquire(name);
        Assert.Null(second);
    }

    [Fact]
    public void Acquire_AfterFirstReleased_SecondSucceeds()
    {
        var name = $@"Local\ControlMenu-SingleInstanceTest-{Guid.NewGuid():N}";
        var first = SingleInstance.Acquire(name);
        Assert.NotNull(first);
        first!.Dispose();
        using var second = SingleInstance.Acquire(name);
        Assert.NotNull(second);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var name = $@"Local\ControlMenu-SingleInstanceTest-{Guid.NewGuid():N}";
        var instance = SingleInstance.Acquire(name);
        Assert.NotNull(instance);

        instance!.Dispose();
        var ex = Record.Exception(() => instance.Dispose());
        Assert.Null(ex);
    }
}
