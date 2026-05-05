using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services.Startup;

/// <summary>
/// One-time startup cleanup. Deletes settings rows that became obsolete
/// when Managed mode for ws-scrcpy-web was removed. Idempotent — runs on
/// every boot but is a no-op once the rows are gone.
/// </summary>
public sealed class ObsoleteSettingsCleanupService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ObsoleteSettingsCleanupService> _logger;

    public ObsoleteSettingsCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ObsoleteSettingsCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var obsolete = await db.Settings
                .Where(s =>
                    (s.Key == "wsscrcpy-mode" && s.ModuleId == null) ||
                    (s.Key == "ws_scrcpy_web_path" && s.ModuleId == "android-devices"))
                .ToListAsync(cancellationToken);

            if (obsolete.Count == 0)
                return;

            db.Settings.RemoveRange(obsolete);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var row in obsolete)
            {
                _logger.LogInformation(
                    "Removed obsolete setting {Key} (module={ModuleId})",
                    row.Key, row.ModuleId ?? "<null>");
            }

            // Clean up stale "external" placeholder in the ws-scrcpy-web Dependency row.
            // Pre-T1 code wrote the literal string "external" into InstalledVersion;
            // post-T1 the field should be null (no version detection yet — see
            // ws-scrcpy-web todo §15 for the v0.5.0 cross-check).
            var wsScrcpyDep = await db.Dependencies
                .FirstOrDefaultAsync(d => d.Name == "ws-scrcpy-web" && d.InstalledVersion == "external",
                    cancellationToken);

            if (wsScrcpyDep is not null)
            {
                wsScrcpyDep.InstalledVersion = null;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Reset ws-scrcpy-web InstalledVersion (was stale 'external' placeholder)");
            }
        }
        catch (Exception ex)
        {
            // Cleanup must not block app startup. Log and move on.
            _logger.LogWarning(ex, "ObsoleteSettingsCleanupService failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
