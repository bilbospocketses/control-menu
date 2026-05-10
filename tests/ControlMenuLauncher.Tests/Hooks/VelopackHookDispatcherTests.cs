using ControlMenu.Launcher.Hooks;
using Xunit;

namespace ControlMenu.Launcher.Tests.Hooks;

public class VelopackHookDispatcherTests
{
    [Theory]
    [InlineData(new[] { "--veloapp-install", "1.1.0" }, HookKind.Install)]
    [InlineData(new[] { "--veloapp-updated", "1.1.0" }, HookKind.Updated)]
    [InlineData(new[] { "--veloapp-uninstall", "1.1.0" }, HookKind.Uninstall)]
    [InlineData(new[] { "--veloapp-obsolete", "1.0.99" }, HookKind.Obsolete)]
    public void ParseHookFlag_KnownFlag_ReturnsKind(string[] args, HookKind expected)
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(args);
        Assert.NotNull(kind);
        Assert.Equal(expected, kind!.Kind);
    }

    [Fact]
    public void ParseHookFlag_UnknownVeloappFlag_ReturnsUnknownWithFlagText()
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-future-thing", "v"]);
        Assert.NotNull(kind);
        Assert.Equal(HookKind.Unknown, kind!.Kind);
        Assert.Equal("--veloapp-future-thing", kind.RawFlag);
    }

    [Fact]
    public void ParseHookFlag_NoVeloappFlag_ReturnsNull()
    {
        Assert.Null(VelopackHookDispatcher.ParseHookFlag(["--some-other-flag", "value"]));
        Assert.Null(VelopackHookDispatcher.ParseHookFlag([]));
    }

    [Fact]
    public void ParseHookFlag_KnownFlagPrecedesUnknown_ReturnsKnown()
    {
        // Mirrors upstream invariant: "Recognized flags take precedence over Unknown"
        // even when known appears AFTER unknown in argv.
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-future-thing", "--veloapp-install", "v"]);
        Assert.NotNull(kind);
        Assert.Equal(HookKind.Install, kind!.Kind);
    }

    [Fact]
    public void ParseHookFlag_KnownFlagAfterUnknown_StillWins()
    {
        // Both orderings: known flag wins regardless of position.
        var k1 = VelopackHookDispatcher.ParseHookFlag(["--veloapp-firstrun", "--veloapp-install"]);
        Assert.Equal(HookKind.Install, k1!.Kind);
        var k2 = VelopackHookDispatcher.ParseHookFlag(["--veloapp-install", "--veloapp-firstrun"]);
        Assert.Equal(HookKind.Install, k2!.Kind);
    }

    [Fact]
    public void ParseHookFlag_FirstUnknownWinsWhenMultipleUnknown()
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-foo", "--veloapp-bar"]);
        Assert.NotNull(kind);
        Assert.Equal(HookKind.Unknown, kind!.Kind);
        Assert.Equal("--veloapp-foo", kind.RawFlag);
    }

    [Fact]
    public void ParseHookFlag_VersionArgPopulatedWhenPresent()
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-install", "1.2.3"]);
        Assert.Equal("1.2.3", kind!.VersionArg);
    }

    [Fact]
    public void ParseHookFlag_VersionArgNullWhenAbsent()
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-install"]);
        Assert.Null(kind!.VersionArg);
    }
}
