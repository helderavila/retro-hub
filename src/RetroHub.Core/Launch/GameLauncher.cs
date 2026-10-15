using System.Diagnostics;
using System.Text.RegularExpressions;
using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Launch;

public sealed record LaunchPlan(EmulatorManifest Emulator, InstalledEmulator Installed, string Executable, string Arguments, string WorkingDirectory);

public sealed class LaunchException(string message) : Exception(message);

/// <summary>Escolhe o emulador certo para o jogo, monta os argumentos do manifesto e mede o tempo jogado.</summary>
public sealed partial class GameLauncher(ManifestRepository manifests, LibraryDatabase db, ControllerConfigurator controllers)
{
    /// <summary>Sessões mais curtas que isso (crash ao abrir, BIOS faltando) não contam tempo.</summary>
    private static readonly TimeSpan MinimumSession = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <param name="state">Abrir já neste save state: força o emulador que gravou o state.</param>
    public LaunchPlan Plan(Game game, Saves.SaveState? state = null)
    {
        var system = manifests.GetSystem(game.SystemId)
            ?? throw new LaunchException($"Sistema desconhecido: {game.SystemId}.");

        var installed = db.GetInstalledEmulators().ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        // Ordem de preferência: escolha do jogo → padrão do sistema → qualquer outro compatível.
        var candidates = new List<string>();
        if (state is not null) candidates.Add(state.EmulatorId);
        else if (game.EmulatorOverride is { } overrideId) candidates.Add(overrideId);
        if (system.DefaultEmulator is { } defaultId) candidates.Add(defaultId);
        candidates.AddRange(manifests.EmulatorsForSystem(system.Id).Select(e => e.Id));

        var problems = new List<string>();
        foreach (var id in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (manifests.GetEmulator(id) is not { } emulator || !installed.TryGetValue(id, out var install))
                continue;

            var profile = emulator.Launch.FirstOrDefault(p => p.Systems.Contains(system.Id, StringComparer.OrdinalIgnoreCase));
            if (profile is null)
                continue;

            if (!File.Exists(install.ExecutablePath))
            {
                problems.Add($"{emulator.Name}: executável sumiu ({install.ExecutablePath}). Reinstale.");
                continue;
            }

            if (profile.Core is { } core)
            {
                var corePath = Path.Combine(install.InstallDir, emulator.Cores?.TargetDir ?? "cores", $"{core}_libretro.dll");
                if (!File.Exists(corePath))
                {
                    problems.Add($"{emulator.Name}: core '{core}' não instalado.");
                    continue;
                }
            }

            if (state is not null && !state.EmulatorId.Equals(id, StringComparison.OrdinalIgnoreCase))
                continue; // o state só abre no emulador que o gravou

            var template = state is not null && emulator.Saves?.LoadStateArgs is { } withState ? withState : profile.Args;
            var args = BuildArguments(template, new Dictionary<string, string>
            {
                ["state"] = state?.Path ?? "",
                ["slot"] = state?.Slot?.ToString() ?? "0",
                ["rom"] = game.Path,
                ["romDir"] = Path.GetDirectoryName(game.Path) ?? "",
                ["romName"] = Path.GetFileNameWithoutExtension(game.Path),
                ["emuDir"] = install.InstallDir,
                ["core"] = profile.Core ?? "",
            });

            return new LaunchPlan(emulator, install, install.ExecutablePath, args, install.InstallDir);
        }

        if (problems.Count > 0)
            throw new LaunchException(string.Join("\n", problems));

        var suggestion = system.DefaultEmulator is { } d && manifests.GetEmulator(d) is { } m ? m.Name : "um emulador compatível";
        throw new LaunchException($"Nenhum emulador instalado para {system.Name}. Instale {suggestion} em Emuladores.");
    }

    public static string BuildArguments(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Abre o emulador e espera ele fechar. Retorna a duração da sessão.</summary>
    public async Task<TimeSpan> LaunchAsync(Game game, CancellationToken ct = default, Saves.SaveState? state = null)
    {
        if (!File.Exists(game.Path))
            throw new LaunchException($"Arquivo não encontrado: {game.Path}");

        // Jogos adicionados antes desta checagem existir: não abre o emulador com um DVD de filme.
        if (Scanning.DiscInspector.Inspect(game.Path).IsVideoDisc)
            throw new LaunchException($"\"{Path.GetFileName(game.Path)}\" é um DVD de vídeo (pasta VIDEO_TS), não um disco de jogo. " +
                                      "Escaneie as pastas de novo para tirá-lo da biblioteca.");

        var plan = Plan(game, state);

        // Primeira vez neste emulador: mapeia o controle (não sobrescreve se o usuário já remapeou depois).
        try { controllers.EnsureConfigured(plan.Emulator, plan.Installed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* joga mesmo assim */ }

        var psi = new ProcessStartInfo(plan.Executable, plan.Arguments)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
        };

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new LaunchException($"Falha ao iniciar {plan.Emulator.Name}.");
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // "Sair do jogo" (Modo TV): pede para fechar como o X da janela; se não responder em 4 s, encerra.
            await Task.Run(() =>
            {
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(4000))
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { /* já fechou */ }
            });
        }
        stopwatch.Stop();

        if (stopwatch.Elapsed >= MinimumSession)
            db.RecordSession(game.Id, startedAt, (long)stopwatch.Elapsed.TotalSeconds);

        return stopwatch.Elapsed;
    }
}
