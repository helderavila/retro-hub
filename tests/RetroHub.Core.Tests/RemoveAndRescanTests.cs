using RetroHub.Core.Library;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

public class RemoveAndRescanTests
{
    private static (RomScanner Scanner, LibraryDatabase Db, string Roms) Create(TempDir dir)
    {
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var roms = Path.Combine(dir.Path, "roms");
        dir.File("roms/snes/Chrono Trigger (USA).sfc", [1, 2, 3]);
        dir.File("roms/snes/Super Metroid (USA).sfc", [4, 5, 6]);
        db.AddRomFolder(roms);
        return (new RomScanner(ManifestTests.Load()), db, roms);
    }

    private static void Scan(RomScanner scanner, LibraryDatabase db, string roms)
    {
        db.UpsertScanned(scanner.Scan([roms]).Roms);
        db.RemoveMissingGames(db.GetRomFolders());
    }

    [Fact]
    public void Removed_game_does_not_come_back_until_library_is_reset()
    {
        using var dir = new TempDir();
        var (scanner, db, roms) = Create(dir);
        Scan(scanner, db, roms);

        db.ExcludeGame(db.GetGames().Single(g => g.Title == "Super Metroid").Id);
        Scan(scanner, db, roms);
        Assert.Equal(["Chrono Trigger"], db.GetGames().Select(g => g.Title));
        Assert.True(File.Exists(Path.Combine(roms, "snes", "Super Metroid (USA).sfc"))); // arquivo intacto

        db.ClearLibrary();
        Scan(scanner, db, roms);
        Assert.Equal(2, db.GetGames().Count);
    }

    [Fact]
    public void Deleted_file_leaves_the_library_on_rescan()
    {
        using var dir = new TempDir();
        var (scanner, db, roms) = Create(dir);
        Scan(scanner, db, roms);

        File.Delete(Path.Combine(roms, "snes", "Chrono Trigger (USA).sfc"));
        Scan(scanner, db, roms);

        Assert.Equal(["Super Metroid"], db.GetGames().Select(g => g.Title));
    }

    [Fact]
    public void Disconnected_drive_keeps_games_and_playtime()
    {
        using var dir = new TempDir();
        var (scanner, db, roms) = Create(dir);
        Scan(scanner, db, roms);
        var chrono = db.GetGames().Single(g => g.Title == "Chrono Trigger");
        db.RecordSession(chrono.Id, DateTimeOffset.UtcNow, 7200);

        Directory.Move(roms, roms + "-desconectado"); // pasta de ROMs inteira some (HD externo)
        db.RemoveMissingGames(db.GetRomFolders());

        Assert.Equal(2, db.GetGames().Count);
        Assert.Equal(7200, db.GetGame(chrono.Id)!.PlaySeconds);
    }

    [Fact]
    public void Files_of_a_cue_game_include_its_tracks()
    {
        using var dir = new TempDir();
        dir.File("ps1/Game (Track 1).bin", [1]);
        dir.File("ps1/Game (Track 2).bin", [2]);
        var cue = dir.File("ps1/Game.cue", "FILE \"Game (Track 1).bin\" BINARY\nFILE \"Game (Track 2).bin\" BINARY\n");
        var m3u = dir.File("ps1/Game.m3u", "Game.cue\n");

        Assert.Equal(3, SheetParser.FilesOfGame(cue).Count);
        Assert.Equal(4, SheetParser.FilesOfGame(m3u).Count);
    }
}
