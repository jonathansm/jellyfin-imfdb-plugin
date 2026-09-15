using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Imfdb.Api;
using Jellyfin.Plugin.Imfdb.Models;
using Jellyfin.Plugin.Imfdb.Services;
using Jellyfin.Plugin.Imfdb.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.Imfdb.Tests;

public sealed class RegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Plugin _plugin;
    private readonly ImfdbCacheService _cache = new(NullLogger<ImfdbCacheService>.Instance);

    public RegressionTests()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(x => x.PluginsPath).Returns(_root);
        paths.SetupGet(x => x.PluginConfigurationsPath).Returns(_root);
        _plugin = new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
        _plugin.UpdateConfiguration(new Configuration.PluginConfiguration());
    }

    private static ImfdbLookupResult Result(BaseItem item) => new(item.Id, null, item.Name, item.ProductionYear,
        "Source", "https://www.imfdb.org/wiki/Source", null, []);

    [Fact]
    public async Task EpisodesInSameSeasonHaveIndependentCaches()
    {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var first = new Episode { Id = Guid.NewGuid(), Name = "First", SeriesId = seriesId, SeasonId = seasonId };
        var second = new Episode { Id = Guid.NewGuid(), Name = "Second", SeriesId = seriesId, SeasonId = seasonId };
        await _cache.WriteAsync(first, Result(first), default);
        await _cache.WriteAsync(second, Result(second), default);
        Assert.Equal(first.Id, (await _cache.ReadAsync(first, default))!.ItemId);
        Assert.Equal(second.Id, (await _cache.ReadAsync(second, default))!.ItemId);
    }

    [Fact]
    public async Task MetadataChangesInvalidateCache()
    {
        var item = new Movie { Id = Guid.NewGuid(), Name = "Original" };
        await _cache.WriteAsync(item, Result(item), default);
        item.Name = "Corrected";
        Assert.Null(await _cache.ReadAsync(item, default));
    }

    [Theory]
    [InlineData("{\"version\":1,\"firearms\":null}")]
    [InlineData("invalid json")]
    public async Task MalformedCacheIsAMiss(string json)
    {
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        await _cache.WriteAsync(item, Result(item), default);
        var file = Directory.GetFiles(_root, "imfdb.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(file, json);
        Assert.Null(await _cache.ReadAsync(item, default));
    }

    [Fact]
    public async Task CancelledCacheWritePropagatesCancellation()
    {
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _cache.WriteAsync(item, Result(item), new CancellationToken(true)));
    }

    [Theory]
    [InlineData("../secret.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\secret.jpg")]
    public void ImagePathRejectsTraversal(string path) => Assert.Null(_cache.GetImagePath(new Movie(), path));

    [Fact]
    public async Task MissingImagesRecommendRefresh()
    {
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        var result = Result(item) with { Firearms = [new("Gun", null, null, "https://example.com/image.jpg", "Summary", null)] };
        await _cache.WriteAsync(item, result, default);
        Assert.True((await _cache.ReadAsync(item, default))!.RefreshRecommended);
    }

    [Fact]
    public async Task RestrictedItemsNeverReachLookupOrCache()
    {
        var user = new User("viewer", "auth", "reset") { Id = Guid.NewGuid() };
        var users = new Mock<IUserManager>();
        users.Setup(x => x.GetUserById(user.Id)).Returns(user);
        var library = new Mock<ILibraryManager>();
        var item = new Movie { Id = Guid.NewGuid(), Name = "Restricted" };
        library.Setup(x => x.GetItemById(item.Id)).Returns(item);
        library.Setup(x => x.GetItemById<BaseItem>(item.Id, user)).Returns((BaseItem?)null);
        var client = new Mock<IImfdbClient>(MockBehavior.Strict);
        var cache = new Mock<IImfdbCacheService>(MockBehavior.Strict);
        var controller = new ImfdbController(library.Object, users.Object, client.Object, cache.Object, NullLogger<ImfdbController>.Instance);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", user.Id.ToString())], "test")) } };
        Assert.IsType<NotFoundResult>((await controller.Lookup(item.Id, false, default)).Result);
        Assert.IsType<NotFoundResult>(controller.Image(item.Id, "image.jpg"));
        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"error\":{\"code\":\"ratelimited\"}}")]
    public async Task WikiErrorsAreNotSuccessfulEmptyResults(string body)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var client = new ImfdbClient(NullLogger<ImfdbClient>.Instance, http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.LookupAsync("Movie", null, default));
    }

    [Fact]
    public async Task YearAloneCannotMatchUnrelatedTitle()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"query\":{\"search\":[{\"title\":\"Unrelated\",\"snippet\":\"1999\"}]}}") }));
        var client = new ImfdbClient(NullLogger<ImfdbClient>.Instance, http);
        Assert.Empty((await client.LookupAsync("The Matrix", 1999, default)).Firearms);
    }

    [Fact]
    public async Task UpstreamFailureReturns502WithoutOverwritingCache()
    {
        var user = new User("viewer", "auth", "reset") { Id = Guid.NewGuid() };
        var users = new Mock<IUserManager>();
        users.Setup(x => x.GetUserById(user.Id)).Returns(user);
        var library = new Mock<ILibraryManager>();
        var item = new Movie { Id = Guid.NewGuid(), Name = "Movie" };
        library.Setup(x => x.GetItemById<BaseItem>(item.Id, user)).Returns(item);
        var client = new Mock<IImfdbClient>();
        client.Setup(x => x.LookupAsync(item.Name, item.ProductionYear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Unavailable"));
        var cache = new Mock<IImfdbCacheService>(MockBehavior.Strict);
        var controller = new ImfdbController(library.Object, users.Object, client.Object, cache.Object, NullLogger<ImfdbController>.Instance);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", user.Id.ToString())], "test")) } };
        Assert.Equal(502, Assert.IsType<StatusCodeResult>((await controller.Lookup(item.Id, true, default)).Result).StatusCode);
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WikiLookupParsesSectionAndNestedImageCaption()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            var body = request.RequestUri!.Query.Contains("action=query", StringComparison.Ordinal)
                ? "{\"query\":{\"search\":[{\"title\":\"Movie\",\"snippet\":\"Movie\"}]}}"
                : JsonSerializer.Serialize(new { parse = new { title = "Movie", wikitext = new Dictionary<string, string> { ["*"] = "== Pistol ==\nA [[Pistol|sidearm]] is used.\n[[File:Example.jpg|thumb|A [[Pistol|pistol]] in the film.]]\n== See also ==\nIgnored" } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var result = await new ImfdbClient(NullLogger<ImfdbClient>.Instance, http).LookupAsync("Movie", null, default);
        var firearm = Assert.Single(result.Firearms);
        Assert.Equal("Pistol", firearm.Name);
        Assert.Equal("A sidearm is used.", firearm.Details);
        Assert.Equal("A pistol in the film.", firearm.Summary);
        Assert.EndsWith("#Pistol", firearm.SourceSectionUrl);
        Assert.StartsWith("https://www.imfdb.org/images/", firearm.ImageUrl);
    }

    [Fact]
    public void TransformerIsIdempotentAndHonorsDisabledSetting()
    {
        var original = "<html><body>Content</body></html>";
        var transformed = ImfdbWebTransformer.TransformIndex(new() { Contents = original });
        Assert.Contains("../Imfdb/ClientScript", transformed);
        Assert.Equal(transformed, ImfdbWebTransformer.TransformIndex(new() { Contents = transformed }));
        _plugin.Configuration.EnableWebUiInjection = false;
        Assert.Equal(original, ImfdbWebTransformer.TransformIndex(new() { Contents = original }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
