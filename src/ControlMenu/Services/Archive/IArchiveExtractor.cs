namespace ControlMenu.Services.Archive;

public interface IArchiveExtractor
{
    /// <summary>Extract a .zip, .tar.gz, or .7z archive into <paramref name="destDir"/>.</summary>
    void Extract(string archivePath, string destDir);
}
