namespace ControlMenu.Services;

public interface ICameraChangeNotifier
{
    event Action? CamerasChanged;
    void NotifyChanged();
}
