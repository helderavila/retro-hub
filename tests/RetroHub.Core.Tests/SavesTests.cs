using System.IO.Compression;
using RetroHub.Core.Input;
using RetroHub.Core.Launch;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Saves;

namespace RetroHub.Core.Tests;

public class SavesTests
{
    private sealed record Env(ManifestRepository Manifests, LibraryDatabase Db, AppPaths Paths, string EmuDir);

    /// <summary>Um "PCSX2" e um "RetroArch" falsos, instalados na pasta de teste.</summary>
    private static Env Create(TempDir dir)
    {
        dir.File("manifests/systems.json", """
            { "systems": [
              { "id": "ps2", "name": "PS2", "shortName": "PS2", "extensions": [".iso"] },
              { "id": "snes", "name": "SNES", "shortName": "SNES", "extensions": [".sfc"] } ] }
            """);
        dir.File("manifests/emulators/pcsx2.json", """
            { "id": "pcsx2", "name": "PCSX2", "systems": ["ps2"], "source": { "type": "url", "url": "https://x.invalid/a.7z" },
              "executable": "cmd.exe", "launch": [ { "systems": ["ps2"], "args": "-batch -- \"{rom}\"" } ],
              "saves": { "states": [ { "dir": "sstates", "pattern": "{serial} (*).*.p2s", "slot": "\\.(\\d+)\\.p2s$" } ],
                         "loadStateArgs": "-batch -statefile \"{state}\" -- \"{rom}\"", "backup": ["memcards", "sstates"] } }
            """);
        dir.File("manifests/emulators/retroarch.json", """
            { "id": "retroarch", "name": "RetroArch", "systems": ["snes"], "source": { "type": "url", "url": "https://x.invalid/a.7z" },
              "executable": "cmd.exe", "cores": { "urlTemplate": "x/{core}" },
              "launch": [ { "systems": ["snes"], "core": "snes9x", "args": "-L {core} \"{rom}\"" } ],
              "saves": { "states": [ { "dir": "states", "pattern": "{romName}.state*", "slot": "\\.state(\\d*)$" } ],
                         "loadStateArgs": "-L {core} \"{rom}\" --entryslot={slot}", "backup": ["saves", "states"] } }
            """);

        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(dir.Path, "manifests") };
        paths.EnsureCreated();
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        var emuDir = Path.Combine(dir.Path, "emus");
        var cmd = TestPlatform.Shell;
        foreach (var id in new[] { "pcsx2", "retroarch" })
            db.SaveInstalledEmulator(new InstalledEmulator { Id = id, Version = "1", InstallDir = Path.Combine(emuDir, id), ExecutablePath = cmd, InstalledAt = DateTimeOffset.UtcNow });
        dir.File("emus/retroarch/cores/" + Util.Platform.CoreFileName("snes9x"), [0]);
        return new Env(ManifestRepository.LoadFor(null, paths.Manifests), db, paths, emuDir);
    }

    private static byte[] P2sWithScreenshot()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("Screenshot.png").Open())
            s.Write([0x89, 0x50, 0x4E, 0x47]);
        return ms.ToArray();
    }

    [Fact]
    public void Finds_states_by_serial_and_by_rom_name_newest_first()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var old = dir.File("emus/pcsx2/sstates/SLUS-20946 (1A2B3C4D).01.p2s", P2sWithScreenshot());
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-2));
        dir.File("emus/pcsx2/sstates/SLUS-20946 (1A2B3C4D).03.p2s", P2sWithScreenshot());
        dir.File("emus/pcsx2/sstates/SLUS-99999 (FFFFFFFF).01.p2s", [0]); // outro jogo
        dir.File("emus/retroarch/states/Chrono Trigger (USA).state", [0]);
        dir.File("emus/retroarch/states/Chrono Trigger (USA).state2", [0]);
        dir.File("emus/retroarch/states/Chrono Trigger (USA).state2.png", [0]); // miniatura, não é save

        var service = new SaveStateService(env.Manifests, env.Db);
        var ps2 = service.Find(new Game { SystemId = "ps2", Serial = "SLUS-20946", Path = Path.Combine(dir.Path, "x", "Black.iso") });
        var snes = service.Find(new Game { SystemId = "snes", Path = Path.Combine(dir.Path, "roms", "Chrono Trigger (USA).sfc") });

        Assert.Equal([3, 1], ps2.Select(s => s.Slot));
        Assert.NotNull(SaveStateService.Thumbnail(ps2[0]));
        Assert.Equal([0, 2], snes.Select(s => s.Slot!.Value).Order());
        Assert.NotNull(SaveStateService.Thumbnail(snes.Single(s => s.Slot == 2)));
    }

    [Fact]
    public void Launch_plan_uses_load_state_arguments()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        var state = dir.File("emus/retroarch/states/Chrono.state3", [0]);
        var launcher = new GameLauncher(env.Manifests, env.Db, new ControllerConfigurator(env.Db));
        var rom = Path.Combine(dir.Path, "roms", "Chrono.sfc");
        var game = new Game { SystemId = "snes", Path = rom };

        var normal = launcher.Plan(game);
        var resumed = launcher.Plan(game, new SaveState(state, "retroarch", "RetroArch", 3, DateTimeOffset.Now, 1, true));

        Assert.Equal($"-L snes9x \"{rom}\"", normal.Arguments);
        Assert.Equal($"-L snes9x \"{rom}\" --entryslot=3", resumed.Arguments);
    }

    [Fact]
    public async Task Backup_zips_saves_of_every_emulator()
    {
        using var dir = new TempDir();
        var env = Create(dir);
        dir.File("emus/pcsx2/memcards/Mcd001.ps2", [1, 2, 3]);
        dir.File("emus/pcsx2/sstates/SLUS-20946 (X).01.p2s", [4]);
        dir.File("emus/retroarch/saves/Chrono Trigger (USA).srm", [5]);
        dir.File("emus/pcsx2/bios/scph.bin", [6]); // BIOS não entra no backup

        var backup = new BackupService(env.Paths, env.Manifests, env.Db);
        Assert.True(backup.IsDue);
        var result = await backup.CreateAsync();

        using var zip = ZipFile.OpenRead(result!.ZipPath);
        Assert.Equal(
            ["pcsx2/memcards/Mcd001.ps2", "pcsx2/sstates/SLUS-20946 (X).01.p2s", "retroarch/saves/Chrono Trigger (USA).srm"],
            zip.Entries.Select(e => e.FullName).Order());
        Assert.False(backup.IsDue);
    }
}
