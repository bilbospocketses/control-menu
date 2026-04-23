namespace ControlMenu.Services;

public sealed class DeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        var snapshot = Changed?.GetInvocationList();
        if (snapshot is null) return;
        foreach (var handler in snapshot)
        {
            try { ((Action)handler)(); }
            catch { }
        }
    }
}
