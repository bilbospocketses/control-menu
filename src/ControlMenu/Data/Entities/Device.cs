using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }
    public required string MacAddress { get; set; }
    /// <summary>
    /// Canonical ADB serial number for this device (value of <c>ro.serialno</c>).
    /// Populated automatically when adding a discovered device (live ADB probe
    /// in <c>DiscoveredPanelRow.RunProbesAsync</c>); can also be set manually
    /// via the Add/Edit form.
    /// </summary>
    /// <remarks>
    /// Same concept as <see cref="Services.Network.ScanHit.Serial"/> but
    /// authoritative — sourced from a connected ADB session rather than the
    /// mDNS service-name string. When both exist for the same device they
    /// should agree; this field is the persistent truth.
    /// </remarks>
    public string? SerialNumber { get; set; }
    public string? LastKnownIp { get; set; }
    public int AdbPort { get; set; } = 5555;
    public DateTime? LastSeen { get; set; }
    public required string ModuleId { get; set; }
    public string? Metadata { get; set; }
}
