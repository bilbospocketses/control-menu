using ControlMenu.Data;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Tests.Services;

public class ConfigurationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        _db = TestDbContextFactory.Create();
        var provider = DataProtectionProvider.Create("ControlMenu-Tests");
        var secretStore = new SecretStore(provider);
        _service = new ConfigurationService(_db, secretStore);
    }

    public void Dispose() => _db.Dispose();

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
        var setting = _db.Settings.Single(s => s.Key == "api-key");
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
}
