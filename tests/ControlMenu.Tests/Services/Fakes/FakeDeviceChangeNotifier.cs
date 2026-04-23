using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;

    public int NotifyChangedCallCount { get; set; }

    public void NotifyChanged()
    {
        NotifyChangedCallCount++;
        Changed?.Invoke();
    }

    public void RaiseChanged() => Changed?.Invoke();
}
