using Microsoft.Extensions.Time.Testing;

namespace ControlMenu.Tests.TestHelpers;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that counts the timers the code under test arms on it.
///
/// <see cref="FakeTimeProvider"/> deadlines are absolute, not relative to when a test calls
/// Advance: advance before the code has reached its next <c>Task.Delay</c> and that timer simply
/// starts counting from the new "now", so the advance never reaches it and the test hangs on a
/// signal that will never come. Sleeping a short real span first only moves the race -- a loaded
/// box loses it. Await <see cref="TimersArmedAsync"/> instead: it returns once the timer exists,
/// however long the code took to get there.
/// </summary>
internal sealed class TimerCountingTimeProvider : FakeTimeProvider
{
    private int _armed;

    /// <summary>How many timers have been created on this clock so far.</summary>
    public int TimersArmed => Volatile.Read(ref _armed);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _armed);
        return timer;
    }

    /// <summary>Returns once at least <paramref name="count"/> timers have been armed on this
    /// clock, failing with a HUNG message if they never are.</summary>
    public Task TimersArmedAsync(int count) =>
        AsyncSignal.BecomesTrueAsync(() => TimersArmed >= count, $"timer #{count} to be armed on the fake clock");
}
