using System.Diagnostics;
using RetroHub.Core.Input;
using RetroHub.Core.Launch;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

public class LaunchCancelTests
{
    /// <summary>"Back + Start" no Modo TV: o jogo é fechado e o tempo jogado ainda é contado.</summary>
    [Fact]
    public async Task Cancelling_closes_the_emulator_and_returns()
    {
        using var dir = new TempDir();
        // Emulador falso: um cmd que ficaria 60 s rodando.
        dir.File("manifests/systems.json", """{ "systems": [ { "id": "nes", "name": "NES", "shortName": "NES", "extensions": [".nes"] } ] }""");
        dir.File("manifests/emulators/fake.json", """
            { "id": "fake", "name": "Fake", "systems": ["nes"], "source": { "type": "url", "url": "https://example.invalid/x.zip" },
              "executable": "cmd.exe",
              "launch": [ { "systems": ["nes"], "args": "/c ping -n 60 127.0.0.1 >nul & rem {rom}" } ] }
            """);
        var manifests = ManifestRepository.Load(Path.Combine(dir.Path, "manifests"));
        var db = new LibraryDatabase(Path.Combine(dir.Path, "lib.db"));
        db.Initialize();
        db.SaveInstalledEmulator(new InstalledEmulator
        {
            Id = "fake", Version = "1", InstallDir = dir.Path, InstalledAt = DateTimeOffset.UtcNow,
            ExecutablePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        });
        var rom = dir.File("game.nes", new byte[16]);
        var game = new Game { Id = 1, SystemId = "nes", Title = "Game", Path = rom };

        var launcher = new GameLauncher(manifests, db, new ControllerConfigurator(db));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var sw = Stopwatch.StartNew();

        var elapsed = await launcher.LaunchAsync(game, cts.Token);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"demorou {sw.Elapsed}");
        Assert.True(elapsed >= TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(Process.GetProcessesByName("PING"), p => SafeStart(p) > DateTime.Now - sw.Elapsed - TimeSpan.FromSeconds(1));
    }

    private static DateTime SafeStart(Process p)
    {
        try { return p.StartTime; } catch { return DateTime.MinValue; }
    }
}
