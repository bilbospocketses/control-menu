using ControlMenu.Data;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class CameraServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<ICameraChangeNotifier> _notifier = new();
    private readonly CameraService _sut;

    public CameraServiceTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _sut = new CameraService(_dbFactory, _config.Object, _notifier.Object);
    }

    public void Dispose() => _dbFactory.Dispose();

    private static Camera NewCamera(string name = "Test", string ip = "192.168.1.50") => new()
    {
        Name = name, IpAddress = ip, Port = 554, IsOnvif = true, Enabled = true,
    };

    [Fact]
    public async Task AddAsync_AssignsId_PersistsRow_StoresCredentials_NotifiesChange()
    {
        var saved = await _sut.AddAsync(NewCamera(), "admin", "secret");

        Assert.NotEqual(Guid.Empty, saved.Id);
        var fetched = await _sut.GetAsync(saved.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Test", fetched.Name);
        _config.Verify(c => c.SetSecretAsync($"camera-{saved.Id:N}-username", "admin", "cameras"), Times.Once);
        _config.Verify(c => c.SetSecretAsync($"camera-{saved.Id:N}-password", "secret", "cameras"), Times.Once);
        _notifier.Verify(n => n.NotifyChanged(), Times.Once);
    }

    [Fact]
    public async Task GetEnabledAsync_FiltersDisabled()
    {
        var enabled = NewCamera("Enabled");
        var disabled = NewCamera("Disabled", "192.168.1.51");
        disabled.Enabled = false;
        await _sut.AddAsync(enabled, "u", "p");
        await _sut.AddAsync(disabled, "u", "p");

        var result = await _sut.GetEnabledAsync();
        Assert.Single(result);
        Assert.Equal("Enabled", result[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow_RemovesSecrets_NotifiesChange()
    {
        var saved = await _sut.AddAsync(NewCamera(), "u", "p");
        _notifier.Reset();

        await _sut.DeleteAsync(saved.Id);

        Assert.Null(await _sut.GetAsync(saved.Id));
        _config.Verify(c => c.DeleteSettingAsync($"camera-{saved.Id:N}-username", "cameras"), Times.Once);
        _config.Verify(c => c.DeleteSettingAsync($"camera-{saved.Id:N}-password", "cameras"), Times.Once);
        _notifier.Verify(n => n.NotifyChanged(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges_NotifiesChange()
    {
        var saved = await _sut.AddAsync(NewCamera(), "u", "p");
        _notifier.Reset();

        saved.Name = "Renamed";
        saved.Enabled = false;
        await _sut.UpdateAsync(saved);

        var fetched = await _sut.GetAsync(saved.Id);
        Assert.Equal("Renamed", fetched!.Name);
        Assert.False(fetched.Enabled);
        _notifier.Verify(n => n.NotifyChanged(), Times.Once);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_BumpsTimestamp()
    {
        var saved = await _sut.AddAsync(NewCamera(), "u", "p");
        var seededTimestamp = saved.LastSeen;
        Assert.NotNull(seededTimestamp);

        await Task.Delay(10);
        await _sut.UpdateLastSeenAsync(saved.Id);

        var fetched = await _sut.GetAsync(saved.Id);
        Assert.NotNull(fetched!.LastSeen);
        Assert.True(fetched.LastSeen > seededTimestamp);
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsNull_WhenAnyMissing()
    {
        var id = Guid.NewGuid();
        _config.Setup(c => c.GetSecretAsync($"camera-{id:N}-username", "cameras")).ReturnsAsync("admin");
        _config.Setup(c => c.GetSecretAsync($"camera-{id:N}-password", "cameras")).ReturnsAsync((string?)null);

        var result = await _sut.GetCredentialsAsync(id);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsTuple_WhenBothPresent()
    {
        var id = Guid.NewGuid();
        _config.Setup(c => c.GetSecretAsync($"camera-{id:N}-username", "cameras")).ReturnsAsync("admin");
        _config.Setup(c => c.GetSecretAsync($"camera-{id:N}-password", "cameras")).ReturnsAsync("secret");

        var result = await _sut.GetCredentialsAsync(id);
        Assert.NotNull(result);
        Assert.Equal("admin", result.Value.Username);
        Assert.Equal("secret", result.Value.Password);
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesAllRows_RemovesSecrets_NotifiesOnce()
    {
        await _sut.AddAsync(NewCamera("A", "192.168.1.50"), "u1", "p1");
        await _sut.AddAsync(NewCamera("B", "192.168.1.51"), "u2", "p2");
        _notifier.Reset();

        var deleted = await _sut.DeleteAllAsync();

        Assert.Equal(2, deleted);
        Assert.Empty(await _sut.GetAllAsync());
        _config.Verify(c => c.DeleteSettingAsync(It.Is<string>(s => s.StartsWith("camera-")), "cameras"),
            Times.Exactly(4)); // 2 cameras x (username + password)
        _notifier.Verify(n => n.NotifyChanged(), Times.Once);
    }

    [Fact]
    public async Task DeleteAllAsync_ReturnsZero_WhenEmpty()
    {
        var deleted = await _sut.DeleteAllAsync();
        Assert.Equal(0, deleted);
        _notifier.Verify(n => n.NotifyChanged(), Times.Never);
    }
}
