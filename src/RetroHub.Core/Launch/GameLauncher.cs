using System.Diagnostics;
using System.Text.RegularExpressions;
using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Launch;

public sealed record LaunchPlan(EmulatorManifest Emulator, InstalledEmulator Installed, string Executable, string Arguments, string WorkingDirectory);

public sealed class LaunchException(string message) : Exception(message);

/// <summary>Picks the right emulator for the game, builds the manifest arguments and measures play time.</summary>
public sealed partial class GameLauncher(ManifestRepository manifests, LibraryDatabase db, ControllerConfigurator controllers)
{
    /// <summary>Sessions shorter than this (crash on start, missing BIOS) do not count as play time.</summary>
    private static readonly TimeSpan MinimumSession = TimeSpan.FromSeconds(10);

    private readonly Install.EmulatorDefaults _defaults = new(db);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <param name="state">Open directly in this save state: forces the emulator that wrote the state.</param>
    public LaunchPlan Plan(Game game, Saves.SaveState? state = null)
    {
        var system = manifests.GetSystem(game.SystemId)
            ?? throw new LaunchException(L.F("Unknown system: {0}.", game.SystemId));

        var installed = db.GetInstalledEmulators().ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        // Preference order: game choice → system default → any other compatible one.
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
                problems.Add(L.F("{0}: executable is missing ({1}). Reinstall it.", emulator.Name, install.ExecutablePath));
                continue;
            }

            if (profile.Core is { } core)
            {
                var corePath = Path.Combine(install.InstallDir, emulator.Cores?.TargetDir ?? "cores", Util.Platform.CoreFileName(core));
                if (!File.Exists(corePath))
                {
                    problems.Add(L.F("{0}: core '{1}' is not installed.", emulator.Name, core));
                    continue;
                }
            }

            if (state is not null && !state.EmulatorId.Equals(id, StringComparison.OrdinalIgnoreCase))
                continue; // a state only opens in the emulator that wrote it

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

        var suggestion = system.DefaultEmulator is { } d && manifests.GetEmulator(d) is { } m ? m.Name : L.T("a compatible emulator");
        throw new LaunchException(L.F("No emulator installed for {0}. Install {1} in Emulators.", L.T(system.Name), suggestion));
    }

    public static string BuildArguments(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Starts the emulator and waits for it to close. Returns the session duration.</summary>
    public async Task<TimeSpan> LaunchAsync(Game game, CancellationToken ct = default, Saves.SaveState? state = null)
    {
        if (!File.Exists(game.Path))
            throw new LaunchException(L.F("File not found: {0}", game.Path));

        // Games added before this check existed: do not open the emulator with a movie DVD.
        if (Scanning.DiscInspector.Inspect(game.Path).IsVideoDisc)
            throw new LaunchException(L.F("\"{0}\" is a video DVD (VIDEO_TS folder), not a game disc. Scan the folders again to remove it from the library.",
                                      Path.GetFileName(game.Path)));

        var plan = Plan(game, state);

        // First time in this emulator: its needed settings and the controller mapping (not redone if the user changes them later).
        try { _defaults.EnsureApplied(plan.Emulator, plan.Installed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { /* play anyway */ }
        try { controllers.EnsureConfigured(plan.Emulator, plan.Installed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* play anyway */ }

        var psi = new ProcessStartInfo(plan.Executable, plan.Arguments)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
        };
        foreach (var (name, value) in plan.Emulator.Environment)
        {
            var expanded = value.Replace("{emuDir}", plan.Installed.InstallDir);
            if (name == "HOME") Directory.CreateDirectory(expanded);
            psi.Environment[name] = expanded;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new LaunchException(L.F("Failed to start {0}.", plan.Emulator.Name));
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // "Quit game" (TV Mode): asks it to close like the window's X (SIGTERM on macOS); if it does not respond in 4 s, kills it.
            await Task.Run(() =>
            {
                try
                {
                    Util.Platform.RequestClose(process);
                    if (!process.WaitForExit(4000))
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { /* already closed */ }
            });
        }
        stopwatch.Stop();

        if (stopwatch.Elapsed >= MinimumSession)
            db.RecordSession(game.Id, startedAt, (long)stopwatch.Elapsed.TotalSeconds);

        return stopwatch.Elapsed;
    }
}
