namespace ControlMenu.Modules.Cameras.Network;

public interface IHikvisionIsapiClient
{
    /// <summary>
    /// Fetches Hikvision/LTS proprietary device-info via ISAPI. Best-effort —
    /// returns null on any failure (network, auth, parse, non-ISAPI camera).
    /// Caller is responsible for vendor-gating before calling.
    /// </summary>
    Task<HikvisionDeviceInfo?> GetDeviceInfoAsync(string ipAddress, int port, string username, string password, CancellationToken ct);
}

public sealed record HikvisionDeviceInfo(
    string? DeviceName,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    string? MacAddress = null,
    int? TelecontrolId = null);
