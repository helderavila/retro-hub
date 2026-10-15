using RetroHub.Core.Library;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

public class CollectionsTests
{
    private static (LibraryDatabase Db, long GameId) Create(TempDir dir)
    {
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var rom = dir.File("snes/Chrono Trigger (USA).sfc", [1, 2, 3]);
        db.UpsertScanned([new ScannedRom(rom, "snes", "Chrono Trigger", null, "aa", 3)]);
        return (db, db.GetGames().Single().Id);
    }

    [Fact]
    public void Starts_with_default_collections_and_tracks_members()
    {
        using var dir = new TempDir();
        var (db, game) = Create(dir);

        Assert.Equal(["Completed", "Want to play"], db.GetCollections().Select(c => c.Name));

        var zerados = db.GetCollections()[0].Id;
        db.SetInCollection(zerados, game, true);
        db.SetInCollection(zerados, game, true); // repetir não duplica
        Assert.Equal(1, db.GetCollections()[0].Count);
        Assert.Contains(game, db.GetCollectionMembers()[zerados]);

        var nova = db.CreateCollection("  Com a família ");
        Assert.Equal(nova, db.CreateCollection("com a família")); // mesmo nome = mesma coleção
        db.SetInCollection(nova, game, true);
        db.SetInCollection(zerados, game, false);
        Assert.Equal(0, db.GetCollections().Single(c => c.Id == zerados).Count);
        Assert.Equal(1, db.GetCollections().Single(c => c.Name == "Com a família").Count);
    }

    [Fact]
    public void Removing_a_game_removes_it_from_collections()
    {
        using var dir = new TempDir();
        var (db, game) = Create(dir);
        var zerados = db.GetCollections()[0].Id;
        db.SetInCollection(zerados, game, true);

        db.RemoveGame(game);

        Assert.Equal(0, db.GetCollections()[0].Count);
    }

    [Fact]
    public void Sessions_feed_the_statistics()
    {
        using var dir = new TempDir();
        var (db, game) = Create(dir);
        db.RecordSession(game, DateTimeOffset.UtcNow.AddDays(-20), 600);
        db.RecordSession(game, DateTimeOffset.UtcNow.AddHours(-2), 1800);

        var recent = db.GetSessions(DateTimeOffset.UtcNow.AddDays(-7));

        var session = Assert.Single(recent);
        Assert.Equal(1800, session.Seconds);
        Assert.Equal("snes", session.SystemId);
        Assert.Equal(2400, db.GetGame(game)!.PlaySeconds);
    }
}
