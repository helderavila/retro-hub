using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

/// <summary>Controle em emuladores Qt (QSettings) que guardam o identificador SDL de cada controle.</summary>
public class QtControllerTests
{
    private static readonly XInputDeviceInfo XboxSeries = new(0, 0x045E, 0x0B13, 0x0523, 1);

    private const string DefaultQtConfig = """
        [UI]
        theme=default

        [Controls]
        player_0_connected\default=true
        player_0_connected=true
        player_1_connected\default=true
        player_1_connected=false
        player_0_button_a\default=false
        player_0_button_a="engine:keyboard,code:67,toggle:0"
        player_0_motionleft="engine:keyboard,code:55,toggle:0"
        """;

    /// <summary>Manifesto de exemplo (formato "qt", jogadores 1 e 2 pelo identificador SDL).</summary>
    private static EmulatorManifest QtEmulator() => new()
    {
        Id = "qtemu",
        Name = "Qt Emu",
        Controller = new ControllerConfig
        {
            File = "user/config/qt-config.ini",
            Format = "qt",
            Description = "Jogadores 1 e 2",
            Settings = new()
            {
                ["Controls"] = new()
                {
                    ["player_0_connected\\default"] = "false", ["player_0_connected"] = "true",
                    ["player_0_button_a\\default"] = "false", ["player_0_button_a"] = "\"engine:sdl,port:{port0},guid:{guid0},button:1\"",
                    ["player_0_button_zr\\default"] = "false", ["player_0_button_zr"] = "\"engine:sdl,port:{port0},guid:{guid0},axis:5,threshold:0.5,invert:+\"",
                    ["player_0_button_dup\\default"] = "false", ["player_0_button_dup"] = "\"engine:sdl,port:{port0},guid:{guid0},hat:0,direction:up\"",
                    ["player_1_connected\\default"] = "false", ["player_1_connected"] = "true",
                    ["player_1_button_a\\default"] = "false", ["player_1_button_a"] = "\"engine:sdl,port:{port1},guid:{guid1},button:1\"",
                },
            },
        },
    };

    [Fact]
    public void Guid_matches_the_sdl_xinput_format()
    {
        Assert.Equal("030000005e040000130b000023057801", XboxSeries.SdlGuid);
        Assert.Equal("030000005e040000e002000000007801", new XInputDeviceInfo(0, 0x045E, 0x02E0, 0, 1).SdlGuid);
    }

    private static (ControllerConfigurator Configurator, EmulatorManifest Emulator, InstalledEmulator Installed, string Ini) Create(
        TempDir dir, params XInputDeviceInfo[] devices)
    {
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        var ini = dir.File("qtemu/user/config/qt-config.ini", DefaultQtConfig);
        return (new ControllerConfigurator(db) { Devices = () => devices }, QtEmulator(),
            new InstalledEmulator { Id = "qtemu", InstallDir = Path.Combine(dir.Path, "qtemu") }, ini);
    }

    [Fact]
    public void Maps_player_1_and_keeps_the_rest_of_the_file()
    {
        using var dir = new TempDir();
        var (configurator, emulator, installed, ini) = Create(dir, XboxSeries);

        Assert.True(configurator.EnsureConfigured(emulator, installed));
        var lines = File.ReadAllLines(ini);

        const string guid = "guid:030000005e040000130b000023057801";
        Assert.Contains($"player_0_button_a=\"engine:sdl,port:0,{guid},button:1\"", lines);
        Assert.Contains($"player_0_button_zr=\"engine:sdl,port:0,{guid},axis:5,threshold:0.5,invert:+\"", lines);
        Assert.Contains($"player_0_button_dup=\"engine:sdl,port:0,{guid},hat:0,direction:up\"", lines);
        Assert.Contains("player_0_button_a\\default=false", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("player_0_button_a=\"engine:keyboard"));
        Assert.Contains("player_0_motionleft=\"engine:keyboard,code:55,toggle:0\"", lines); // o que o manifesto não cita fica como estava
        Assert.Contains("theme=default", lines);

        // Só um controle: o jogador 2 continua desconectado.
        Assert.Contains("player_1_connected=false", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("player_1_button_a="));

        Assert.False(configurator.EnsureConfigured(emulator, installed)); // já configurado
    }

    [Fact]
    public void Two_identical_controllers_get_ports_0_and_1()
    {
        using var dir = new TempDir();
        var (configurator, emulator, installed, ini) = Create(dir, XboxSeries, XboxSeries with { Slot = 1 });

        configurator.EnsureConfigured(emulator, installed);
        var lines = File.ReadAllLines(ini);

        Assert.Contains("player_1_connected=true", lines);
        Assert.Contains(lines, l => l.StartsWith("player_1_button_a=\"engine:sdl,port:1,guid:030000005e040000130b000023057801,"));
    }

    [Fact]
    public void Without_a_controller_nothing_is_written_and_it_retries_later()
    {
        using var dir = new TempDir();
        var (configurator, emulator, installed, ini) = Create(dir);

        Assert.False(configurator.EnsureConfigured(emulator, installed));
        Assert.Equal(DefaultQtConfig.Split('\n').Select(l => l.TrimEnd('\r')), File.ReadAllLines(ini));
    }
}
