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

        var legacyKeys = new List<string>(MaxLegacyIndex * 5 + 1);
        for (int i = 1; i <= MaxLegacyIndex; i++)
        {
            legacyKeys.Add($"camera-{i}-name");
            legacyKeys.Add($"camera-{i}-ip");
            legacyKeys.Add($"camera-{i}-port");
            legacyKeys.Add($"camera-{i}-username");
            legacyKeys.Add($"camera-{i}-password");
        }
        legacyKeys.Add("camera-count");

        // One set-based DELETE instead of 81 per-key context opens/round-trips.
        await _config.DeleteSettingsAsync(legacyKeys, Module);
        await _config.SetSettingAsync(MarkerKey, "true", Module);
        _logger.LogInformation("Purged {Count} legacy camera-* settings + secrets", legacyKeys.Count);
    }
}
