using ControlMenu.Modules.Cameras.Entities;

namespace ControlMenu.Modules.Cameras.Services;

public interface ICameraService
{
    Task<IReadOnlyList<Camera>> GetAllAsync();
    Task<IReadOnlyList<Camera>> GetEnabledAsync();
    Task<Camera?> GetAsync(Guid id);
    Task<Camera> AddAsync(Camera camera, string username, string password);
    Task UpdateAsync(Camera camera);
    Task SetCredentialsAsync(Guid id, string username, string password);
    Task<(string Username, string Password)?> GetCredentialsAsync(Guid id);
    Task DeleteAsync(Guid id);
    Task UpdateLastSeenAsync(Guid id);
    /// <summary>Deletes all cameras and their stored credentials. Fires the change notifier exactly once.</summary>
    Task<int> DeleteAllAsync();
}
