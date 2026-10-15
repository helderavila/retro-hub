using System.IO.Compression;
using RetroHub.Core.Install;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

/// <summary>Extrair jogos .zip/.7z para a pasta de jogos sem perder tempo jogado, favoritos e coleções.</summary>
public class GameUnpackerTests
{
    private sealed record Env(ManifestRepository Manifests, LibraryDatabase Db, GameUnpacker Unpacker, string GamesRoot);

    private static Env Create(TempDir dir)
    {
        dir.File("manifests/systems.json", """
            { "systems": [
              { "id": "ps1", "name": "PlayStation", "shortName": "PS1", "extensions": [".iso", ".cue", ".bin"], "folderAliases": ["ps1", "psx"], "defaultEmulator": "duck" },
              { "id": "ps2", "name": "PlayStation 2", "shortName": "PS2", "extensions": [".iso"], "folderAliases": ["ps2"], "defaultEmulator": "duck" },
              { "id": "snes", "name": "SNES", "shortName": "SNES", "extensions": [".sfc"], "folderAliases": ["snes"], "defaultEmulator": "ra" },
              { "id": "arcade", "name": "Arcade", "shortName": "ARC", "extensions": [".zip"], "folderAliases": ["arcade"], "defaultEmulator": "ra" } ] }
            """);
        dir.File("manifests/emulators/duck.json", """
            { "id": "duck", "name": "Duck", "systems": ["ps1", "ps2"], "source": { "type": "url", "url": "https://x.invalid/a.zip" },
              "executable": "duck", "launch": [ { "systems": ["ps1", "ps2"] } ] }
            """);
        dir.File("manifests/emulators/ra.json", """
            { "id": "ra", "name": "RA", "systems": ["snes", "arcade"], "opensArchives": true, "source": { "type": "url", "url": "https://x.invalid/a.zip" },
              "executable": "ra", "launch": [ { "systems": ["snes", "arcade"] } ] }
            """);
        var manifests = ManifestRepository.LoadFor(null, Path.Combine(dir.Path, "manifests"));
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        return new Env(manifests, db, new GameUnpacker(manifests, db, new RomScanner(manifests)), Path.Combine(dir.Path, "Games"));
    }

    private static byte[] Ps1Iso() => TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT = cdrom:\\SCUS_944.55;1\r\n");

    private static string Zip(TempDir dir, string name, params (string Entry, byte[] Content)[] entries)
    {
        var path = Path.Combine(dir.Path, "downloads", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(content);
        }
        return path;
    }

    /// <summary>Adds the archive to the library as a scan would, with play time and a favorite.</summary>
    private static Game AddPlayed(Env env, string path, string systemId)
    {
        env.Db.UpsertScanned([new ScannedRom(path, systemId, Path.GetFileNameWithoutExtension(path), null, null, new FileInfo(path).Length, "fp:" + path)]);
        var game = env.Db.GetGames().Single(g => g.Path == path);
        env.Db.RecordSession(game.Id, DateTimeOffset.UtcNow.AddHours(-1), 1800);
        env.Db.SetFavorite(game.Id, true);
        return env.Db.GetGame(game.Id)!;
    }

    private sealed class Lines : IProgress<InstallUpdate>
    {
        public List<string> All { get; } = [];
        public void Report(InstallUpdate value) { if (value.Log is { } l) All.Add(l); }
    }

    [Fact]
    public void Advice_follows_the_system_and_its_emulator()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var disc = AddPlayed(env, Zip(dir, "Disc.zip", ("Disc.iso", Ps1Iso())), "ps1");
        var cart = AddPlayed(env, Zip(dir, "Cart.zip", ("Cart.sfc", new byte[512])), "snes");
        var arcade = AddPlayed(env, Zip(dir, "mslug.zip", ("201-p1.p1", new byte[16])), "arcade");

        Assert.Equal(UnpackAdvice.Recommended, env.Unpacker.Describe(disc, env.GamesRoot).Advice);
        Assert.Equal(UnpackAdvice.Optional, env.Unpacker.Describe(cart, env.GamesRoot).Advice);
        Assert.Equal(UnpackAdvice.KeepCompressed, env.Unpacker.Describe(arcade, env.GamesRoot).Advice);
        Assert.Equal(Path.Combine(env.GamesRoot, "ps1"), env.Unpacker.Describe(disc, env.GamesRoot).TargetDir);
    }

    [Fact]
    public async Task Disc_goes_to_the_system_folder_and_keeps_play_time_and_favorite()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Crash Bandicoot (USA).zip", ("Crash Bandicoot (USA).iso", Ps1Iso()));
        var game = AddPlayed(env, zip, "ps1");
        var trashed = new List<string>();

        var results = await env.Unpacker.UnpackAsync([game], env.GamesRoot, trashed.Add, new Lines(), CancellationToken.None);

        var expected = Path.Combine(env.GamesRoot, "ps1", "Crash Bandicoot (USA).iso");
        Assert.Equal(expected, results.Single().NewPath);
        Assert.True(File.Exists(expected));
        var after = env.Db.GetGame(game.Id)!;
        Assert.Equal(expected, after.Path);
        Assert.Equal(1800, after.PlaySeconds);
        Assert.True(after.Favorite);
        Assert.Equal("SCUS-94455", after.Serial);
        Assert.Equal([zip], trashed);
        Assert.Contains(env.GamesRoot, env.Db.GetRomFolders());
        Assert.Empty(Directory.GetDirectories(Path.Combine(env.GamesRoot, "ps1"), ".retrohub-unpack-*"));
    }

    [Fact]
    public async Task Cue_and_tracks_go_to_a_folder_and_the_library_opens_the_cue()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var cue = "FILE \"Game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\nFILE \"Game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n";
        var game = AddPlayed(env, Zip(dir, "Game.zip",
            ("Game.cue", System.Text.Encoding.ASCII.GetBytes(cue)), ("Game (Track 1).bin", Ps1Iso()), ("Game (Track 2).bin", new byte[4096]),
            ("__MACOSX/._Game.cue", new byte[8])), "ps1");

        var results = await env.Unpacker.UnpackAsync([game], env.GamesRoot, null, new Lines(), CancellationToken.None);

        var folder = Path.Combine(env.GamesRoot, "ps1", "Game");
        Assert.Equal(Path.Combine(folder, "Game.cue"), results.Single().NewPath);
        Assert.Equal(3, Directory.GetFiles(folder).Length);
        Assert.False(Directory.Exists(Path.Combine(folder, "__MACOSX")));
    }

    [Fact]
    public async Task A_kept_archive_does_not_come_back_on_the_next_scan()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Disc.zip", ("Disc.iso", Ps1Iso()));
        var game = AddPlayed(env, zip, "ps1");

        await env.Unpacker.UnpackAsync([game], env.GamesRoot, removeOriginal: null, new Lines(), CancellationToken.None);
        var rescan = env.Db.UpsertScanned([new ScannedRom(zip, "ps1", "Disc", null, null, 1, "fp:other")]);

        Assert.True(File.Exists(zip));
        Assert.Equal(0, rescan.Added);
        Assert.Single(env.Db.GetGames());
    }

    [Fact]
    public async Task An_archive_without_a_game_is_reported_and_leaves_nothing_behind()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Readme.zip", ("readme.txt", new byte[10]));
        var game = AddPlayed(env, zip, "ps1");

        var results = await env.Unpacker.UnpackAsync([game], env.GamesRoot, _ => throw new InvalidOperationException("must not trash"), new Lines(), CancellationToken.None);

        Assert.Null(results.Single().NewPath);
        Assert.NotNull(results.Single().Error);
        Assert.Equal(zip, env.Db.GetGame(game.Id)!.Path);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(env.GamesRoot, "ps1")));
    }

    [Fact]
    public async Task Arcade_sets_are_never_extracted()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "mslug.zip", ("201-p1.p1", new byte[16]));
        var game = AddPlayed(env, zip, "arcade");

        var results = await env.Unpacker.UnpackAsync([game], env.GamesRoot, null, new Lines(), CancellationToken.None);

        Assert.NotNull(results.Single().Error);
        Assert.Equal(zip, env.Db.GetGame(game.Id)!.Path);
    }

    [Fact]
    public async Task Cancelling_leaves_the_archive_and_the_library_as_they_were()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Disc.zip", ("Disc.iso", Ps1Iso()));
        var game = AddPlayed(env, zip, "ps1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Unpacker.UnpackAsync([game], env.GamesRoot, null, new Lines(), cts.Token));

        Assert.Equal(zip, env.Db.GetGame(game.Id)!.Path);
        Assert.True(File.Exists(zip));
        Assert.False(Directory.Exists(Path.Combine(env.GamesRoot, "ps1")) && Directory.EnumerateFileSystemEntries(Path.Combine(env.GamesRoot, "ps1")).Any());
    }

    [Fact]
    public async Task A_name_already_taken_gets_a_number()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        dir.File("Games/ps1/Disc.iso", [1, 2, 3]); // something else with the same name
        var game = AddPlayed(env, Zip(dir, "Disc.zip", ("Disc.iso", Ps1Iso())), "ps1");

        var results = await env.Unpacker.UnpackAsync([game], env.GamesRoot, null, new Lines(), CancellationToken.None);

        Assert.Equal(Path.Combine(env.GamesRoot, "ps1", "Disc (2).iso"), results.Single().NewPath);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(env.GamesRoot, "ps1", "Disc.iso")));
    }

    [Fact]
    public async Task A_picked_archive_is_identified_by_the_disc_inside_and_added_to_the_library()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Spyro (USA).zip", ("Spyro (USA).iso", Ps1Iso())); // .iso alone could be PS1 or PS2

        Assert.Equal("", env.Unpacker.DescribeFile(zip, env.GamesRoot).Game.SystemId);
        var results = await env.Unpacker.UnpackAsync([new UnpackItem(zip, null, null)], env.GamesRoot, null, new Lines(), CancellationToken.None);

        var expected = Path.Combine(env.GamesRoot, "ps1", "Spyro (USA).iso");
        Assert.Equal(expected, results.Single().NewPath);
        var game = env.Db.GetGames().Single();
        Assert.Equal(expected, game.Path);
        Assert.Equal("ps1", game.SystemId);
        Assert.Equal("manual", game.Source);
    }

    [Fact]
    public async Task The_user_can_pick_the_system_when_it_cannot_be_detected()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Homebrew.zip", ("Homebrew.iso", new byte[64 * 1024]));

        var results = await env.Unpacker.UnpackAsync([new UnpackItem(zip, null, "ps2")], env.GamesRoot, null, new Lines(), CancellationToken.None);

        Assert.Equal(Path.Combine(env.GamesRoot, "ps2", "Homebrew.iso"), results.Single().NewPath);
        Assert.Equal("ps2", env.Db.GetGames().Single().SystemId);
    }

    [Fact]
    public async Task An_undetectable_archive_asks_for_the_system_and_leaves_nothing_behind()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var zip = Zip(dir, "Mystery.zip", ("Mystery.iso", new byte[64 * 1024]));

        var results = await env.Unpacker.UnpackAsync([new UnpackItem(zip, null, null)], env.GamesRoot, _ => throw new InvalidOperationException("must not trash"), new Lines(), CancellationToken.None);

        Assert.NotNull(results.Single().Error);
        Assert.Empty(env.Db.GetGames());
        Assert.True(File.Exists(zip));
        Assert.Empty(Directory.EnumerateFileSystemEntries(env.GamesRoot));
    }
}
