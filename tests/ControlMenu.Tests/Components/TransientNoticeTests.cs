using ControlMenu.Components.Shared;
using Microsoft.Extensions.Time.Testing;

namespace ControlMenu.Tests.Components;

public class TransientNoticeTests
{
    /// <summary>
    /// Builds a notice on a fake clock plus a signal that completes the first time the notice asks
    /// its component to re-render. The auto-dismiss clears the message *before* invoking that
    /// callback, so awaiting the signal is a deterministic "the dismiss has finished" handle — which
    /// is what lets every test below drive time explicitly instead of racing real elapsed time.
    /// </summary>
    private static (TransientNotice Notice, Task Rerendered) NoticeOn(FakeTimeProvider time)
    {
        var rerendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notice = new TransientNotice(
            () => { rerendered.TrySetResult(); return Task.CompletedTask; },
            time);
        return (notice, rerendered.Task);
    }

    /// <summary>
    /// Asserts no auto-dismiss re-render arrives within a real grace window. A dismiss runs its
    /// state change on a continuation, so a wrongly-surviving timer fires slightly *after* the
    /// <see cref="FakeTimeProvider.Advance"/> that released it — asserting instantly would race
    /// past the very bug these tests exist to catch. Waiting real time is safe here precisely
    /// because the clock is fake: a correctly-cancelled timer never fires no matter how long we
    /// wait, since fake time only moves when a test moves it.
    /// </summary>
    private static async Task AssertNoRerender(Task rerendered, string because)
    {
        var grace = Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.False(await Task.WhenAny(rerendered, grace) == rerendered, because);
    }

    [Fact]
    public void Show_SetsMessageClassIconAndVisible()
    {
        var notice = new TransientNotice(() => Task.CompletedTask, new FakeTimeProvider());
        notice.Show("hello", "status-success", "bi-check", dismissMs: 60_000);

        Assert.Equal("hello", notice.Message);
        Assert.Equal("status-success", notice.CssClass);
        Assert.Equal("bi-check", notice.Icon);
        Assert.True(notice.IsVisible);
    }

    [Fact]
    public async Task AutoDismiss_ClearsMessageAndNotifies()
    {
        var time = new FakeTimeProvider();
        var (notice, rerendered) = NoticeOn(time);

        notice.Show("bye", dismissMs: 80);
        Assert.True(notice.IsVisible);

        time.Advance(TimeSpan.FromMilliseconds(80));
        await rerendered.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(notice.Message);
        Assert.False(notice.IsVisible);
    }

    [Fact]
    public async Task Show_TwiceQuickly_LaterMessageSurvivesTheEarlierTimer()
    {
        // The bug this helper fixes: the settings pages started a Task.Delay timer with no CTS, so
        // an earlier message's timer would fire and wipe a newer message. Show() must cancel the
        // prior timer. The fake clock lands the assertions exactly on the two deadlines that matter,
        // rather than sampling somewhere between them and hoping the scheduler cooperates.
        var time = new FakeTimeProvider();
        var (notice, rerendered) = NoticeOn(time);

        notice.Show("first", dismissMs: 200);
        time.Advance(TimeSpan.FromMilliseconds(100));
        notice.Show("second", dismissMs: 200);   // restarts; the first 200ms timer must be cancelled

        // t=200ms — exactly when the *first* message's timer was due. It was cancelled, so the newer
        // message must still be standing.
        time.Advance(TimeSpan.FromMilliseconds(100));
        await AssertNoRerender(rerendered, "the first message's timer was cancelled and must not fire");
        Assert.Equal("second", notice.Message);

        // t=300ms — the second message's own deadline; now it clears.
        time.Advance(TimeSpan.FromMilliseconds(100));
        await rerendered.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(notice.Message);
    }

    [Fact]
    public async Task Clear_RemovesMessageImmediatelyAndStopsTimer()
    {
        var time = new FakeTimeProvider();
        var (notice, rerendered) = NoticeOn(time);

        notice.Show("x", dismissMs: 60_000);
        notice.Clear();

        Assert.Null(notice.Message);
        Assert.False(notice.IsVisible);

        // Advance far past the cancelled timer's deadline: it must never fire and clobber state.
        time.Advance(TimeSpan.FromMinutes(5));
        await AssertNoRerender(rerendered, "Clear() cancelled the timer, so it must never fire");
        Assert.Null(notice.Message);
    }
}
