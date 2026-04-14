using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class CameraServiceTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly CameraService _sut;

    public CameraServiceTests() => _sut = new CameraService(_config.Object);

    [Fact]
    public async Task GetCameraAsync_ReturnsNull_WhenNotConfigured()
    {
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync((string?)null);
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync((string?)null);
        var result = await _sut.GetCameraAsync(1);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCameraAsync_ReturnsConfig_WhenConfigured()
    {
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync("Front Door");
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync("192.168.86.101");
        _config.Setup(c => c.GetSettingAsync("camera-1-port", "cameras")).ReturnsAsync("80");
        var result = await _sut.GetCameraAsync(1);
        Assert.NotNull(result);
        Assert.Equal("Front Door", result.Name);
        Assert.Equal("192.168.86.101", result.IpAddress);
        Assert.Equal(80, result.Port);
    }

    [Fact]
    public async Task GetCameraAsync_DefaultsPort80_WhenNotSet()
    {
        _config.Setup(c => c.GetSettingAsync("camera-3-name", "cameras")).ReturnsAsync("Garage");
        _config.Setup(c => c.GetSettingAsync("camera-3-ip", "cameras")).ReturnsAsync("192.168.86.103");
        _config.Setup(c => c.GetSettingAsync("camera-3-port", "cameras")).ReturnsAsync((string?)null);
        var result = await _sut.GetCameraAsync(3);
        Assert.NotNull(result);
        Assert.Equal(80, result.Port);
    }

    [Fact]
    public async Task GetConfiguredCamerasAsync_ReturnsOnlyConfigured()
    {
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync("Front Door");
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync("192.168.86.101");
        _config.Setup(c => c.GetSettingAsync("camera-1-port", "cameras")).ReturnsAsync("80");
        var result = await _sut.GetConfiguredCamerasAsync();
        Assert.Single(result);
        Assert.Equal("Front Door", result[0].Name);
    }

    [Fact]
    public async Task SaveCameraAsync_StoresAllFields()
    {
        await _sut.SaveCameraAsync(2, "Garage", "192.168.86.102", 8080);
        _config.Verify(c => c.SetSettingAsync("camera-2-name", "Garage", "cameras"));
        _config.Verify(c => c.SetSettingAsync("camera-2-ip", "192.168.86.102", "cameras"));
        _config.Verify(c => c.SetSettingAsync("camera-2-port", "8080", "cameras"));
    }

    [Fact]
    public async Task SaveCredentialsAsync_StoresAsSecrets()
    {
        await _sut.SaveCredentialsAsync(1, "admin", "secret123");
        _config.Verify(c => c.SetSecretAsync("camera-1-username", "admin", "cameras"));
        _config.Verify(c => c.SetSecretAsync("camera-1-password", "secret123", "cameras"));
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsNull_WhenNotSet()
    {
        var result = await _sut.GetCredentialsAsync(1);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsTuple_WhenSet()
    {
        _config.Setup(c => c.GetSecretAsync("camera-1-username", "cameras")).ReturnsAsync("admin");
        _config.Setup(c => c.GetSecretAsync("camera-1-password", "cameras")).ReturnsAsync("secret123");
        var result = await _sut.GetCredentialsAsync(1);
        Assert.NotNull(result);
        Assert.Equal("admin", result.Value.Username);
        Assert.Equal("secret123", result.Value.Password);
    }
}
