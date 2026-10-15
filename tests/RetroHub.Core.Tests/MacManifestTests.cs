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

    [Fact]
    public void Retroarch_on_mac_uses_parallel_rdp_on_vulkan_for_n64()
    {
        var ra = Load().GetEmulator("retroarch")!;
        var options = ra.Defaults.Single(d => d.File.EndsWith("Mupen64Plus-Next.opt"));
        var overrideCfg = ra.Defaults.Single(d => d.File.EndsWith("Mupen64Plus-Next.cfg"));
        Assert.Equal("parallel", options.Settings[""]["mupen64plus-rdp-plugin"]);
        Assert.Equal("vulkan", overrideCfg.Settings[""]["video_driver"]);
        Assert.DoesNotContain(ManifestTests.Load().GetEmulator("retroarch")!.Defaults, d => d.File.Contains("Mupen64Plus")); // Windows keeps GLideN64
    }

    [Fact]
    public void Defaults_are_written_once_and_a_later_user_change_is_kept()
    {
        using var dir = new TempDir();
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var ra = Load().GetEmulator("retroarch")!;
        var installed = new InstalledEmulator { Id = "retroarch", Version = "1", InstallDir = dir.Path, InstalledAt = DateTimeOffset.UtcNow };
        var opt = dir.File("config/Mupen64Plus-Next/Mupen64Plus-Next.opt", "mupen64plus-rdp-plugin = \"gliden64\"\nmupen64plus-43screensize = \"640x480\"\n");
        var defaults = new Install.EmulatorDefaults(db);

        Assert.True(defaults.EnsureApplied(ra, installed));
        var text = File.ReadAllText(opt);
        Assert.Contains("mupen64plus-rdp-plugin = \"parallel\"", text);
        Assert.Contains("mupen64plus-43screensize = \"640x480\"", text); // other options untouched
        Assert.Contains("video_driver = \"vulkan\"", File.ReadAllText(Path.Combine(dir.Path, "config/Mupen64Plus-Next/Mupen64Plus-Next.cfg")));

        File.WriteAllText(opt, "mupen64plus-rdp-plugin = \"angrylion\"\n");
        Assert.False(defaults.EnsureApplied(ra, installed));
        Assert.Contains("angrylion", File.ReadAllText(opt));
    }

    public static TheoryData<string?> Platforms => new() { null, "macos" };

    /// <summary>Straight into the game: every emulator writes its quiet-launch settings, and the Qt ones start without their main window.</summary>
    [Theory]
    [MemberData(nameof(Platforms))]
    public void Every_emulator_launches_quietly(string? platform)
    {
        var repo = ManifestRepository.LoadFor(platform, Path.Combine(AppContext.BaseDirectory, "manifests"));
        string Value(string id, string section, string key) =>
            repo.GetEmulator(id)!.Defaults.Where(d => d.Settings.ContainsKey(section) && d.Settings[section].ContainsKey(key))
                .Select(d => d.Settings[section][key]).Single();

        Assert.Equal("false", Value("retroarch", "", "video_font_enable"));
        Assert.Equal("false", Value("duckstation", "Display", "ShowOSDMessages"));
        Assert.Equal("1", Value("pcsx2", "UI", "SettingsVersion"));
        Assert.Equal("-1", Value("ppsspp", "General", "NotificationPos"));
        Assert.Equal("True", Value("dolphin", "Analytics", "PermissionAsked"));
        Assert.All(new[] { "duckstation", "pcsx2" }, id => Assert.StartsWith("-nogui ", repo.GetEmulator(id)!.Launch.Single().Args));
        Assert.Contains("--escape-exit", repo.GetEmulator("ppsspp")!.Launch.Single().Args);
    }

    [Fact]
    public void Mac_quiet_settings_land_in_the_mac_config_files()
    {
        var mac = Load();
        Assert.Contains(mac.GetEmulator("retroarch")!.Defaults, d => d.File == "config/retroarch.cfg");
        Assert.Contains(mac.GetEmulator("ppsspp")!.Defaults, d => d.File == Path.Combine("home/.config/ppsspp", "PSP/SYSTEM/ppsspp.ini"));
        Assert.Contains(mac.GetEmulator("duckstation")!.Defaults, d => d.File == Path.Combine("home", "Library", "Application Support", "DuckStation", "settings.ini"));
    }
}
