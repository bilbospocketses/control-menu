namespace ControlMenu.Modules.Cameras.Entities;

public sealed record CameraDeviceInfo(
    string? FirmwareVersion,
    string? SerialNumber,
    string? HardwareId);
