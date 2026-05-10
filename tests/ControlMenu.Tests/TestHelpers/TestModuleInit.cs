using System.Runtime.CompilerServices;
using ControlMenu.Services;

namespace ControlMenu.Tests.TestHelpers;

/// <summary>
/// Sets DepsRootHolder.Path before any test in this assembly runs.
/// Module classes (AndroidDevicesModule, CamerasModule, JellyfinModule) have
/// a static readonly DepsRoot field that reads DepsRootHolder.Path at first
/// type-init; without this initializer the field throws in test contexts where
/// Program.cs never ran.
/// </summary>
internal static class TestModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Set only if not already set (avoids clobbering an integration-test
        // setup that explicitly sets the path before tests run).
        if (!DepsRootHolder.IsSet)
            DepsRootHolder.Path = Path.Combine(Path.GetTempPath(), "cm-test-deps");
    }
}
