using System.Runtime.CompilerServices;

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
/// <item><b>Fake clocks</b> -- never sleep so the code "has time" to arm its next timer before an
/// Advance; a loaded box loses that race and the test hangs on a timer that started late. Wait for
/// the timer itself with <see cref="TimerCountingTimeProvider.TimersArmedAsync"/>.</item>
/// </list>
/// </summary>
internal static class AsyncSignal
{
    /// <summary>Ceiling for a signal that should arrive essentially immediately. Never waited in a
    /// passing run -- it exists so a broken run fails with a HUNG message instead of hanging. It is
    /// deliberately generous: correct code that only runs slower on a loaded box must never fail.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>Window a negative assertion watches before concluding nothing arrived. Long enough
    /// for a continuation on a loaded runner; the whole window is paid on every passing run, so it
    /// is deliberately modest. A slow box only makes it stricter; it cannot fail a correct run.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    /// <summary>The failure text for a wait that ran out: it starts with HUNG and names what the
    /// test was waiting on, so a hang is never mistaken for an assertion about the result.</summary>
    public static string Hung(string what) =>
        $"HUNG: waited {Timeout.TotalSeconds:F0} s for `{what}`, and it never arrived";

    /// <summary>Awaits <paramref name="signal"/>, failing with a HUNG message rather than hanging.</summary>
    public static async Task ArrivesAsync(
        Task signal, [CallerArgumentExpression(nameof(signal))] string what = "")
    {
        try
        {
            await signal.WaitAsync(Timeout);
        }
        catch (TimeoutException ex) when (!signal.IsCompleted)
        {
            throw new TimeoutException(Hung(what), ex);
        }
    }

    /// <inheritdoc cref="ArrivesAsync(Task, string)"/>
    public static async Task<T> ArrivesAsync<T>(
        Task<T> signal, [CallerArgumentExpression(nameof(signal))] string what = "")
    {
        try
        {
            return await signal.WaitAsync(Timeout);
        }
        catch (TimeoutException ex) when (!signal.IsCompleted)
        {
            throw new TimeoutException(Hung(what), ex);
        }
    }

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
    /// Polls <paramref name="condition"/> until it holds, then returns. For state that no callback
    /// announces; prefer <see cref="ArrivesAsync"/> whenever the code under test raises a signal.
    /// </summary>
    /// <param name="because">What the test is waiting for -- named in the HUNG message.</param>
    public static async Task BecomesTrueAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail(Hung(because));
            await Task.Delay(10);
        }
    }
}
