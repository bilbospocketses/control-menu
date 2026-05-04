namespace ControlMenu.Modules.Cameras.Network;

public interface IOnvifClient
{
    Task<OnvifDeviceInfo> GetDeviceInformationAsync(string serviceUrl, string username, string password, CancellationToken ct);
    Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(string serviceUrl, string username, string password, CancellationToken ct);
    Task<string> GetStreamUriAsync(string serviceUrl, string profileToken, string username, string password, CancellationToken ct);
}

public sealed record OnvifDeviceInfo(
    string Manufacturer,
    string Model,
    string FirmwareVersion,
    string SerialNumber,
    string HardwareId);

public sealed record OnvifProfile(
    string Token,
    string Name);
