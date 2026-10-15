using RetroHub.Core.Input;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

public class GraphicsPresetTests
{
    [Fact]
    public void Every_emulator_with_presets_has_the_three_levels()
    {
        foreach (var emu in ManifestTests.Load().Emulators.Where(e => e.Graphics?.Presets is not null))
            Assert.Equal([EmulatorSettings.Original, EmulatorSettings.Enhanced, EmulatorSettings.Maximum], emu.Graphics!.Presets!.Values.Keys);
    }

    [Fact]
    public void Pcsx2_presets_are_applied_and_detected()
    {
        using var dir = new TempDir();
        var g = ManifestTests.Load().GetEmulator("pcsx2")!.Graphics!;
        var installed = new InstalledEmulator { Id = "pcsx2", InstallDir = dir.Path };
        dir.File("inis/PCSX2.ini", "[EmuCore/GS]\nupscale_multiplier = 1\nMaxAnisotropy = 0\nAspectRatio = Auto 4:3/3:2\n");

        Assert.Equal("Original", EmulatorSettings.Current(installed, g, g.Presets));
        EmulatorSettings.Apply(installed, g, g.Presets!, "Máximo");

        Assert.Equal("Máximo", EmulatorSettings.Current(installed, g, g.Presets));
        Assert.Equal("6x", EmulatorSettings.Current(installed, g, g.Resolution));

        EmulatorSettings.Apply(installed, g, g.Resolution!, "4x"); // ajuste manual → nível personalizado
        Assert.Null(EmulatorSettings.Current(installed, g, g.Presets));
    }

    [Fact]
    public void Widescreen_toggle_turns_on_patches_and_16_9_then_reverts()
    {
        using var dir = new TempDir();
        var g = ManifestTests.Load().GetEmulator("pcsx2")!.Graphics!;
        var installed = new InstalledEmulator { Id = "pcsx2", InstallDir = dir.Path };
        dir.File("inis/PCSX2.ini", "[EmuCore]\nEnableWideScreenPatches = false\n[EmuCore/GS]\nAspectRatio = Auto 4:3/3:2\n");
        var widescreen = g.Toggles.Single(t => t.Id == "widescreen");

        EmulatorSettings.SetToggle(installed, g, widescreen, true);
        Assert.True(EmulatorSettings.IsOn(installed, g, widescreen));
        Assert.Equal("16:9", EmulatorSettings.Current(installed, g, g.Aspect));

        EmulatorSettings.SetToggle(installed, g, widescreen, false);
        Assert.False(EmulatorSettings.IsOn(installed, g, widescreen));
        Assert.Equal("Auto", EmulatorSettings.Current(installed, g, g.Aspect));
    }

    [Fact]
    public void Qt_preset_writes_keys_with_default_false()
    {
        using var dir = new TempDir();
        static Dictionary<string, Dictionary<string, string>> Renderer(string res, string af) => new()
        {
            ["Renderer"] = new() { ["resolution_setup\\default"] = "false", ["resolution_setup"] = res, ["max_anisotropy\\default"] = "false", ["max_anisotropy"] = af },
        };
        var g = new GraphicsConfig
        {
            File = "user/config/qt-config.ini",
            Format = "qt",
            Presets = new GraphicsOption { Values = new() { ["Original"] = Renderer("3", "1"), ["Máximo"] = Renderer("6", "5") } },
            Resolution = new GraphicsOption { Values = new() { ["1x"] = Renderer("3", "1"), ["2x"] = Renderer("6", "5") } },
        };
        var installed = new InstalledEmulator { Id = "qtemu", InstallDir = dir.Path };
        var ini = dir.File("user/config/qt-config.ini", "[Renderer]\nresolution_setup\\default=true\nresolution_setup=3\nbackend=1\n");

        EmulatorSettings.Apply(installed, g, g.Presets!, "Máximo");
        var lines = File.ReadAllLines(ini);

        Assert.Contains("resolution_setup\\default=false", lines);
        Assert.Contains("resolution_setup=6", lines);
        Assert.Contains("max_anisotropy=5", lines);
        Assert.Contains("backend=1", lines); // o resto do arquivo fica
        Assert.Equal("Máximo", EmulatorSettings.Current(installed, g, g.Presets));
        Assert.Equal("2x", EmulatorSettings.Current(installed, g, g.Resolution));
    }

    [Fact]
    public void Retroarch_crt_toggle_creates_and_removes_the_global_shader_preset()
    {
        using var dir = new TempDir();
        var g = ManifestTests.Load().GetEmulator("retroarch")!.Graphics!;
        var installed = new InstalledEmulator { Id = "retroarch", InstallDir = dir.Path };
        dir.File("retroarch.cfg", "video_driver = \"d3d11\"\nvideo_shader_enable = \"false\"\n");
        var crt = g.Toggles.Single(t => t.Id == "crt");
        var preset = Path.Combine(dir.Path, "config", "global.slangp");

        EmulatorSettings.SetToggle(installed, g, crt, true);
        Assert.True(File.Exists(preset));
        Assert.Contains("crt-aperture.slangp", File.ReadAllText(preset));
        Assert.True(EmulatorSettings.IsOn(installed, g, crt));

        EmulatorSettings.SetToggle(installed, g, crt, false);
        Assert.False(File.Exists(preset));
        Assert.False(EmulatorSettings.IsOn(installed, g, crt));
        Assert.Contains("video_driver = \"d3d11\"", File.ReadAllLines(Path.Combine(dir.Path, "retroarch.cfg")));
    }
}
