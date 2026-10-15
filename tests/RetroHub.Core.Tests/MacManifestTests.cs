using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

/// <summary>Os manifestos enviados com o app, vistos pelo Mac (bloco "macos" aplicado).</summary>
public class MacManifestTests
{
    private static ManifestRepository Load() =>
        ManifestRepository.LoadFor("macos", Path.Combine(AppContext.BaseDirectory, "manifests"));

    [Fact]
    public void Shipped_manifests_load_on_mac_without_warnings()
    {
        var repo = Load();
        Assert.Empty(repo.Warnings);
        Assert.Equal(5, repo.Emulators.Count);
    }

    [Fact]
    public void Every_system_has_an_emulator_on_mac()
    {
        var repo = Load();
        foreach (var system in repo.Systems)
            Assert.Contains(repo.EmulatorsForSystem(system.Id), e => e.Launch.Any(p => p.Systems.Contains(system.Id)));
    }

    [Fact]
    public void Mac_downloads_are_app_bundles_without_windows_leftovers()
    {
        foreach (var emulator in Load().Emulators)
        {
            Assert.Contains(".app/Contents/MacOS/", emulator.Executable);
            Assert.Empty(emulator.Dependencies); // no winget on macOS
            Assert.DoesNotContain(emulator.Launch, p => p.Args.Contains(".dll") || p.Args.Contains('\\'));
            Assert.DoesNotContain(".exe", emulator.Source.Asset ?? emulator.Source.Url ?? "");
            Assert.DoesNotContain("windows", emulator.Source.Asset ?? emulator.Source.Url ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Data_dir_moves_config_saves_and_bios_under_the_redirected_home()
    {
        var duck = Load().GetEmulator("duckstation")!;
        var root = Path.Combine("home", "Library", "Application Support", "DuckStation");

        Assert.Equal("{emuDir}/home", duck.Environment["HOME"]);
        Assert.Equal(Path.Combine(root, "settings.ini"), duck.Controller!.File);
        Assert.Equal(Path.Combine(root, "settings.ini"), duck.Graphics!.File);
        Assert.Equal(Path.Combine(root, "settings.ini"), duck.Achievements!.File);
        Assert.Equal(Path.Combine(root, "bios"), duck.BiosDir);
        Assert.All(duck.Saves!.States, s => Assert.StartsWith(root, s.Dir));
        Assert.All(duck.Saves.Backup, b => Assert.StartsWith(root, b));
    }

    [Fact]
    public void Windows_view_is_unchanged_by_the_mac_blocks()
    {
        var windows = ManifestTests.Load();
        var duck = windows.GetEmulator("duckstation")!;
        Assert.Equal("settings.ini", duck.Controller!.File);
        Assert.Empty(duck.Environment);
        Assert.Equal("retroarch.exe", windows.GetEmulator("retroarch")!.Executable);
    }

    [Fact]
    public void Dolphin_maps_controllers_by_sdl_name()
    {
        using var dir = new TempDir();
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var dolphin = Load().GetEmulator("dolphin")!;
        var pads = new IGamepadDevice[] { new SdlGamepadDevice(0, "0300aaaa", "Xbox Wireless Controller") };
        var configurator = new ControllerConfigurator(db) { Devices = () => pads };

        var settings = configurator.Resolve(dolphin.Controller!)!;

        Assert.Equal("SDL/0/Xbox Wireless Controller", settings["GCPad1"]["Device"]);
        Assert.False(settings["GCPad2"].ContainsKey("Device")); // player 2 has no controller: its device is left as is
    }

    [Fact]
    public async Task Launch_passes_the_manifest_environment()
    {
        if (OperatingSystem.IsWindows())
            return; // uses /bin/sh

        using var dir = new TempDir();
        dir.File("manifests/systems.json", """{ "systems": [ { "id": "nes", "name": "NES", "shortName": "NES", "extensions": [".nes"] } ] }""");
        dir.File("manifests/emulators/fake.json", """
            { "id": "fake", "name": "Fake", "systems": ["nes"], "source": { "type": "url", "url": "https://example.invalid/x.zip" },
              "executable": "sh", "environment": { "HOME": "{emuDir}/home" },
              "launch": [ { "systems": ["nes"], "args": "-c \"echo $HOME > home.txt\" {rom}" } ] }
            """);
        var manifests = ManifestRepository.LoadFor(null, Path.Combine(dir.Path, "manifests"));
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        db.SaveInstalledEmulator(new InstalledEmulator { Id = "fake", Version = "1", InstallDir = dir.Path, ExecutablePath = "/bin/sh", InstalledAt = DateTimeOffset.UtcNow });
        var rom = dir.File("game.nes", new byte[16]);

        await new Launch.GameLauncher(manifests, db, new ControllerConfigurator(db)).LaunchAsync(new Game { Id = 1, SystemId = "nes", Title = "G", Path = rom });

        Assert.Equal(Path.Combine(dir.Path, "home"), File.ReadAllText(Path.Combine(dir.Path, "home.txt")).Trim());
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "home")));
    }
}
