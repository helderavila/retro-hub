using RetroHub.Core.Bios;
using RetroHub.Core.Install;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

/// <summary>Sistemas e emuladores adicionados pelo usuário (pasta de manifestos dele, fora do app).</summary>
public class UserManifestTests
{
    private const string HandheldSystem = """
        {
          // comentário vale, como nos manifestos do app
          "systems": [
            { "id": "portatil", "name": "Portátil de Teste", "shortName": "PTL", "extensions": [".ptl"],
              "folderAliases": ["portatil"], "defaultEmulator": "meu-emu", "biosRule": "all",
              "bios": [ { "file": "console.keys", "description": "Chaves do console" } ] }
          ]
        }
        """;

    private static (AppPaths Paths, ManifestRepository Manifests, LibraryDatabase Db) Create(TempDir dir)
    {
        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(AppContext.BaseDirectory, "manifests") };
        paths.EnsureCreated();
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        return (paths, ManifestRepository.LoadFor(null, paths.Manifests, paths.UserManifests), db);
    }

    [Fact]
    public void User_systems_and_emulators_are_added_and_override_the_shipped_ones()
    {
        using var dir = new TempDir();
        var (paths, manifests, _) = Create(dir);
        dir.File("data/manifests/systems/portatil.json", HandheldSystem);
        dir.File("data/manifests/emulators/ppsspp.json", """{ "id": "ppsspp", "name": "Meu PPSSPP", "systems": ["psp"], "source": { "type": "local", "path": "C:\\x\\ppsspp.exe" }, "executable": "ppsspp.exe", "launch": [ { "systems": ["psp"] } ] }""");
        dir.File("data/manifests/emulators/quebrado.json", "{ isto não é json");

        manifests.Reload();

        Assert.Equal("Portátil de Teste", manifests.GetSystem("portatil")!.Name);
        Assert.Equal("Meu PPSSPP", manifests.GetEmulator("ppsspp")!.Name);
        Assert.True(manifests.GetEmulator("ppsspp")!.IsCustom);
        Assert.False(manifests.GetEmulator("pcsx2")!.IsCustom);
        Assert.Single(manifests.Emulators, e => e.Id == "ppsspp");
        Assert.Contains(manifests.Warnings, w => w.Contains("quebrado.json")); // arquivo inválido vira aviso, não derruba o app
    }

    [Fact]
    public void Title_id_in_brackets_becomes_the_serial()
    {
        using var dir = new TempDir();
        var (_, manifests, _) = Create(dir);
        dir.File("data/manifests/systems/portatil.json", HandheldSystem);
        manifests.Reload();
        var rom = dir.File("roms/Um Jogo [0100F2C0115B6000][v0].ptl", new byte[64]);

        var scanned = new RomScanner(manifests).Identify(rom)!;

        Assert.Equal("portatil", scanned.SystemId);
        Assert.Equal("0100F2C0115B6000", scanned.Serial);
        Assert.Equal("Um Jogo", scanned.Title);
    }

    [Fact]
    public async Task Key_files_are_imported_by_exact_name()
    {
        using var dir = new TempDir();
        var (paths, manifests, db) = Create(dir);
        dir.File("data/manifests/systems/portatil.json", HandheldSystem);
        manifests.Reload();
        dir.File("pendrive/console.keys", "key_00 = 11\n");
        dir.File("pendrive/notas.txt", "nada");

        var report = await new BiosVerifier(manifests, paths, db).ImportAsync([Path.Combine(dir.Path, "pendrive")]);

        Assert.Contains(report.Imported, i => i.FileName == "console.keys" && i.System == "PTL");
        Assert.True(report.Reports.Single(r => r.System.Id == "portatil").Satisfied);
    }

    [Fact]
    public async Task Local_emulator_is_registered_without_copying_and_removal_keeps_its_folder()
    {
        using var dir = new TempDir();
        var (paths, manifests, db) = Create(dir);
        var exe = dir.File("Meus Emuladores/Emu PSP/emu.exe", "MZ");
        var custom = new CustomEmulators(paths, manifests, db);
        var installer = new EmulatorInstaller(paths, new HttpClient(), new DownloadSourceResolver(new HttpClient()), new DependencyInstaller(), manifests, db);

        var manifest = custom.CreateLocal("Emu PSP", exe, ["psp"], "\"{rom}\" --fullscreen");
        var installed = await installer.InstallAsync(manifest, new Progress<InstallUpdate>(), default);

        Assert.Equal("emu-psp", manifest.Id);
        Assert.True(manifest.IsCustom);
        Assert.Equal(exe, installed.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(exe), installed.InstallDir);
        Assert.False(Directory.Exists(paths.EmulatorDir("emu-psp"))); // nada copiado para a pasta do Retro Hub
        Assert.Contains(manifests.EmulatorsForSystem("psp"), e => e.Id == "emu-psp");

        custom.Remove(manifest, installer);

        Assert.Null(manifests.GetEmulator("emu-psp"));
        Assert.DoesNotContain(db.GetInstalledEmulators(), e => e.Id == "emu-psp");
        Assert.True(File.Exists(exe)); // o emulador do usuário fica intacto
    }

    [Fact]
    public void Creating_needs_rom_placeholder_and_known_systems()
    {
        using var dir = new TempDir();
        var (paths, manifests, db) = Create(dir);
        var exe = dir.File("emu/emu.exe", "MZ");
        var custom = new CustomEmulators(paths, manifests, db);

        Assert.Throws<ArgumentException>(() => custom.CreateLocal("Emu", exe, ["psp"], "--fullscreen"));
        Assert.Throws<ArgumentException>(() => custom.CreateLocal("Emu", exe, ["nao-existe"], "\"{rom}\""));
        Assert.Throws<ArgumentException>(() => custom.CreateLocal("Emu", exe, [], "\"{rom}\""));
    }

    [Fact]
    public void Imported_manifest_must_have_a_safe_id()
    {
        using var dir = new TempDir();
        var (paths, manifests, db) = Create(dir);
        var custom = new CustomEmulators(paths, manifests, db);
        var bad = dir.File("bad.json", """{ "id": "..\\..\\x", "name": "X", "systems": ["psp"], "executable": "x.exe" }""");
        var good = dir.File("good.json", """{ "id": "emu-novo", "name": "Emu Novo", "systems": ["psp"], "source": { "type": "url", "url": "https://example.com/emu.zip" }, "executable": "emu.exe", "launch": [ { "systems": ["psp"] } ] }""");

        Assert.Throws<InvalidDataException>(() => custom.Import(bad));
        Assert.Equal("Emu Novo", custom.Import(good).Name);
        Assert.True(File.Exists(Path.Combine(paths.UserManifests, "emulators", "emu-novo.json")));
    }

    [Fact]
    public void Update_keeps_the_preserved_user_folder()
    {
        using var dir = new TempDir();
        dir.File("nova/emu.exe", "novo");
        dir.File("nova/user/config.ini", "padrão");
        dir.File("instalado/emu.exe", "velho");
        dir.File("instalado/user/config.ini", "minha configuração");
        dir.File("instalado/user/save/save.bin", "meu save");

        ArchiveExtractor.CopyDirectory(Path.Combine(dir.Path, "nova"), Path.Combine(dir.Path, "instalado"), ["user"]);

        Assert.Equal("novo", File.ReadAllText(Path.Combine(dir.Path, "instalado", "emu.exe")));
        Assert.Equal("minha configuração", File.ReadAllText(Path.Combine(dir.Path, "instalado", "user", "config.ini")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "instalado", "user", "save", "save.bin")));
    }
}
