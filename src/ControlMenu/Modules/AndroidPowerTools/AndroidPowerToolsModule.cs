namespace ControlMenu.Modules.AndroidPowerTools;

/// <summary>
/// Thin module that embeds ws-scrcpy-web's full home page as an iframe.
/// Peer of AndroidDevices — not a replacement. Devices module stays the
/// primary management surface (device list, PIN unlock, power state, etc.);
/// this module gives direct access to the ws-scrcpy-web tooling that isn't
/// replicated in Control Menu: one-click shell, file browser, network-scan
/// panel, and stream configuration.
///
/// No standalone dependencies — ws-scrcpy-web is already declared by
/// AndroidDevicesModule and managed at runtime by WsScrcpyService.
/// </summary>
public class AndroidPowerToolsModule : IToolModule
{
    public string Id => "android-power-tools";
    public string DisplayName => "Android Power Tools";
    public string Icon => "bi-wrench";
    public int SortOrder => 2;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Power Tools", "/android-power-tools", "🛠️", 0)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
