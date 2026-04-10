using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }
    public required string MacAddress { get; set; }
    public string? SerialNumber { get; set; }
    public string? LastKnownIp { get; set; }
    public int AdbPort { get; set; } = 5555;
    public DateTime? LastSeen { get; set; }
    public required string ModuleId { get; set; }
    public string? Metadata { get; set; }
}
