using RetroHub.Core.Library;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

public class DuplicateTests
{
    private static readonly byte[] Ps2Disc = TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT2 = cdrom0:\\SLUS_212.06;1");

    private static (RomScanner Scanner, LibraryDatabase Db) Create(TempDir dir)
    {
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        return (new RomScanner(ManifestTests.Load()), db);
    }

    [Fact]
    public void Same_game_in_two_folders_is_listed_once()
    {
        using var dir = new TempDir();
        var (scanner, db) = Create(dir);
        dir.File("Downloads/ps2/Black.iso", Ps2Disc);
        dir.File("Downloads/Black/Black.iso", Ps2Disc);
        dir.File("Downloads/ps2/Outro Jogo.iso", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT2 = cdrom0:\\SLUS_200.62;1"));

        var result = db.UpsertScanned(scanner.Scan([Path.Combine(dir.Path, "Downloads")]).Roms);

        Assert.Equal(2, result.Added);
        Assert.Single(result.Duplicates);
        Assert.Equal(2, db.GetGames().Count);

        // Escanear de novo não muda nada.
        var again = db.UpsertScanned(scanner.Scan([Path.Combine(dir.Path, "Downloads")]).Roms);
        Assert.Equal(0, again.Added);
        Assert.Equal(2, db.GetGames().Count);
    }

    [Fact]
    public void Moved_file_keeps_playtime_and_favorite()
    {
        using var dir = new TempDir();
        var (scanner, db) = Create(dir);
        var original = dir.File("roms/Black.iso", Ps2Disc);
        db.UpsertScanned(scanner.Scan([Path.Combine(dir.Path, "roms")]).Roms);
        var game = db.GetGames().Single();
        db.SetFavorite(game.Id, true);
        db.RecordSession(game.Id, DateTimeOffset.UtcNow, 3600);

        Directory.CreateDirectory(Path.Combine(dir.Path, "roms", "ps2"));
        File.Move(original, Path.Combine(dir.Path, "roms", "ps2", "Black.iso"));
        var result = db.UpsertScanned(scanner.Scan([Path.Combine(dir.Path, "roms")]).Roms);

        Assert.Equal(1, result.Moved);
        var moved = db.GetGames().Single();
        Assert.Equal(game.Id, moved.Id);
        Assert.EndsWith(Path.Combine("ps2", "Black.iso"), moved.Path);
        Assert.True(moved.Favorite);
        Assert.Equal(3600, moved.PlaySeconds);
    }

    [Fact]
    public void Duplicates_saved_before_the_fix_are_merged_on_next_scan()
    {
        using var dir = new TempDir();
        var (scanner, db) = Create(dir);
        var a = dir.File("Downloads/ps2/Black.iso", Ps2Disc);
        var b = dir.File("Downloads/Black/Black.iso", Ps2Disc);

        // Como a versão antiga gravava: sem impressão digital, um registro por caminho.
        foreach (var path in new[] { a, b })
            db.UpsertScanned([scanner.Identify(path)! with { Fingerprint = null }]);
        Assert.Equal(2, db.GetGames().Count);
        var second = db.GetGames().Single(g => g.Path == Path.GetFullPath(b));
        db.RecordSession(second.Id, DateTimeOffset.UtcNow, 600);

        var result = db.UpsertScanned(scanner.Scan([Path.Combine(dir.Path, "Downloads")]).Roms);

        var remaining = Assert.Single(db.GetGames());
        Assert.Single(result.Duplicates);
        Assert.Equal(600, remaining.PlaySeconds); // fica o que tem tempo jogado
        Assert.True(File.Exists(a) && File.Exists(b)); // nada apagado do disco
    }
}
