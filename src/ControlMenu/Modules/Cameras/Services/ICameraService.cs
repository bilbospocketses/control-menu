namespace ControlMenu.Modules.Cameras.Services;

public interface ICameraService
{
    const int MaxCameras = 8;
    Task<CameraConfig?> GetCameraAsync(int index);
    Task<List<CameraConfig>> GetConfiguredCamerasAsync();
    Task SaveCameraAsync(int index, string name, string ipAddress, int port);
    Task SaveCredentialsAsync(int index, string username, string password);
    Task<(string Username, string Password)?> GetCredentialsAsync(int index);
}
