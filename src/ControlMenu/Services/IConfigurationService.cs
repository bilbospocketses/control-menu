using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IConfigurationService
{
    Task<string?> GetSettingAsync(string key, string? moduleId = null);
    Task SetSettingAsync(string key, string value, string? moduleId = null);
    Task<string?> GetSecretAsync(string key, string? moduleId = null);
    Task SetSecretAsync(string key, string value, string? moduleId = null);
    Task DeleteSettingAsync(string key, string? moduleId = null);

    /// <summary>
    /// Deletes every setting whose key is in <paramref name="keys"/> within the given module in a
    /// single set-based round-trip (one DELETE), instead of one context open + query per key.
    /// </summary>
    Task DeleteSettingsAsync(IReadOnlyCollection<string> keys, string? moduleId = null);

    Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId);
}
