namespace ControlMenu.Services;

public record AssetMatch(
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    bool AutoSelected);
