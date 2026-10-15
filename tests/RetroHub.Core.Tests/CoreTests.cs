using System.IO.Compression;
using RetroHub.Core.Launch;
using RetroHub.Core.Manifests;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Tests;

public class ManifestTests
{
    internal static ManifestRepository Load() =>
        ManifestRepository.Load(Path.Combine(AppContext.BaseDirectory, "manifests"));

    [Fact]
    public void Shipped_manifests_load_without_warnings()
    {
        var repo = Load();
        Assert.Empty(repo.Warnings);
        Assert.True(repo.Systems.Count >= 15);
        Assert.Equal(5, repo.Emulators.Count);
    }

    [Fact]
    public void Every_system_has_an_emulator_with_a_launch_profile()
    {
        var repo = Load();
        foreach (var system in repo.Systems)
        {
            Assert.Contains(repo.EmulatorsForSystem(system.Id), e => e.Launch.Any(p => p.Systems.Contains(system.Id)));
        }
    }
}

public class TitleCleanerTests
{
    [Theory]
    [InlineData("Chrono Trigger (USA).sfc", "Chrono Trigger")]
    [InlineData("Legend of Zelda, The - A Link to the Past (USA) [!].sfc", "The Legend of Zelda - A Link to the Past")]
    [InlineData("Final_Fantasy_VII_(Disc 1).cue", "Final Fantasy VII")]
    [InlineData("Metal Gear Solid (USA) (Disc 1) (Rev 1).chd", "Metal Gear Solid")]
    [InlineData("(Beta).nes", "(Beta)")]
    public void Cleans_common_naming_conventions(string file, string expected) =>
        Assert.Equal(expected, TitleCleaner.Clean(file));
}

public class DiscInspectorTests
{
    [Fact]
    public void Detects_ps2_and_serial_from_system_cnf()
    {
        using var dir = new TempDir();
        var iso = dir.File("game.iso", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF",
            "BOOT2 = cdrom0:\\SLUS_200.62;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n"));
        Assert.Equal(new DiscInfo("ps2", "SLUS-20062"), DiscInspector.Inspect(iso));
    }

    [Fact]
    public void Detects_ps1_from_system_cnf()
    {
        using var dir = new TempDir();
        var iso = dir.File("game.bin", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF",
            "BOOT = cdrom:\\SCUS_944.55;1\r\nTCB = 4\r\n"));
        Assert.Equal(new DiscInfo("ps1", "SCUS-94455"), DiscInspector.Inspect(iso));
    }

    [Fact]
    public void Detects_psp_umd()
    {
        using var dir = new TempDir();
        var iso = dir.File("game.iso", TestImages.Iso9660("PSP GAME", "UMD_DATA.BIN", "ULUS-10041|0001|G"));
        Assert.Equal(new DiscInfo("psp", "ULUS-10041"), DiscInspector.Inspect(iso));
    }

    [Fact]
    public void Detects_gamecube_iso_and_rvz_wii()
    {
        using var dir = new TempDir();
        Assert.Equal(new DiscInfo("gamecube", "GALE01"), DiscInspector.Inspect(dir.File("m.iso", TestImages.GameCube("GALE01"))));
        Assert.Equal(new DiscInfo("wii", "RMGE01"), DiscInspector.Inspect(dir.File("g.rvz", TestImages.Rvz(2, "RMGE01"))));
    }

    [Fact]
    public void Follows_cue_to_its_bin()
    {
        using var dir = new TempDir();
        dir.File("ps1/Game (USA) (Track 1).bin", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT = cdrom:\\SLUS_007.08;1"));
        var cue = dir.File("ps1/Game (USA).cue", "FILE \"Game (USA) (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        Assert.Equal(new DiscInfo("ps1", "SLUS-00708"), DiscInspector.Inspect(cue));
    }

    [Fact]
    public void Detects_dvd_video_bonus_discs()
    {
        using var dir = new TempDir();
        var iso = dir.File("Grand Theft Auto - San Andreas (Bonus).iso", TestImages.Iso9660("", "VIDEO_TS", "x"));
        Assert.True(DiscInspector.Inspect(iso).IsVideoDisc);
    }

    [Fact]
    public void Unknown_garbage_is_unknown()
    {
        using var dir = new TempDir();
        Assert.Equal(DiscInfo.Unknown, DiscInspector.Inspect(dir.File("x.iso", new byte[100_000])));
    }
}

public class RomScannerTests
{
    [Fact]
    public void Scans_a_mixed_library()
    {
        using var dir = new TempDir();
        dir.File("snes/Chrono Trigger (USA).sfc", new byte[1024]);
        dir.File("misc/Metroid Fusion (USA).zip", Zip("Metroid Fusion (USA).gba"));
        dir.File("isos/Shadow of the Colossus (USA).iso", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT2 = cdrom0:\\SCUS_974.72;1"));
        dir.File("ps1/FF7 (Disc 1).bin", TestImages.Iso9660("PLAYSTATION", "SYSTEM.CNF", "BOOT = cdrom:\\SCUS_941.63;1"));
        dir.File("ps1/FF7 (Disc 1).cue", "FILE \"FF7 (Disc 1).bin\" BINARY\n");
        dir.File("gamecube/Melee.iso", new byte[0x9000]);          // cabeçalho zerado → decide pela pasta
        dir.File("random/Mystery.iso", new byte[0x9000]);          // sem pista nenhuma
        dir.File("snes/readme.txt", "ignorado");
        dir.File("ps2/GTA San Andreas (Bonus).iso", TestImages.Iso9660("", "VIDEO_TS", "x")); // pasta ps2, mas é DVD de filme

        var scanner = new RomScanner(ManifestTests.Load());
        var report = scanner.Scan([dir.Path]);
        var bySystem = report.Roms.ToDictionary(r => r.Title, r => r.SystemId);

        Assert.Equal("snes", bySystem["Chrono Trigger"]);
        Assert.Equal("gba", bySystem["Metroid Fusion"]);
        Assert.Equal("ps2", bySystem["Shadow of the Colossus"]);
        Assert.Equal("ps1", bySystem["FF7"]);
        Assert.Equal("gamecube", bySystem["Melee"]);
        Assert.Equal(5, report.Roms.Count); // o .bin referenciado pelo .cue não vira jogo separado
        Assert.Single(report.Unidentified);
        Assert.EndsWith("Mystery.iso", report.Unidentified[0]);
        Assert.Single(report.Rejected);
        Assert.EndsWith("GTA San Andreas (Bonus).iso", report.Rejected[0].Path);

        Assert.Equal("SCUS-94163", report.Roms.Single(r => r.Title == "FF7").Serial);
        Assert.NotNull(report.Roms.Single(r => r.Title == "Chrono Trigger").Crc32);
    }

    private static byte[] Zip(string entryName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(entryName).Open();
            stream.Write(new byte[64]);
        }
        return ms.ToArray();
    }
}

public class LauncherTests
{
    [Fact]
    public void Builds_arguments_from_placeholders()
    {
        var args = GameLauncher.BuildArguments("-f -L \"{emuDir}\\cores\\{core}_libretro.dll\" \"{rom}\" {unknown}",
            new Dictionary<string, string>
            {
                ["emuDir"] = @"C:\RH\emulators\retroarch",
                ["core"] = "snes9x",
                ["rom"] = @"D:\ROMs\snes\Chrono Trigger (USA).sfc",
            });

        Assert.Equal(@"-f -L ""C:\RH\emulators\retroarch\cores\snes9x_libretro.dll"" ""D:\ROMs\snes\Chrono Trigger (USA).sfc"" {unknown}", args);
    }
}
