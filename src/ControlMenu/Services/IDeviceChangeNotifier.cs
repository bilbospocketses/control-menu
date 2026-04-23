namespace ControlMenu.Services;

public interface IDeviceChangeNotifier
{
    event Action Changed;
    void NotifyChanged();
}
