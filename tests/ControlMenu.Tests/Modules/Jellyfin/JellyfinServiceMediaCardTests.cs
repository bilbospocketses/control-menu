using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinServiceMediaCardTests : IDisposable
{
    private const string VirtualFoldersJson = """
        [
          { "Name": "Movies",      "ItemId": "lib-movies", "CollectionType": "movies",      "PrimaryImageItemId": "lib-movies" },
          { "Name": "TV Shows",    "ItemId": "lib-tv",     "CollectionType": "tvshows",     "PrimaryImageItemId": "lib-tv" },
          { "Name": "Collections", "ItemId": "lib-coll",   "CollectionType": "boxsets",     "PrimaryImageItemId": null }
        ]
        """;

    private static readonly JellyfinApiConfig ApiConfig = new("http://jf:8096", "secret-key", "user-1");

    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();
    private readonly Mock<IJellyfinDirectoryResolver> _mockDirectoryResolver = new();
    private readonly string _backupDir = Path.Combine(Path.GetTempPath(), "cm-cardtests-" + Guid.NewGuid().ToString("N"));

    public JellyfinServiceMediaCardTests()
    {
        _mockDirectoryResolver.Setup(r => r.GetBackupDirectoryAsync()).ReturnsAsync(_backupDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_backupDir)) Directory.Delete(_backupDir, recursive: true);
        }
        catch { /* temp dir cleanup is best effort */ }
        GC.SuppressFinalize(this);
    }

    private (JellyfinService Service, List<HttpRequestMessage> Requests) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new ScriptedHandler(requests.Add, respond);
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var service = new JellyfinService(_mockExecutor.Object, _mockConfig.Object,
            _mockHttpFactory.Object, _mockResolver.Object, _mockDirectoryResolver.Object);
        return (service, requests);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Png(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    [Fact]
    public async Task GetLibrariesAsync_ParsesTheVirtualFolderList()
    {
        var (service, _) = CreateService(_ => Json(VirtualFoldersJson));

        var libraries = await service.GetLibrariesAsync(ApiConfig);

        Assert.Equal(3, libraries.Count);
        Assert.Equal("Movies", libraries[0].Name);
        Assert.Equal("lib-movies", libraries[0].Id);
        Assert.Equal("movies", libraries[0].CollectionType);
        Assert.True(libraries[0].HasCard);
        Assert.False(libraries[2].HasCard);
    }

    [Fact]
    public async Task GetLibrariesAsync_SendsTheApiKeyAsAHeaderNeverInTheQueryString()
    {
        var (service, requests) = CreateService(_ => Json(VirtualFoldersJson));

        await service.GetLibrariesAsync(ApiConfig);

        var req = Assert.Single(requests);
        Assert.Equal("secret-key", req.Headers.GetValues("X-Emby-Token").Single());
        // Query strings land in proxy logs and request traces.
        Assert.DoesNotContain("secret-key", req.RequestUri!.Query);
    }

    [Fact]
    public async Task RefreshLibraryCardAsync_RefreshesImagesOnly()
    {
        var (service, requests) = CreateService();

        await service.RefreshLibraryCardAsync("lib-movies", ApiConfig);

        var req = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        var uri = req.RequestUri!.ToString();
        Assert.Contains("/Items/lib-movies/Refresh", uri);
        // metadataRefreshMode=FullRefresh here would rescan every item in the library and turn a
        // 30-second card regeneration into an overnight job.
        Assert.Contains("metadataRefreshMode=None", uri);
        Assert.Contains("imageRefreshMode=FullRefresh", uri);
        Assert.Contains("replaceAllImages=true", uri);
    }

    [Fact]
    public async Task DeleteLibraryCardAsync_DeletesThePrimaryImage()
    {
        var (service, requests) = CreateService();

        await service.DeleteLibraryCardAsync("lib-movies", ApiConfig);

        var req = Assert.Single(requests);
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.Contains("/Items/lib-movies/Images/Primary", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task BackupLibraryCardAsync_WritesTheCardIntoTheBackupDirectory()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var (service, _) = CreateService(_ => Png(bytes));

        var path = await service.BackupLibraryCardAsync("lib-movies", "Movies", ApiConfig);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.EndsWith(".png", path);
        Assert.Contains("Movies", Path.GetFileName(path));
    }

    [Fact]
    public async Task BackupLibraryCardAsync_SanitisesLibraryNamesThatArePathTraversal()
    {
        var (service, _) = CreateService(_ => Png([1, 2, 3]));

        var path = await service.BackupLibraryCardAsync("lib-x", "../../etc/passwd", ApiConfig);

        Assert.NotNull(path);
        // A library name is server-supplied text, not a path component: it must not escape the
        // media-cards folder under the backup directory.
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_backupDir, "media-cards")),
            Path.GetFullPath(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task BackupLibraryCardAsync_ReturnsNull_WhenTheLibraryHasNoCard()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var path = await service.BackupLibraryCardAsync("lib-coll", "Collections", ApiConfig);

        // No card is not a failure -- it just means there is nothing to preserve.
        Assert.Null(path);
    }

    [Fact]
    public async Task HasLibraryCardAsync_ReflectsWhetherTheImageEndpointSucceeds()
    {
        var (withCard, _) = CreateService(_ => Png([1]));
        Assert.True(await withCard.HasLibraryCardAsync("lib-movies", ApiConfig));

        var (withoutCard, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await withoutCard.HasLibraryCardAsync("lib-movies", ApiConfig));
    }
}

internal sealed class ScriptedHandler(
    Action<HttpRequestMessage> capture,
    Func<HttpRequestMessage, HttpResponseMessage>? respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        capture(request);
        return Task.FromResult(respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
