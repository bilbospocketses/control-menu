using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class DeviceChangeNotifierTests
{
    [Fact]
    public void NotifyChanged_FiresChangedEvent()
    {
        var notifier = new DeviceChangeNotifier();
        var fired = 0;
        notifier.Changed += () => fired++;

        notifier.NotifyChanged();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void NotifyChanged_FaultingSubscriberDoesNotBlockOthers()
    {
        var notifier = new DeviceChangeNotifier();
        var secondFired = false;
        notifier.Changed += () => throw new InvalidOperationException("boom");
        notifier.Changed += () => secondFired = true;

        notifier.NotifyChanged();

        Assert.True(secondFired);
    }

    [Fact]
    public void NotifyChanged_NoSubscribers_DoesNotThrow()
    {
        var notifier = new DeviceChangeNotifier();
        var ex = Record.Exception((Action)(() => notifier.NotifyChanged()));
        Assert.Null(ex);
    }
}
