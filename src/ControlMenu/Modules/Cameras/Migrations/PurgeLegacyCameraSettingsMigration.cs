using ControlMenu.Services;

namespace ControlMenu.Modules.Cameras.Migrations;

public class PurgeLegacyCameraSettingsMigration
{
    private const string Module = "cameras";
    private const string MarkerKey = "cameras-legacy-purge-completed";
    private const int MaxLegacyIndex = 16;

    private readonly IConfigurationService _config;
    private readonly ILogger<PurgeLegacyCameraSettingsMigration> _logger;

    public PurgeLegacyCameraSettingsMigration(
        IConfigurationService config,
        ILogger<PurgeLegacyCameraSettingsMigration> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var marker = await _config.GetSettingAsync(MarkerKey, Module);
        if (marker == "true") return;

        for (int i = 1; i <= MaxLegacyIndex; i++)
        {
            await _config.DeleteSettingAsync($"camera-{i}-name", Module);
            await _config.DeleteSettingAsync($"camera-{i}-ip", Module);
            await _config.DeleteSettingAsync($"camera-{i}-port", Module);
            await _config.DeleteSettingAsync($"camera-{i}-username", Module);
            await _config.DeleteSettingAsync($"camera-{i}-password", Module);
        }
        await _config.DeleteSettingAsync("camera-count", Module);
        await _config.SetSettingAsync(MarkerKey, "true", Module);
        _logger.LogInformation("Purged legacy camera-{{1..{Max}}}-* settings + secrets", MaxLegacyIndex);
    }
}
