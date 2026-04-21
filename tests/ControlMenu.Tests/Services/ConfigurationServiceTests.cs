using ControlMenu.Data;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Tests.Services;

public class ConfigurationServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        var provider = DataProtectionProvider.Create("ControlMenu-Tests");
        var secretStore = new SecretStore(provider);
        _service = new ConfigurationService(_dbFactory, secretStore);
    }

    public void Dispose() => _dbFactory.Dispose();

    [Fact]
    public async Task GetSettingAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetSettingAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetSetting_RoundTrip()
    {
        await _service.SetSettingAsync("theme", "dark");
        var result = await _service.GetSettingAsync("theme");
        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task SetSetting_OverwritesExistingValue()
    {
        await _service.SetSettingAsync("theme", "dark");
        await _service.SetSettingAsync("theme", "light");
        var result = await _service.GetSettingAsync("theme");
        Assert.Equal("light", result);
    }

    [Fact]
    public async Task SetAndGetSecret_RoundTrip()
    {
        await _service.SetSecretAsync("api-key", "secret-value-123");
        var result = await _service.GetSecretAsync("api-key");
        Assert.Equal("secret-value-123", result);
    }

    [Fact]
    public async Task SetSecret_StoresEncryptedValue()
    {
        await _service.SetSecretAsync("api-key", "secret-value-123");
        using var db = _dbFactory.CreateDbContext();
        var setting = db.Settings.Single(s => s.Key == "api-key");
        Assert.True(setting.IsSecret);
        Assert.NotEqual("secret-value-123", setting.Value);
    }

    [Fact]
    public async Task ModuleScoping_SameKeyDifferentModules()
    {
        await _service.SetSettingAsync("url", "http://global", moduleId: null);
        await _service.SetSettingAsync("url", "http://jellyfin", moduleId: "jellyfin");
        var global = await _service.GetSettingAsync("url", moduleId: null);
        var jellyfin = await _service.GetSettingAsync("url", moduleId: "jellyfin");
        Assert.Equal("http://global", global);
        Assert.Equal("http://jellyfin", jellyfin);
    }

    [Fact]
    public async Task DeleteSettingAsync_RemovesSetting()
    {
        await _service.SetSettingAsync("theme", "dark");
        await _service.DeleteSettingAsync("theme");
        var result = await _service.GetSettingAsync("theme");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetModuleSettingsAsync_ReturnsOnlyModuleSettings()
    {
        await _service.SetSettingAsync("global-key", "global-value");
        await _service.SetSettingAsync("jf-url", "http://localhost:8096", moduleId: "jellyfin");
        await _service.SetSettingAsync("jf-key", "abc123", moduleId: "jellyfin");
        await _service.SetSettingAsync("other-key", "other", moduleId: "other-module");
        var settings = await _service.GetModuleSettingsAsync("jellyfin");
        Assert.Equal(2, settings.Count);
        Assert.All(settings, s => Assert.Equal("jellyfin", s.ModuleId));
    }

    [Fact]
    public async Task ScanSubnets_RoundTripsJsonArray()
    {
        var subnets = new[] { "192.168.1.0/24", "10.0.0.0/16" };
        await _service.SetSettingAsync("scan-subnets", System.Text.Json.JsonSerializer.Serialize(subnets));
        var raw = await _service.GetSettingAsync("scan-subnets");
        var back = System.Text.Json.JsonSerializer.Deserialize<string[]>(raw!);
        Assert.Equal(subnets, back);
    }

    [Theory]
    [InlineData("managed")]
    [InlineData("external")]
    public async Task WsscrcpyMode_RoundTrips(string mode)
    {
        await _service.SetSettingAsync("wsscrcpy-mode", mode);
        Assert.Equal(mode, await _service.GetSettingAsync("wsscrcpy-mode"));
    }

    [Fact]
    public async Task WsscrcpyUrl_RoundTrips()
    {
        await _service.SetSettingAsync("wsscrcpy-url", "http://ws-scrcpy:8000");
        Assert.Equal("http://ws-scrcpy:8000", await _service.GetSettingAsync("wsscrcpy-url"));
    }
}
