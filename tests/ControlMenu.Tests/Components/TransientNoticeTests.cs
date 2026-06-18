using ControlMenu.Components.Shared;

namespace ControlMenu.Tests.Components;

public class TransientNoticeTests
{
    [Fact]
    public void Show_SetsMessageClassIconAndVisible()
    {
        var notice = new TransientNotice(() => Task.CompletedTask);
        notice.Show("hello", "status-success", "bi-check", dismissMs: 60_000);

        Assert.Equal("hello", notice.Message);
        Assert.Equal("status-success", notice.CssClass);
        Assert.Equal("bi-check", notice.Icon);
        Assert.True(notice.IsVisible);
    }

    [Fact]
    public async Task AutoDismiss_ClearsMessageAndNotifies()
    {
        var changes = 0;
        var notice = new TransientNotice(() => { Interlocked.Increment(ref changes); return Task.CompletedTask; });
        notice.Show("bye", dismissMs: 80);

        Assert.True(notice.IsVisible);
        await Task.Delay(250);

        Assert.Null(notice.Message);
        Assert.False(notice.IsVisible);
        Assert.True(changes >= 1, "auto-dismiss should notify the component to re-render");
    }

    [Fact]
    public async Task Show_TwiceQuickly_LaterMessageSurvivesTheEarlierTimer()
    {
        // The bug this helper fixes: the settings pages started a Task.Delay timer with no CTS, so
        // an earlier message's timer would fire and wipe a newer message. Show() must cancel the
        // prior timer.
        var notice = new TransientNotice(() => Task.CompletedTask);

        notice.Show("first", dismissMs: 200);
        await Task.Delay(100);
        notice.Show("second", dismissMs: 200);   // restarts; the first 200ms timer must be cancelled

        await Task.Delay(180);   // ~280ms: past the first message's 200ms deadline, before the second's (~300ms)
        Assert.Equal("second", notice.Message);

        await Task.Delay(180);   // ~460ms: past the second's deadline
        Assert.Null(notice.Message);
    }

    [Fact]
    public async Task Clear_RemovesMessageImmediatelyAndStopsTimer()
    {
        var notice = new TransientNotice(() => Task.CompletedTask);
        notice.Show("x", dismissMs: 60_000);
        notice.Clear();

        Assert.Null(notice.Message);
        Assert.False(notice.IsVisible);
        await Task.Delay(50);   // the long timer must not fire and clobber state later
        Assert.Null(notice.Message);
    }
}
