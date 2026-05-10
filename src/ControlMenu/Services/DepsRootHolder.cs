namespace ControlMenu.Services;

/// <summary>
/// Static holder so module classes (which initialize Dependencies as a
/// readonly property) can read the resolver-derived deps root without
/// taking it via DI. Set once at composition root in Program.cs after
/// IDataPathResolver is built. Reads throw if accessed before set.
///
/// This pattern exists because IToolModule implementations declare
/// Dependencies as a property whose value is computed at type-init time
/// from a static readonly field — they can't take an IDataPathResolver
/// constructor argument without restructuring the IToolModule contract.
/// </summary>
internal static class DepsRootHolder
{
    private static string? _path;

    public static bool IsSet => _path is not null;

    public static string Path
    {
        get => _path ?? throw new InvalidOperationException("DepsRootHolder.Path read before composition root set it");
        set => _path = value;
    }
}
