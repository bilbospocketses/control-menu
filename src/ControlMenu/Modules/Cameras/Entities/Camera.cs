namespace ControlMenu.Modules.Cameras.Entities;

public class Camera
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public int Port { get; set; } = 554;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? RtspStreamUrl { get; set; }
    public string? OnvifServiceUrl { get; set; }
    public bool IsOnvif { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastSeen { get; set; }
    public string? MacAddress { get; set; }
    public int? CameraNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? HardwareId { get; set; }
}
