namespace ControlMenu.Services;

public sealed class CameraChangeNotifier : ICameraChangeNotifier
{
    public event Action? CamerasChanged;

    public void NotifyChanged()
    {
        var snapshot = CamerasChanged?.GetInvocationList();
        if (snapshot is null) return;
        foreach (var handler in snapshot)
        {
            try { ((Action)handler)(); }
            catch { }
        }
    }
}
