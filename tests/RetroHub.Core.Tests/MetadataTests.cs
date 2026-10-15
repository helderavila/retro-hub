using System.Security.Cryptography;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Metadata;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

public class MetadataTests
{
    private static readonly byte[] Rom = Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray();

    private static string Crc(byte[] data)
    {
        var crc = new System.IO.Hashing.Crc32();
        crc.Append(data);
        return crc.GetCurrentHashAsUInt32().ToString("X8");
    }

    /// <summary>Monta um "sistema de teste" com .dat em cache local — nenhum acesso à internet.</summary>
    private static (MetadataService Service, LibraryDatabase Db, AppPaths Paths) Create(TempDir dir)
    {
        dir.File("manifests/systems.json", """
            { "systems": [ { "id": "nes", "name": "NES", "shortName": "NES", "extensions": [".nes"],
                             "thumbnails": "Test - System", "dat": "no-intro" } ] }
            """);
        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(dir.Path, "manifests") };
        paths.EnsureCreated();

        var sha1 = Convert.ToHexString(SHA1.HashData(Rom));
        dir.File("data/metadata/no-intro/Test - System.dat", $$"""
            clrmamepro ( name "Test - System" version "1" )
            game (
                name "Super Test Bros. (USA)"
                region "USA"
                rom ( name "Super Test Bros. (USA).nes" size {{Rom.Length}} crc {{Crc(Rom)}} sha1 {{sha1}} )
            )
            """);
        dir.File("data/metadata/developer/Test - System.dat", $$"""
            game ( comment "Super Test Bros. (USA)" developer "Test Studio \"TS\"" rom ( crc {{Crc(Rom)}} ) )
            """);
        dir.File("data/metadata/genre/Test - System.dat", $$"""
            game ( comment "Super Test Bros. (USA)" genre "Platform" rom ( crc {{Crc(Rom)}} ) )
            """);
        foreach (var missing in new[] { "publisher", "releaseyear" })
            dir.File($"data/metadata/{missing}/Test - System.dat.404", "");

        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        var service = new MetadataService(new HttpClient(), paths, ManifestRepository.Load(paths.Manifests), db);
        return (service, db, paths);
    }

    private static Game AddGame(LibraryDatabase db, string path, string title, string? crc = null)
    {
        db.UpsertScanned([new Scanning.ScannedRom(path, "nes", title, null, crc, new FileInfo(path).Length)]);
        return db.GetGames().Single(g => g.Path == path);
    }

    [Fact]
    public void Parses_clrmamepro_with_nested_roms_and_escaped_quotes()
    {
        var entries = DatParser.Parse("""
            clrmamepro ( name "x" )
            game ( name "A (USA)" serial "SLUS-00001" rom ( name "A.iso" size 10 crc ABCD1234 ) rom ( name "A2.bin" size 5 crc 00000001 ) )
            game ( comment "B" description "Diz \"oi\"" )
            """);
        Assert.Equal(2, entries.Count);
        Assert.Equal("A (USA)", entries[0].Name);
        Assert.Equal(2, entries[0].Roms.Count);
        Assert.Equal("ABCD1234", entries[0].Roms[0]["crc"]);
        Assert.Equal("B", entries[1].Name);
        Assert.Equal("Diz \"oi\"", entries[1].Get("description"));
    }

    [Fact]
    public async Task Finds_info_by_crc_even_with_a_bad_file_name()
    {
        using var dir = new TempDir();
        var (service, db, _) = Create(dir);
        var game = AddGame(db, dir.File("roms/stb.nes", Rom), "stb", Crc(Rom).ToLowerInvariant());

        var info = await service.LookupAsync(game);

        Assert.Equal("Super Test Bros. (USA)", info!.Name);
        Assert.Equal("Test Studio \"TS\"", info.Developer);
        Assert.Equal("Platform", info.Genre);
        Assert.Equal("USA", info.Region);
    }

    [Fact]
    public async Task Verifies_exact_dump_ignoring_ines_header()
    {
        using var dir = new TempDir();
        var (service, db, _) = Create(dir);
        var withHeader = "NES\u001a"u8.ToArray().Concat(new byte[12]).Concat(Rom).ToArray();
        var game = AddGame(db, dir.File("roms/Super Test Bros.nes", withHeader), "Super Test Bros");

        var result = await service.VerifyAsync(game);

        Assert.Equal("ok", result.VerifyStatus);
        Assert.Equal("Super Test Bros. (USA)", result.Name);
        Assert.Equal("ok", GameMetadata.FromJson(db.GetGame(game.Id)!.MetadataJson)!.VerifyStatus); // gravado no banco
    }

    [Fact]
    public async Task Modified_file_is_reported_as_unknown()
    {
        using var dir = new TempDir();
        var (service, db, _) = Create(dir);
        var hacked = Rom.ToArray();
        hacked[100] ^= 0xFF;
        var game = AddGame(db, dir.File("roms/Super Test Bros (Hack).nes", hacked), "Super Test Bros");

        Assert.Equal("unknown", (await service.VerifyAsync(game)).VerifyStatus);
    }
}
