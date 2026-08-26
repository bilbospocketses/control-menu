using Microsoft.Extensions.Time.Testing;

namespace ControlMenu.Tests.TestHelpers;

/// <summary>
/// Assertions for work that lands on a continuation rather than inline.
///
/// Sleeping a fixed span and then asserting is the flake class that blocked this repo's Dependabot
/// lane for two months: a positive assertion sampled mid-window passes only while the scheduler
/// cooperates. The rules these helpers encode are:
///
/// <list type="bullet">
/// <item><b>Positive assertion</b> ("this must happen") -- never sleep. Await a signal the
/// production code itself raises, via <see cref="ArrivesAsync"/>. The timeout is a failure
/// ceiling, not a wait: a healthy run completes the moment the signal fires.</item>
/// <item><b>Negative assertion</b> ("this must NOT happen") -- a real wait is unavoidable, because
/// absence cannot be awaited. <see cref="NeverArrivesAsync"/> bounds it explicitly and says why in
/// the failure message. Erring longer here is safe: it only ever makes the assertion stricter.</item>
/// </list>
/// </summary>
internal static class AsyncSignal
{
    /// <summary>Ceiling for a signal that should arrive essentially immediately. Never waited in a
    /// passing run -- it exists so a broken run fails with a clear timeout instead of hanging.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Window a negative assertion watches before concluding nothing arrived. Long enough
    /// for a continuation on a loaded runner; the whole window is paid on every passing run, so it
    /// is deliberately modest.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    /// <summary>Awaits <paramref name="signal"/>, failing with a timeout rather than hanging.</summary>
    public static Task ArrivesAsync(Task signal, TimeSpan? timeout = null)
        => signal.WaitAsync(timeout ?? Timeout);

    /// <inheritdoc cref="ArrivesAsync(Task, TimeSpan?)"/>
    public static Task<T> ArrivesAsync<T>(Task<T> signal, TimeSpan? timeout = null)
        => signal.WaitAsync(timeout ?? Timeout);

    /// <summary>
    /// Asserts <paramref name="signal"/> does not complete within a bounded real window.
    /// </summary>
    /// <param name="because">Why it must not fire -- surfaces as the assertion message.</param>
    public static async Task NeverArrivesAsync(Task signal, string because, TimeSpan? grace = null)
    {
        var window = Task.Delay(grace ?? Grace);
        Assert.False(await Task.WhenAny(signal, window) == signal, because);
    }

    /// <summary>
    /// Yields real time so code under test can resume from an await and arm its <em>next</em>
    /// fake-clock timer.
    ///
    /// <see cref="FakeTimeProvider"/> deadlines are absolute, not relative to when a test calls
    /// Advance: advance before the code has reached its next <c>Task.Delay</c> and that timer simply
    /// starts counting from the new "now", so the advance never reaches it and the test hangs on a
    /// signal that will never come. Await this between a completed cycle and the advance that should
    /// drive the following one. Waiting real time here is safe -- a frozen fake clock cannot fire
    /// anything while we wait, so this can only ever make the following assertion more reliable.
    /// </summary>
    public static Task SettleAsync() => Task.Delay(100);

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds, then returns. For state that no callback
    /// announces; prefer <see cref="ArrivesAsync"/> whenever the code under test raises a signal.
    /// </summary>
    public static async Task BecomesTrueAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail(because);
            await Task.Delay(10);
        }
    }
}
