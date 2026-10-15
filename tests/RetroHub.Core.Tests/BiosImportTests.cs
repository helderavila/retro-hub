using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RetroHub.Core.Bios;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;

namespace RetroHub.Core.Tests;

public class BiosImportTests
{
    private static readonly byte[] FakeBios = Enumerable.Range(0, 1024).Select(i => (byte)(i * 7)).ToArray();

    private static (BiosVerifier Verifier, AppPaths Paths) Create(TempDir dir)
    {
        var md5 = Convert.ToHexString(MD5.HashData(FakeBios)).ToLowerInvariant();
        dir.File("manifests/systems.json", $$"""
            { "systems": [
              { "id": "test", "name": "Teste", "shortName": "TST", "extensions": [".tst"], "biosRule": "any",
                "bios": [ { "file": "test_bios.bin", "md5": ["{{md5}}"], "size": 1024 } ] },
              { "id": "ps2", "name": "PlayStation 2", "shortName": "PS2", "extensions": [".iso"], "biosRule": "any",
                "bios": [ { "file": "*.bin", "size": 4194304, "signature": "RESET" } ] }
            ] }
            """);

        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(dir.Path, "manifests") };
        paths.EnsureCreated();
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        return (new BiosVerifier(ManifestRepository.LoadFor(null, paths.Manifests), paths, db), paths);
    }

    [Fact]
    public async Task Imports_by_hash_from_zip_and_renames()
    {
        using var dir = new TempDir();
        var (verifier, paths) = Create(dir);

        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            using (var s = zip.CreateEntry("pasta/nome-qualquer.dat").Open())
                s.Write(FakeBios);
            dir.File("pendrive/backup.zip", ms.ToArray());
        }
        dir.File("pendrive/outro.bin", new byte[1024]); // mesmo tamanho, hash diferente

        var report = await verifier.ImportAsync([Path.Combine(dir.Path, "pendrive")]);

        Assert.Equal(2, report.FilesExamined);
        Assert.Single(report.Imported);
        Assert.Equal("test_bios.bin", report.Imported[0].FileName);
        Assert.Equal(FakeBios, File.ReadAllBytes(Path.Combine(paths.Bios, "test_bios.bin")));
        Assert.True(report.Reports.Single(r => r.System.Id == "test").Satisfied);

        var again = await verifier.ImportAsync([Path.Combine(dir.Path, "pendrive")]);
        Assert.Empty(again.Imported);
        Assert.Equal(1, again.AlreadyPresent);
    }

    [Fact]
    public async Task Ps2_bios_needs_the_romdir_signature()
    {
        using var dir = new TempDir();
        var (verifier, paths) = Create(dir);

        var bios = new byte[4 * 1024 * 1024];
        Encoding.ASCII.GetBytes("RESET").CopyTo(bios, 0x2700);
        dir.File("dumps/SCPH-39001.bin", bios);
        dir.File("dumps/Some Genesis Game.bin", new byte[4 * 1024 * 1024]); // 4 MiB, mas não é BIOS

        var report = await verifier.ImportAsync([Path.Combine(dir.Path, "dumps")]);

        Assert.Equal(["SCPH-39001.bin"], report.Imported.Select(i => i.FileName));
        Assert.False(File.Exists(Path.Combine(paths.Bios, "Some Genesis Game.bin")));
        Assert.True(report.Reports.Single(r => r.System.Id == "ps2").Satisfied);
    }
}
