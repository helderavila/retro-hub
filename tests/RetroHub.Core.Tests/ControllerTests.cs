using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

public class ControllerTests
{
    [Fact]
    public void Merge_changes_only_mapped_keys_and_keeps_the_rest()
    {
        using var dir = new TempDir();
        var ini = dir.File("inis/PCSX2.ini", """
            ; configuração do usuário
            [EmuCore/GS]
            upscale_multiplier = 3

            [Pad1]
            Type = DualShock2
            Cross = Keyboard/Z
            Deadzone = 0.15

            [Hotkeys]
            ToggleFullscreen = Keyboard/Alt & Keyboard/Return
            """);

        IniMerger.Merge(ini, "ini", new Dictionary<string, Dictionary<string, string>>
        {
            ["Pad1"] = new() { ["Cross"] = "SDL-0/FaceSouth", ["Circle"] = "SDL-0/FaceEast" },
            ["InputSources"] = new() { ["SDL"] = "true" },
        });

        var lines = File.ReadAllLines(ini);
        Assert.Contains("; configuração do usuário", lines);
        Assert.Contains("upscale_multiplier = 3", lines);
        Assert.Contains("Deadzone = 0.15", lines);                      // chave do usuário preservada
        Assert.Contains("Cross = SDL-0/FaceSouth", lines);               // substituída no lugar
        Assert.DoesNotContain("Cross = Keyboard/Z", lines);
        Assert.Contains("ToggleFullscreen = Keyboard/Alt & Keyboard/Return", lines);

        // Chave nova entra dentro de [Pad1], antes de [Hotkeys].
        Assert.True(Array.IndexOf(lines, "Circle = SDL-0/FaceEast") < Array.IndexOf(lines, "[Hotkeys]"));
        Assert.True(Array.IndexOf(lines, "[InputSources]") > Array.IndexOf(lines, "[Hotkeys]"));
    }

    [Fact]
    public void Merge_flat_format_for_retroarch_cfg()
    {
        using var dir = new TempDir();
        var cfg = dir.File("retroarch.cfg", "video_fullscreen = \"true\"\ninput_joypad_driver = \"dinput\"\n");

        IniMerger.Merge(cfg, "flat", new Dictionary<string, Dictionary<string, string>>
        {
            [""] = new() { ["input_joypad_driver"] = "xinput", ["input_autodetect_enable"] = "true" },
        });

        Assert.Equal(["video_fullscreen = \"true\"", "input_joypad_driver = \"xinput\"", "input_autodetect_enable = \"true\""],
            File.ReadAllLines(cfg));
    }

    [Fact]
    public void Configures_once_and_respects_later_user_remaps()
    {
        using var dir = new TempDir();
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var configurator = new ControllerConfigurator(db);

        var manifest = new EmulatorManifest
        {
            Id = "duckstation",
            Controller = new ControllerConfig
            {
                File = "settings.ini",
                Settings = new() { ["Pad1"] = new() { ["Cross"] = "SDL-0/FaceSouth" } },
            },
        };
        var installed = new InstalledEmulator { Id = "duckstation", InstallDir = Path.Combine(dir.Path, "duck") };
        var ini = Path.Combine(installed.InstallDir, "settings.ini");

        Assert.True(configurator.EnsureConfigured(manifest, installed));
        Assert.Contains("Cross = SDL-0/FaceSouth", File.ReadAllLines(ini));

        File.WriteAllText(ini, "[Pad1]\nCross = SDL-0/FaceEast\n");        // usuário remapeou no emulador
        Assert.False(configurator.EnsureConfigured(manifest, installed)); // não desfaz
        Assert.Contains("Cross = SDL-0/FaceEast", File.ReadAllLines(ini));

        configurator.Apply(manifest, installed);                           // botão "Configurar controle"
        Assert.Contains("Cross = SDL-0/FaceSouth", File.ReadAllLines(ini));
    }

    [Fact]
    public void Shipped_manifests_map_the_controller_where_needed()
    {
        var repo = ManifestTests.Load();
        Assert.Equal("inis/PCSX2.ini", repo.GetEmulator("pcsx2")!.Controller!.File);
        Assert.Equal("SDL-0/FaceSouth", repo.GetEmulator("duckstation")!.Controller!.Settings["Pad1"]["Cross"]);
        Assert.Equal("XInput/0/Gamepad", repo.GetEmulator("dolphin")!.Controller!.Settings["GCPad1"]["Device"]);
        Assert.Equal("flat", repo.GetEmulator("retroarch")!.Controller!.Format);
        Assert.Null(repo.GetEmulator("ppsspp")!.Controller); // suporte nativo
    }
}
