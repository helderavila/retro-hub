using RetroHub.Core.Input;
using RetroHub.Core.Install;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

public class EmulatorSettingsTests
{
    [Fact]
    public void Reads_and_changes_resolution_in_the_real_pcsx2_manifest()
    {
        using var dir = new TempDir();
        var pcsx2 = ManifestTests.Load().GetEmulator("pcsx2")!;
        var installed = new InstalledEmulator { Id = "pcsx2", InstallDir = dir.Path };
        dir.File("inis/PCSX2.ini", "[EmuCore/GS]\nAspectRatio = Auto 4:3/3:2\nupscale_multiplier = 1\nMipmap = true\n");
        var g = pcsx2.Graphics!;

        Assert.Equal("1x", EmulatorSettings.Current(installed, g, g.Resolution));
        Assert.Equal("Auto", EmulatorSettings.Current(installed, g, g.Aspect));

        EmulatorSettings.Apply(installed, g, g.Resolution!, "3x");
        EmulatorSettings.Apply(installed, g, g.Aspect!, "16:9");

        Assert.Equal("3x", EmulatorSettings.Current(installed, g, g.Resolution));
        Assert.Equal("16:9", EmulatorSettings.Current(installed, g, g.Aspect));
        Assert.Contains("Mipmap = true", File.ReadAllLines(Path.Combine(dir.Path, "inis", "PCSX2.ini")));
    }

    [Fact]
    public void Retroarch_aspect_and_achievements_use_the_flat_cfg()
    {
        using var dir = new TempDir();
        var retroarch = ManifestTests.Load().GetEmulator("retroarch")!;
        var installed = new InstalledEmulator { Id = "retroarch", InstallDir = dir.Path };
        dir.File("retroarch.cfg", "video_fullscreen = \"true\"\n");

        EmulatorSettings.Apply(installed, retroarch.Graphics!, retroarch.Graphics!.Aspect!, "4:3");
        EmulatorSettings.EnableAchievements(installed, retroarch.Achievements!, "jogador", "s3nha");

        var cfg = File.ReadAllLines(Path.Combine(dir.Path, "retroarch.cfg"));
        Assert.Contains("aspect_ratio_index = \"0\"", cfg);
        Assert.Contains("video_aspect_ratio_auto = \"false\"", cfg);
        Assert.Contains("cheevos_enable = \"true\"", cfg);
        Assert.Contains("cheevos_username = \"jogador\"", cfg);
        Assert.Contains("video_fullscreen = \"true\"", cfg);
        Assert.Equal("4:3", EmulatorSettings.Current(installed, retroarch.Graphics, retroarch.Graphics.Aspect));
    }

    [Fact]
    public void Update_keeps_user_config_files()
    {
        using var dir = new TempDir();
        dir.File("nova-versao/retroarch.exe", "novo");
        dir.File("nova-versao/retroarch.cfg", "padrão da versão nova");
        dir.File("instalado/retroarch.exe", "velho");
        dir.File("instalado/retroarch.cfg", "configuração do usuário");

        ArchiveExtractor.CopyDirectory(Path.Combine(dir.Path, "nova-versao"), Path.Combine(dir.Path, "instalado"), ["retroarch.cfg"]);

        Assert.Equal("novo", File.ReadAllText(Path.Combine(dir.Path, "instalado", "retroarch.exe")));
        Assert.Equal("configuração do usuário", File.ReadAllText(Path.Combine(dir.Path, "instalado", "retroarch.cfg")));
    }
}
