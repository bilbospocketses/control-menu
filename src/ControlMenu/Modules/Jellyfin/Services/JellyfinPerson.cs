namespace ControlMenu.Modules.Jellyfin.Services;

public record JellyfinPerson(string Id, string Name);

public record JellyfinApiConfig(string BaseUrl, string ApiKey, string? UserId);
