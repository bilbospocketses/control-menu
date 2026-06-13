namespace ControlMenu.Components.Pages;

/// <summary>
/// First-paint helpers for the home page's modular tile grid. The authoritative
/// per-card span is measured client-side (wwwroot/js/home-tiles.js); this only
/// seeds a close initial value so the server-rendered first paint does not flash
/// before the client measure-and-snap pass runs.
/// </summary>
public static class HomeTileLayout
{
    // Roughly three pill-links fit in one tile-unit row. Round up so a card is
    // never seeded too short; the client pass corrects each card precisely.
    private const int PillsPerUnit = 3;

    /// <summary>Initial grid-row span for a card with the given visible-link count.</summary>
    public static int InitialSpan(int visibleEntryCount)
    {
        if (visibleEntryCount <= 0)
            return 1;
        return (visibleEntryCount + PillsPerUnit - 1) / PillsPerUnit; // integer ceil
    }
}
