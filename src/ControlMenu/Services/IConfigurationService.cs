using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IConfigurationService
{
    Task<string?> GetSettingAsync(string key, string? moduleId = null);
    Task SetSettingAsync(string key, string value, string? moduleId = null);
    Task<string?> GetSecretAsync(string key, string? moduleId = null);
    Task SetSecretAsync(string key, string value, string? moduleId = null);
    Task DeleteSettingAsync(string key, string? moduleId = null);
    Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId);
}
