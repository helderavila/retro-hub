using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Manifests;

/// <summary>
/// Loads and validates the app's systems.json, dependencies.json and emulators/*.json, plus systems/*.json and emulators/*.json
/// from the user's folder (the user's wins when the id is the same). <see cref="Reload"/> rereads everything without restarting.
/// </summary>
public sealed class ManifestRepository
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _manifestsDir;
    private readonly string? _userDir;
    private readonly string? _platform;

    private Dictionary<string, SystemDefinition> _systems = [];
    private Dictionary<string, EmulatorManifest> _emulators = [];
    private Dictionary<string, DependencyDefinition> _dependencies = [];
    private Dictionary<string, List<SystemDefinition>> _byExtension = [];

    public IReadOnlyList<SystemDefinition> Systems { get; private set; } = [];
    public IReadOnlyList<EmulatorManifest> Emulators { get; private set; } = [];
    public IReadOnlyList<DependencyDefinition> Dependencies { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    private ManifestRepository(string manifestsDir, string? userDir, string? platform)
    {
        _manifestsDir = manifestsDir;
        _userDir = userDir;
        _platform = platform;
        Reload();
    }

    /// <param name="userDir">User manifests folder (systems\, emulators\); null = app manifests only.</param>
    public static ManifestRepository Load(string manifestsDir, string? userDir = null) => new(manifestsDir, userDir, Util.Platform.ManifestKey);

    /// <summary>Loads the manifests as a given platform sees them: null = Windows (top-level keys), "macos" = with the macOS blocks applied.</summary>
    public static ManifestRepository LoadFor(string? platform, string manifestsDir, string? userDir = null) => new(manifestsDir, userDir, platform);

    /// <summary>Rereads the manifests (after the user adds, imports or removes an emulator).</summary>
    public void Reload()
    {
        var systems = Read<SystemsManifest>(Path.Combine(_manifestsDir, "systems.json")).Systems;

        var depsPath = Path.Combine(_manifestsDir, "dependencies.json");
        var dependencies = File.Exists(depsPath) ? Read<DependenciesManifest>(depsPath).Dependencies : [];

        var emulators = ReadAll<EmulatorManifest>(Path.Combine(_manifestsDir, "emulators"), _platform).Select(x => x.Item)
            .Where(e => e.SupportedHere).ToList();

        var problems = new List<string>();
        if (_userDir is not null)
        {
            // User manifests come last and replace the app's ones with the same id.
            foreach (var (file, extra) in ReadAll<SystemsManifest>(Path.Combine(_userDir, "systems"), _platform, problems))
            {
                systems.RemoveAll(s => extra.Systems.Any(x => string.Equals(x.Id, s.Id, StringComparison.OrdinalIgnoreCase)));
                systems.AddRange(extra.Systems);
            }
            foreach (var (file, emulator) in ReadAll<EmulatorManifest>(Path.Combine(_userDir, "emulators"), _platform, problems))
            {
                if (!emulator.SupportedHere)
                    continue;
                emulator.UserManifestPath = file;
                emulators.RemoveAll(e => string.Equals(e.Id, emulator.Id, StringComparison.OrdinalIgnoreCase));
                emulators.Add(emulator);
            }
        }

        foreach (var s in systems)
            s.Extensions = s.Extensions.Select(e => e.ToLowerInvariant()).ToList();

        Systems = systems;
        Emulators = emulators;
        Dependencies = dependencies;

        _systems = systems.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _emulators = emulators.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _dependencies = dependencies.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _byExtension = systems
            .SelectMany(s => s.Extensions.Select(ext => (ext, s)))
            .GroupBy(x => x.ext)
            .ToDictionary(g => g.Key, g => g.Select(x => x.s).ToList());

        Warnings = [.. problems, .. Validate()];
    }

    /// <summary>Reads every .json in a folder. With <paramref name="problems"/>, an invalid file becomes a warning instead of an error.</summary>
    private static List<(string File, T Item)> ReadAll<T>(string dir, string? platform, List<string>? problems = null)
    {
        var list = new List<(string, T)>();
        if (!Directory.Exists(dir))
            return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add((file, Read<T>(file, platform))); }
            catch (InvalidDataException ex) when (problems is not null) { problems.Add(ex.Message); }
        }
        return list;
    }

    /// <summary>Reads and checks an emulator manifest (to import a user file), as this repository's platform sees it.</summary>
    public EmulatorManifest ReadEmulator(string path) => Read<EmulatorManifest>(path, _platform);

    public SystemDefinition? GetSystem(string id) => _systems.GetValueOrDefault(id);
    public EmulatorManifest? GetEmulator(string id) => _emulators.GetValueOrDefault(id);
    public DependencyDefinition? GetDependency(string id) => _dependencies.GetValueOrDefault(id);

    public IReadOnlyList<SystemDefinition> SystemsForExtension(string extension) =>
        _byExtension.TryGetValue(extension.ToLowerInvariant(), out var list) ? list : [];

    public IEnumerable<EmulatorManifest> EmulatorsForSystem(string systemId) =>
        Emulators.Where(e => e.Systems.Contains(systemId, StringComparer.OrdinalIgnoreCase));

    private static T Read<T>(string path, string? platform = null)
    {
        try
        {
            if (typeof(T) == typeof(EmulatorManifest))
                return (T)(object)ReadEmulatorForPlatform(path, platform);

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException(L.F("Empty manifest: {0}", path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(L.F("Invalid manifest ({0}): {1}", Path.GetFileName(path), ex.Message), ex);
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Emulator manifest with the platform block applied: on macOS, each key inside "macos": { … } replaces the top-level one
    /// (source, executable, launch, controller…). Without that block, a downloadable emulator is Windows-only there
    /// (<see cref="EmulatorManifest.SupportedHere"/> = false); a "local" one (an app already on the computer) works anywhere.
    /// </summary>
    internal static EmulatorManifest ReadEmulatorForPlatform(string path, string? platformKey)
    {
        var text = File.ReadAllText(path);
        if (JsonNode.Parse(text, documentOptions: DocumentOptions) is not JsonObject root)
            throw new InvalidDataException(L.F("Empty manifest: {0}", path));

        var hasBlock = false;
        if (platformKey is not null && root[platformKey] is JsonObject block)
        {
            hasBlock = true;
            foreach (var (key, value) in block.ToList())
            {
                block.Remove(key);
                root[key] = value;
            }
        }
        foreach (var key in new[] { "macos", "windows" })
            root.Remove(key);

        var manifest = root.Deserialize<EmulatorManifest>(JsonOptions)
            ?? throw new InvalidDataException(L.F("Empty manifest: {0}", path));
        manifest.SupportedHere = platformKey is null || hasBlock || manifest.Source.Type == "local";
        ApplyDataDir(manifest);
        return manifest;
    }

    /// <summary>"dataDir": prefixes it to every data path, so the rest of the app keeps working relative to the install folder.</summary>
    private static void ApplyDataDir(EmulatorManifest m)
    {
        if (string.IsNullOrWhiteSpace(m.DataDir))
            return;
        string In(string relative) => Path.Combine(m.DataDir, relative);

        if (m.BiosDir is not null) m.BiosDir = In(m.BiosDir);
        if (m.Controller is { } controller) controller.File = In(controller.File);
        if (m.Achievements is { } achievements) achievements.File = In(achievements.File);
        if (m.Graphics is { } graphics)
        {
            graphics.File = In(graphics.File);
            foreach (var toggle in graphics.Toggles)
                if (toggle.File is { } file) file.Path = In(file.Path);
        }
        if (m.Saves is { } saves)
        {
            foreach (var location in saves.States)
                location.Dir = In(location.Dir);
            saves.Backup = saves.Backup.Select(In).ToList();
        }
    }

    private List<string> Validate()
    {
        var warnings = new List<string>();

        foreach (var dup in Systems.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add(L.F("Duplicate system: {0}", dup.Key));
        foreach (var dup in Emulators.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add(L.F("Duplicate emulator: {0}", dup.Key));

        foreach (var system in Systems)
        {
            if (system.DefaultEmulator is { } emu && GetEmulator(emu) is null)
                warnings.Add(L.F("{0}: defaultEmulator '{1}' does not exist", system.Id, emu));
        }

        foreach (var emu in Emulators)
        {
            if (emu.Source.Type is not ("github" or "forgejo" or "url" or "local"))
                warnings.Add(L.F("{0}: unknown source.type '{1}'", emu.Id, emu.Source.Type));
            if (emu.Source.Type == "local" && string.IsNullOrWhiteSpace(emu.Source.Path))
                warnings.Add(L.F("{0}: local source requires 'path'", emu.Id));
            if (emu.Source.Type is "github" or "forgejo" && (emu.Source.Repo is null || emu.Source.Asset is null))
                warnings.Add(L.F("{0}: github source requires 'repo' and 'asset'", emu.Id));
            if (emu.Source.Type == "url" && emu.Source.Url is null)
                warnings.Add(L.F("{0}: url source requires 'url'", emu.Id));
            if (string.IsNullOrWhiteSpace(emu.Executable))
                warnings.Add(L.F("{0}: empty 'executable'", emu.Id));

            if (emu.Controller is { } controller)
            {
                if (controller.Format is not ("ini" or "flat" or "qt"))
                    warnings.Add(L.F("{0}: unknown controller.format '{1}'", emu.Id, controller.Format));
                if (string.IsNullOrWhiteSpace(controller.File) || Path.IsPathRooted(controller.File) || controller.File.Contains(".."))
                    warnings.Add(L.F("{0}: controller.file must be relative to the emulator folder", emu.Id));
            }

            foreach (var sys in emu.Systems.Where(s => GetSystem(s) is null))
                warnings.Add(L.F("{0}: system '{1}' does not exist in systems.json", emu.Id, sys));
            foreach (var dep in emu.Dependencies.Where(d => GetDependency(d) is null))
                warnings.Add(L.F("{0}: dependency '{1}' does not exist in dependencies.json", emu.Id, dep));

            foreach (var profile in emu.Launch)
            {
                foreach (var sys in profile.Systems.Where(s => !emu.Systems.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    warnings.Add(L.F("{0}: launch profile mentions '{1}', which is not in 'systems'", emu.Id, sys));
                if (profile.Core is not null && emu.Cores is null)
                    warnings.Add(L.F("{0}: profile uses core '{1}' but there is no 'cores'", emu.Id, profile.Core));
            }

            foreach (var sys in emu.Systems.Where(s => !emu.Launch.Any(p => p.Systems.Contains(s, StringComparer.OrdinalIgnoreCase))))
                warnings.Add(L.F("{0}: no launch profile for '{1}'", emu.Id, sys));
        }

        return warnings;
    }
}
