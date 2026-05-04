using ControlMenu.Data;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Services;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Modules.Cameras.Services;

public class CameraService : ICameraService
{
    private const string Module = "cameras";
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfigurationService _config;
    private readonly ICameraChangeNotifier _notifier;

    public CameraService(
        IDbContextFactory<AppDbContext> dbFactory,
        IConfigurationService config,
        ICameraChangeNotifier notifier)
    {
        _dbFactory = dbFactory;
        _config = config;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<Camera>> GetAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Cameras.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<IReadOnlyList<Camera>> GetEnabledAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Cameras.AsNoTracking()
            .Where(c => c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Camera?> GetAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Camera> AddAsync(Camera camera, string username, string password)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        if (camera.Id == Guid.Empty) camera.Id = Guid.NewGuid();
        db.Cameras.Add(camera);
        await db.SaveChangesAsync();
        await _config.SetSecretAsync($"camera-{camera.Id:N}-username", username, Module);
        await _config.SetSecretAsync($"camera-{camera.Id:N}-password", password, Module);
        _notifier.NotifyChanged();
        return camera;
    }

    public async Task UpdateAsync(Camera camera)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.Cameras.FindAsync(camera.Id)
            ?? throw new InvalidOperationException($"Camera {camera.Id} not found.");
        db.Entry(existing).CurrentValues.SetValues(camera);
        await db.SaveChangesAsync();
        _notifier.NotifyChanged();
    }

    public async Task SetCredentialsAsync(Guid id, string username, string password)
    {
        await _config.SetSecretAsync($"camera-{id:N}-username", username, Module);
        await _config.SetSecretAsync($"camera-{id:N}-password", password, Module);
    }

    public async Task<(string Username, string Password)?> GetCredentialsAsync(Guid id)
    {
        var user = await _config.GetSecretAsync($"camera-{id:N}-username", Module);
        var pass = await _config.GetSecretAsync($"camera-{id:N}-password", Module);
        return string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass) ? null : (user, pass);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var camera = await db.Cameras.FindAsync(id);
        if (camera is null) return;
        db.Cameras.Remove(camera);
        await db.SaveChangesAsync();
        await _config.DeleteSettingAsync($"camera-{id:N}-username", Module);
        await _config.DeleteSettingAsync($"camera-{id:N}-password", Module);
        _notifier.NotifyChanged();
    }

    public async Task UpdateLastSeenAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var camera = await db.Cameras.FindAsync(id);
        if (camera is null) return;
        camera.LastSeen = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
