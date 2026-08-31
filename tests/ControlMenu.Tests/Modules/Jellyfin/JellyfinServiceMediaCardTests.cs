using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinServiceMediaCardTests : IDisposable
{
    // Shaped after the real /UserViews response: libraries are CollectionFolders, Playlists and
    // Live TV are UserViews. Live TV is the one Jellyfin has no generator for.
    private const string UserViewsJson = """
        {
          "Items": [
            { "Name": "Movies",      "Id": "lib-movies", "Type": "CollectionFolder", "CollectionType": "movies",   "ImageTags": { "Primary": "aaa" } },
            { "Name": "TV Shows",    "Id": "lib-tv",     "Type": "CollectionFolder", "CollectionType": "tvshows",  "ImageTags": { "Primary": "bbb" } },
            { "Name": "Music",       "Id": "lib-music",  "Type": "CollectionFolder", "CollectionType": "music",    "ImageTags": {} },
            { "Name": "Audiobooks",  "Id": "lib-books",  "Type": "CollectionFolder", "CollectionType": "books",    "ImageTags": {} },
            { "Name": "Playlists",   "Id": "view-plist", "Type": "UserView",         "CollectionType": "playlists","ImageTags": { "Primary": "ccc" } },
            { "Name": "Live TV",     "Id": "view-livetv","Type": "UserView",         "CollectionType": "livetv",   "ImageTags": { "Primary": "ddd" } }
          ],
          "TotalRecordCount": 6,
          "StartIndex": 0
        }
        """;

    private const string UsersJson = """
        [
          { "Id": "user-regular", "Name": "kid",   "Policy": { "IsAdministrator": false } },
          { "Id": "user-admin",   "Name": "jamie", "Policy": { "IsAdministrator": true  } }
        ]
        """;

    private static readonly JellyfinApiConfig ApiConfig = new("http://jf:8096", "secret-key", "user-1");
    private static readonly JellyfinApiConfig NoUserApiConfig = new("http://jf:8096", "secret-key", null);

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

    /// <summary>Routes /Users and /UserViews the way the real server does.</summary>
    private static HttpResponseMessage RouteViews(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/Users", StringComparison.Ordinal)) return Json(UsersJson);
        if (path.EndsWith("/UserViews", StringComparison.Ordinal)) return Json(UserViewsJson);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
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
    public async Task GetMediaCardTargetsAsync_ReturnsTheWholeMyMediaRow_NotJustLibraries()
    {
        var (service, _) = CreateService(RouteViews);

        var targets = await service.GetMediaCardTargetsAsync(ApiConfig);

        // /Library/VirtualFolders reports only CollectionFolders, which is why Playlists was missing.
        Assert.Equal(6, targets.Count);
        Assert.Contains(targets, t => t.Name == "Playlists");
        Assert.Contains(targets, t => t.Name == "Live TV");
    }

    [Fact]
    public async Task GetMediaCardTargetsAsync_MarksPlaylistsRegenerable()
    {
        var (service, _) = CreateService(RouteViews);

        var playlists = (await service.GetMediaCardTargetsAsync(ApiConfig)).Single(t => t.Name == "Playlists");

        Assert.True(playlists.CanRegenerate);
        Assert.True(playlists.HasCard);
    }

    [Fact]
    public async Task GetMediaCardTargetsAsync_MarksLiveTvNotRegenerable()
    {
        var (service, _) = CreateService(RouteViews);

        var liveTv = (await service.GetMediaCardTargetsAsync(ApiConfig)).Single(t => t.Name == "Live TV");

        Assert.False(liveTv.CanRegenerate);
        Assert.Contains("Live TV", liveTv.BlockedReason);
    }

    [Theory]
    [InlineData("Music")]
    [InlineData("Audiobooks")]
    public async Task GetMediaCardTargetsAsync_MarksEveryLibraryRegenerable(string name)
    {
        var (service, _) = CreateService(RouteViews);

        var target = (await service.GetMediaCardTargetsAsync(ApiConfig)).Single(t => t.Name == name);

        // Music, music videos and books/audiobooks are CollectionFolders like any other library, so
        // they work the day they are added -- no per-type list to keep in sync.
        Assert.True(target.CanRegenerate);
        Assert.False(target.HasCard);
    }

    [Fact]
    public async Task GetMediaCardTargetsAsync_UsesTheConfiguredUser()
    {
        var (service, requests) = CreateService(RouteViews);

        await service.GetMediaCardTargetsAsync(ApiConfig);

        Assert.DoesNotContain(requests, r => r.RequestUri!.AbsolutePath.EndsWith("/Users", StringComparison.Ordinal));
        Assert.Contains("userId=user-1", requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task GetMediaCardTargetsAsync_FallsBackToTheAdminWhenNoUserIsConfigured()
    {
        var (service, requests) = CreateService(RouteViews);

        await service.GetMediaCardTargetsAsync(NoUserApiConfig);

        // /UserViews needs a user and an API key carries none. Returning nothing when
        // jellyfin-user-id happens to be unset is the trap the cast & crew job fell into.
        Assert.Contains("userId=user-admin", requests.Last().RequestUri!.Query);
    }

    [Fact]
    public async Task GetMediaCardTargetsAsync_SendsTheApiKeyAsAHeaderNeverInTheQueryString()
    {
        var (service, requests) = CreateService(RouteViews);

        await service.GetMediaCardTargetsAsync(ApiConfig);

        var req = Assert.Single(requests);
        Assert.Equal("secret-key", req.Headers.GetValues("X-Emby-Token").Single());
        Assert.DoesNotContain("secret-key", req.RequestUri!.Query);
    }

    [Fact]
    public async Task RefreshLibraryCardAsync_NeverReplacesAllImages()
    {
        var (service, requests) = CreateService();

        await service.RefreshLibraryCardAsync("lib-movies", ApiConfig);

        // 2026-08-30 incident: replaceAllImages=true on a LIBRARY recurses into its children and
        // re-fetches every one of their images -- ~2,200 files across D:\Movies, D:\TV_Shows and
        // the boxset folders, from four ticked libraries. The card is deleted first, so the
        // provider is filling an empty slot and `true` buys nothing. Never set it again.
        var uri = Assert.Single(requests).RequestUri!.ToString();
        Assert.Contains("replaceAllImages=false", uri);
        Assert.DoesNotContain("replaceAllImages=true", uri);
    }

    [Fact]
    public async Task RefreshLibraryCardAsync_NeverUsesMetadataRefreshModeNone()
    {
        var (service, requests) = CreateService();

        await service.RefreshLibraryCardAsync("lib-movies", ApiConfig);

        // MetadataService.RefreshMetadata nests the ImageProvider.RefreshImages call inside
        // `if (MetadataRefreshMode != None)`, so None skips the image refresh outright and the card
        // can never come back. ValidationOnly clears that gate without running metadata providers,
        // which require >= Default.
        var req = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        var uri = req.RequestUri!.ToString();
        Assert.Contains("/Items/lib-movies/Refresh", uri);
        Assert.Contains("metadataRefreshMode=ValidationOnly", uri);
        Assert.DoesNotContain("metadataRefreshMode=None", uri);
        Assert.Contains("imageRefreshMode=FullRefresh", uri);
    }

    [Fact]
    public async Task RestoreLibraryCardAsync_UploadsTheBackupWithItsRealMimeType()
    {
        var backup = Path.Combine(Path.GetTempPath(), $"card-{Guid.NewGuid():N}.png");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 9 };
        await File.WriteAllBytesAsync(backup, bytes);
        try
        {
            // The body must be read inside the handler: the service disposes its HttpContent as
            // soon as the call returns, so reading it afterwards throws ObjectDisposedException.
            byte[]? sent = null;
            string? sentMime = null;
            var (service, requests) = CreateService(req =>
            {
                sentMime = req.Content?.Headers.ContentType?.MediaType;
                sent = req.Content?.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

            await service.RestoreLibraryCardAsync("lib-movies", backup, ApiConfig);

            var req = Assert.Single(requests);
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("/Items/lib-movies/Images/Primary", req.RequestUri!.ToString());
            Assert.Equal("image/png", sentMime);
            Assert.Equal(bytes, sent);
        }
        finally
        {
            File.Delete(backup);
        }
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
    public async Task FindLatestCardBackupAsync_ReturnsTheNewestBackupForThatLibrary()
    {
        var dir = Path.Combine(_backupDir, "media-cards");
        Directory.CreateDirectory(dir);
        var older = Path.Combine(dir, "Movies-20260830-100000.png");
        var newer = Path.Combine(dir, "Movies-20260830-225459.png");
        var other = Path.Combine(dir, "Playlists-20260830-224856.png");
        foreach (var f in new[] { older, newer, other }) await File.WriteAllBytesAsync(f, [1]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var (service, _) = CreateService();

        Assert.Equal(newer, await service.FindLatestCardBackupAsync("Movies"));
        Assert.Equal(other, await service.FindLatestCardBackupAsync("Playlists"));
    }

    [Fact]
    public async Task FindLatestCardBackupAsync_ReturnsNull_WhenNothingWasEverBackedUp()
    {
        var (service, _) = CreateService();

        Assert.Null(await service.FindLatestCardBackupAsync("Movies"));
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

public class MediaCardSupportTests
{
    [Theory]
    [InlineData("movies")]
    [InlineData("tvshows")]
    [InlineData("music")]
    [InlineData("musicvideos")]
    [InlineData("books")]
    [InlineData("boxsets")]
    [InlineData("homevideos")]
    [InlineData("photos")]
    public void EveryLibraryIsRegenerable_WhateverItsCollectionType(string collectionType)
    {
        // CollectionFolderImageProvider.Supports is `item is CollectionFolder` -- no type list.
        var (canRegenerate, reason) = MediaCardSupport.Evaluate("CollectionFolder", collectionType);

        Assert.True(canRegenerate);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("movies")]
    [InlineData("tvshows")]
    [InlineData("playlists")]
    public void CollectionStripViewsAreRegenerable(string viewType)
    {
        Assert.True(MediaCardSupport.Evaluate("UserView", viewType).CanRegenerate);
    }

    [Fact]
    public void LiveTvIsNotRegenerable()
    {
        // DynamicImageProvider.IsUsingCollectionStrip lists movies, tvshows and playlists only, so
        // a deleted Live TV card can never be rebuilt by Jellyfin.
        var (canRegenerate, reason) = MediaCardSupport.Evaluate("UserView", "livetv");

        Assert.False(canRegenerate);
        Assert.Contains("Live TV", reason);
    }

    [Fact]
    public void UnknownItemTypesAreNotRegenerable()
    {
        Assert.False(MediaCardSupport.Evaluate("Folder", null).CanRegenerate);
        Assert.False(MediaCardSupport.Evaluate(null, null).CanRegenerate);
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
