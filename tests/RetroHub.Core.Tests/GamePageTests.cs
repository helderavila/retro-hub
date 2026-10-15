using System.Net;
using RetroHub.Core.Covers;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

public class GamePageTests
{
    /// <summary>Responde com <paramref name="respond"/> e guarda as URLs pedidas.</summary>
    private sealed class FakeHttp(Func<Uri, HttpStatusCode> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var status = respond(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(status == HttpStatusCode.OK ? [137, 80, 78, 71] : []) });
        }
    }

    private static (AppPaths Paths, LibraryDatabase Db, Game Game) Create(TempDir dir)
    {
        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(dir.Path, "manifests") };
        paths.EnsureCreated();
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        var rom = dir.File("snes/Chrono Trigger (USA).sfc", [1, 2, 3]);
        db.UpsertScanned([new ScannedRom(rom, "snes", "Chrono Trigger", null, "aa", 3)]);
        return (paths, db, db.GetGames().Single());
    }

    [Fact]
    public async Task Edited_info_is_never_overwritten_by_the_automatic_lookup()
    {
        using var dir = new TempDir();
        var (paths, db, game) = Create(dir);
        var http = new FakeHttp(_ => HttpStatusCode.OK);
        var metadata = new MetadataService(new HttpClient(http), paths, ManifestTests.Load(), db);
        game.MetadataJson = new GameMetadata { Genre = "Tiro", ManualInfo = true }.ToJson();

        Assert.Equal(0, await metadata.FillMissingAsync([game]));
        Assert.Empty(http.Requests);
        Assert.Equal("Tiro", GameMetadata.FromJson(game.MetadataJson)!.Genre);
    }

    [Fact]
    public void Catalog_name_comes_from_a_downloaded_cover_only()
    {
        using var dir = new TempDir();
        var (paths, db, game) = Create(dir);
        var covers = new CoverFetcher(new HttpClient(new FakeHttp(_ => HttpStatusCode.OK)), paths, ManifestTests.Load(), db);

        game.CoverPath = Path.Combine(paths.Covers, "snes", "Chrono Trigger (USA).png");
        Assert.Equal("Chrono Trigger (USA)", covers.CatalogName(game, "snes"));

        game.CoverPath = Path.Combine(paths.Covers, "snes", "custom-1.jpg");
        Assert.Null(covers.CatalogName(game, "snes"));

        game.CoverPath = CoverResolver.NoCover;
        Assert.Null(covers.CatalogName(game, "snes"));
    }

    [Fact]
    public async Task Screenshots_use_the_cover_name_and_a_missing_one_is_not_asked_again()
    {
        using var dir = new TempDir();
        var (paths, db, game) = Create(dir);
        var http = new FakeHttp(uri => uri.AbsolutePath.Contains("/Named_Snaps/") ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        var covers = new CoverFetcher(new HttpClient(http), paths, ManifestTests.Load(), db);
        game.CoverPath = Path.Combine(paths.Covers, "snes", "Chrono Trigger (USA).png");

        var snap = await covers.FetchArtAsync(game, CoverFetcher.Snapshots);
        Assert.NotNull(snap);
        Assert.True(File.Exists(snap));
        Assert.EndsWith("/Named_Snaps/Chrono%20Trigger%20%28USA%29.png", http.Requests.Single().AbsoluteUri);

        Assert.Null(await covers.FetchArtAsync(game, CoverFetcher.TitleScreens));
        Assert.Null(await covers.FetchArtAsync(game, CoverFetcher.TitleScreens));
        Assert.Equal(2, http.Requests.Count); // o segundo pedido da tela de título não vai para a internet
    }
}
