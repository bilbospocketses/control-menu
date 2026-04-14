using ControlMenu.Services;

namespace ControlMenu.Modules.Cameras.Services;

public class CameraService(IConfigurationService config) : ICameraService
{
    private const string Module = "cameras";

    public async Task<CameraConfig?> GetCameraAsync(int index)
    {
        var name = await config.GetSettingAsync($"camera-{index}-name", Module);
        var ip = await config.GetSettingAsync($"camera-{index}-ip", Module);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip))
            return null;
        var portStr = await config.GetSettingAsync($"camera-{index}-port", Module);
        var port = int.TryParse(portStr, out var p) ? p : 80;
        return new CameraConfig(index, name, ip, port);
    }

    public async Task<List<CameraConfig>> GetConfiguredCamerasAsync()
    {
        var cameras = new List<CameraConfig>();
        for (var i = 1; i <= ICameraService.MaxCameras; i++)
        {
            var cam = await GetCameraAsync(i);
            if (cam is not null)
                cameras.Add(cam);
        }
        return cameras;
    }

    public async Task SaveCameraAsync(int index, string name, string ipAddress, int port)
    {
        await config.SetSettingAsync($"camera-{index}-name", name, Module);
        await config.SetSettingAsync($"camera-{index}-ip", ipAddress, Module);
        await config.SetSettingAsync($"camera-{index}-port", port.ToString(), Module);
    }

    public async Task SaveCredentialsAsync(int index, string username, string password)
    {
        await config.SetSecretAsync($"camera-{index}-username", username, Module);
        await config.SetSecretAsync($"camera-{index}-password", password, Module);
    }

    public async Task<(string Username, string Password)?> GetCredentialsAsync(int index)
    {
        var user = await config.GetSecretAsync($"camera-{index}-username", Module);
        var pass = await config.GetSecretAsync($"camera-{index}-password", Module);
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            return null;
        return (user, pass);
    }
}
