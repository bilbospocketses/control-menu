using System.Runtime.CompilerServices;
using Bunit;
using Bunit.Extensions.WaitForHelpers;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Tests.TestHelpers;

/// <summary>
/// bUnit waits on the suite's hang ceiling. bUnit's own default is one second, and a page whose
/// render lands on a continuation can take longer than that on a loaded box -- correct code that
/// only runs slower must never fail a test. A wait that does run out fails with a HUNG message
/// naming what it waited for, so a hang is never mistaken for an assertion about the page.
/// </summary>
internal static class BunitWaits
{
    /// <summary>bUnit's <c>WaitForState</c> with <see cref="AsyncSignal.Timeout"/> and a HUNG failure.</summary>
    public static void WaitForStateOrHung<TComponent>(
        this IRenderedComponent<TComponent> cut,
        Func<bool> statePredicate,
        [CallerArgumentExpression(nameof(statePredicate))] string what = "")
        where TComponent : IComponent
    {
        try
        {
            cut.WaitForState(statePredicate, AsyncSignal.Timeout);
        }
        catch (WaitForFailedException ex) when (RanOut(ex))
        {
            throw new TimeoutException(AsyncSignal.Hung(what), ex);
        }
    }

    /// <summary>bUnit's <c>WaitForAssertion</c> with <see cref="AsyncSignal.Timeout"/> and a HUNG
    /// failure that keeps the last assertion's message.</summary>
    public static void WaitForAssertionOrHung<TComponent>(
        this IRenderedComponent<TComponent> cut,
        Action assertion,
        [CallerArgumentExpression(nameof(assertion))] string what = "")
        where TComponent : IComponent
    {
        try
        {
            cut.WaitForAssertion(assertion, AsyncSignal.Timeout);
        }
        catch (WaitForFailedException ex) when (RanOut(ex))
        {
            throw new TimeoutException(
                $"{AsyncSignal.Hung(what)}; last failure: {ex.InnerException?.Message}", ex);
        }
    }

    /// <summary>bUnit raises the same exception type when a state predicate throws; only the
    /// time-out messages mention the timeout, and only those are a hang.</summary>
    private static bool RanOut(WaitForFailedException ex) =>
        ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
}
