namespace ControlMenu.Modules.Jellyfin.Services;

/// <summary>
/// One tile in Jellyfin's My Media row, as reported by <c>/UserViews</c>.
/// </summary>
/// <param name="Id">The item id to refresh.</param>
/// <param name="Name">The library name, which Jellyfin bakes into the card.</param>
/// <param name="CollectionType">Jellyfin's collection type (<c>movies</c>, <c>music</c>, <c>livetv</c>...).</param>
/// <param name="ItemType">The item's .NET type name, e.g. <c>CollectionFolder</c> or <c>UserView</c>.</param>
/// <param name="HasCard">Whether it has a primary image today.</param>
/// <param name="CanRegenerate">Whether Jellyfin will build a card for it -- see <see cref="MediaCardSupport"/>.</param>
/// <param name="BlockedReason">Why not, when it cannot.</param>
public record MediaCardTarget(
    string Id,
    string Name,
    string? CollectionType,
    string? ItemType,
    bool HasCard,
    bool CanRegenerate,
    string? BlockedReason);

/// <summary>
/// Outcome of regenerating one card. <paramref name="Restored"/> is set when regeneration failed
/// and the backup was put back, so the tile is never left blank.
/// </summary>
public record MediaCardResult(
    string LibraryId,
    string LibraryName,
    bool Regenerated,
    string? BackupPath,
    string? Error,
    bool Restored = false);

/// <summary>
/// Whether Jellyfin will generate a card for a given tile, mirroring the two providers that
/// actually do it (read at tag <c>v10.11.11</c>):
/// <list type="bullet">
/// <item><c>CollectionFolderImageProvider.Supports</c> is <c>item is CollectionFolder</c> -- every
/// library qualifies, including music, music videos and books/audiobooks, each of which the
/// provider has an explicit item-type case for.</item>
/// <item><c>DynamicImageProvider.Supports</c> (for <c>UserView</c>) requires the view type to be in
/// its collection-strip list: movies, tvshows, playlists. <b>Live TV is not in it</b>, so Jellyfin
/// never generates that card -- deleting it leaves the tile blank permanently.</item>
/// </list>
/// </summary>
public static class MediaCardSupport
{
    private const string CollectionFolderType = "CollectionFolder";
    private const string UserViewType = "UserView";
    private const string LiveTvCollectionType = "livetv";

    /// <summary>The view types <c>DynamicImageProvider.IsUsingCollectionStrip</c> accepts.</summary>
    private static readonly HashSet<string> CollectionStripViewTypes =
        new(StringComparer.OrdinalIgnoreCase) { "movies", "tvshows", "playlists" };

    public static (bool CanRegenerate, string? Reason) Evaluate(string? itemType, string? collectionType)
    {
        // Every library is a CollectionFolder, whatever its collection type.
        if (string.Equals(itemType, CollectionFolderType, StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }

        if (string.Equals(itemType, UserViewType, StringComparison.OrdinalIgnoreCase))
        {
            if (collectionType is not null && CollectionStripViewTypes.Contains(collectionType))
            {
                return (true, null);
            }

            if (string.Equals(collectionType, LiveTvCollectionType, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Jellyfin never generates a Live TV card. Deleting this one would leave the tile blank until you set a new image by hand.");
            }

            return (false, $"Jellyfin does not generate cards for '{collectionType ?? "unknown"}' views.");
        }

        return (false, $"Not a library or a generated view ({itemType ?? "unknown type"}).");
    }
}
