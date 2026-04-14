using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISecretStore _secretStore;

    public ConfigurationService(IDbContextFactory<AppDbContext> dbFactory, ISecretStore secretStore)
    {
        _dbFactory = dbFactory;
        _secretStore = secretStore;
    }

    public async Task<string?> GetSettingAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, string? moduleId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
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
            db.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.IsSecret = false;
        }
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetSecretAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null) return null;
        return setting.IsSecret ? _secretStore.Decrypt(setting.Value) : setting.Value;
    }

    public async Task SetSecretAsync(string key, string value, string? moduleId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var encrypted = _secretStore.Encrypt(value);
        var setting = await db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
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
            db.Settings.Add(setting);
        }
        else
        {
            setting.Value = encrypted;
            setting.IsSecret = true;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteSettingAsync(string key, string? moduleId = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var setting = await db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
        if (setting is not null)
        {
            db.Settings.Remove(setting);
            await db.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Settings
            .Where(s => s.ModuleId == moduleId)
            .ToListAsync();
    }

    private async Task<Setting?> FindSettingAsync(string key, string? moduleId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
    }
}
