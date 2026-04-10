using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly AppDbContext _db;
    private readonly ISecretStore _secretStore;

    public ConfigurationService(AppDbContext db, ISecretStore secretStore)
    {
        _db = db;
        _secretStore = secretStore;
    }

    public async Task<string?> GetSettingAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null)
        {
            setting = new Setting
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                Key = key,
                Value = value,
                IsSecret = false
            };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.IsSecret = false;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string?> GetSecretAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null) return null;
        return setting.IsSecret ? _secretStore.Decrypt(setting.Value) : setting.Value;
    }

    public async Task SetSecretAsync(string key, string value, string? moduleId = null)
    {
        var encrypted = _secretStore.Encrypt(value);
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null)
        {
            setting = new Setting
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                Key = key,
                Value = encrypted,
                IsSecret = true
            };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = encrypted;
            setting.IsSecret = true;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteSettingAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is not null)
        {
            _db.Settings.Remove(setting);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId)
    {
        return await _db.Settings
            .Where(s => s.ModuleId == moduleId)
            .ToListAsync();
    }

    private async Task<Setting?> FindSettingAsync(string key, string? moduleId)
    {
        return await _db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
    }
}
