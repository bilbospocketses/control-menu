using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class IntervalChangeSignalTests
{
    [Fact]
    public void TriggerBeforeWait_IsLatched_NextWaitCompletesImmediately()
    {
        var signal = new IntervalChangeSignal();
        signal.Trigger(IntervalChangeSignal.AndroidLiveness); // no waiter parked → must be latched
        var wait = signal.WaitAsync(IntervalChangeSignal.AndroidLiveness);
        Assert.True(wait.IsCompletedSuccessfully); // latched trigger consumed — no lost wakeup
    }

    [Fact]
    public async Task TriggerAfterWait_CompletesTheParkedWaiter()
    {
        var signal = new IntervalChangeSignal();
        var wait = signal.WaitAsync(IntervalChangeSignal.CameraLiveness);
        Assert.False(wait.IsCompleted); // parked
        signal.Trigger(IntervalChangeSignal.CameraLiveness);
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void Latch_IsConsumedOnce_SecondWaitParks()
    {
        var signal = new IntervalChangeSignal();
        signal.Trigger("k");
        Assert.True(signal.WaitAsync("k").IsCompleted);  // consumes the latch
        Assert.False(signal.WaitAsync("k").IsCompleted); // nothing latched → parks
    }

    [Fact]
    public void Keys_AreIndependent()
    {
        var signal = new IntervalChangeSignal();
        signal.Trigger("a");
        Assert.True(signal.WaitAsync("a").IsCompleted);
        Assert.False(signal.WaitAsync("b").IsCompleted); // an unrelated key is unaffected
    }
}
