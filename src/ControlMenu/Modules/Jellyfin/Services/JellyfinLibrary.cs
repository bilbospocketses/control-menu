namespace ControlMenu.Modules.Jellyfin.Services;

/// <summary>
/// A Jellyfin library (a <c>CollectionFolder</c>) as reported by <c>/Library/VirtualFolders</c>.
/// <paramref name="HasCard"/> is whether it currently has a My Media card.
/// </summary>
public record JellyfinLibrary(string Id, string Name, string? CollectionType, bool HasCard);

/// <summary>Outcome of regenerating one library's My Media card.</summary>
public record MediaCardResult(string LibraryId, string LibraryName, bool Regenerated, string? BackupPath, string? Error);
