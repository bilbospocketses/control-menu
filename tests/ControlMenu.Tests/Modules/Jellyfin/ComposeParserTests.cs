using ControlMenu.Modules.Jellyfin.Services;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class ComposeParserTests : IDisposable
{
    private readonly string _dir;

    public ComposeParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-compose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteCompose(string content)
    {
        var path = Path.Combine(_dir, "docker-compose.yml");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parse_ValidCompose_ExtractsContainerConfigAndDbPath()
    {
        // Windows-rooted host path (tests run on Windows; CM reads the db locally).
        var path = WriteCompose(
            "services:\n" +
            "  jellyfin:\n" +
            "    container_name: jellyfin\n" +
            "    volumes:\n" +
            "      - C:/srv/jellyfin/config:/config\n" +
            "      - C:/media:/media\n");

        var result = ComposeParser.Parse(path);

        Assert.Null(result.ErrorMessage);
        Assert.Equal("jellyfin", result.ContainerName);
        Assert.Equal("C:/srv/jellyfin/config", result.ConfigHostPath);
        Assert.Equal(Path.Combine("C:/srv/jellyfin/config", "data", "jellyfin.db"), result.DbPath);
    }

    [Fact]
    public void Parse_FileOverSizeCap_ReturnsError_WithoutParsing()
    {
        // > 1 MB; the size guard must reject before reading the file into memory.
        var big = "services:\n" + new string('#', 1_100_000) + "\n";
        var path = WriteCompose(big);

        var result = ComposeParser.Parse(path);

        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("too large", result.ErrorMessage!);
        Assert.Null(result.ConfigHostPath);
    }

    [Fact]
    public void Parse_RelativeConfigHostPath_ReturnsError()
    {
        // A non-fully-qualified host path can't be resolved to the local jellyfin.db, and is
        // rejected as defense-in-depth before it reaches Path.Combine / the sqlite3 sink.
        var path = WriteCompose(
            "services:\n" +
            "  jellyfin:\n" +
            "    container_name: jellyfin\n" +
            "    volumes:\n" +
            "      - ./config:/config\n");

        var result = ComposeParser.Parse(path);

        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.DbPath);
    }
}
