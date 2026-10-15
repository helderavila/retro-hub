using System.Text.Json;
using System.Text.Json.Serialization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Manifests;

/// <summary>
/// Carrega e valida systems.json, dependencies.json e emulators/*.json do app, mais systems/*.json e emulators/*.json
/// da pasta do usuário (o do usuário vale quando o id é o mesmo). <see cref="Reload"/> relê tudo sem reiniciar.
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

    private Dictionary<string, SystemDefinition> _systems = [];
    private Dictionary<string, EmulatorManifest> _emulators = [];
    private Dictionary<string, DependencyDefinition> _dependencies = [];
    private Dictionary<string, List<SystemDefinition>> _byExtension = [];

    public IReadOnlyList<SystemDefinition> Systems { get; private set; } = [];
    public IReadOnlyList<EmulatorManifest> Emulators { get; private set; } = [];
    public IReadOnlyList<DependencyDefinition> Dependencies { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    private ManifestRepository(string manifestsDir, string? userDir)
    {
        _manifestsDir = manifestsDir;
        _userDir = userDir;
        Reload();
    }

    /// <param name="userDir">Pasta de manifestos do usuário (systems\, emulators\); null = só os do app.</param>
    public static ManifestRepository Load(string manifestsDir, string? userDir = null) => new(manifestsDir, userDir);

    /// <summary>Relê os manifestos (depois que o usuário adiciona, importa ou remove um emulador).</summary>
    public void Reload()
    {
        var systems = Read<SystemsManifest>(Path.Combine(_manifestsDir, "systems.json")).Systems;

        var depsPath = Path.Combine(_manifestsDir, "dependencies.json");
        var dependencies = File.Exists(depsPath) ? Read<DependenciesManifest>(depsPath).Dependencies : [];

        var emulators = ReadAll<EmulatorManifest>(Path.Combine(_manifestsDir, "emulators")).Select(x => x.Item).ToList();

        var problems = new List<string>();
        if (_userDir is not null)
        {
            // Os do usuário entram por último e substituem os do app com o mesmo id.
            foreach (var (file, extra) in ReadAll<SystemsManifest>(Path.Combine(_userDir, "systems"), problems))
            {
                systems.RemoveAll(s => extra.Systems.Any(x => string.Equals(x.Id, s.Id, StringComparison.OrdinalIgnoreCase)));
                systems.AddRange(extra.Systems);
            }
            foreach (var (file, emulator) in ReadAll<EmulatorManifest>(Path.Combine(_userDir, "emulators"), problems))
            {
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

    /// <summary>Lê todos os .json de uma pasta. Com <paramref name="problems"/>, um arquivo inválido vira aviso em vez de erro.</summary>
    private static List<(string File, T Item)> ReadAll<T>(string dir, List<string>? problems = null)
    {
        var list = new List<(string, T)>();
        if (!Directory.Exists(dir))
            return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add((file, Read<T>(file))); }
            catch (InvalidDataException ex) when (problems is not null) { problems.Add(ex.Message); }
        }
        return list;
    }

    /// <summary>Lê e confere um manifesto de emulador (para importar um arquivo do usuário).</summary>
    public static EmulatorManifest ReadEmulator(string path) => Read<EmulatorManifest>(path);

    public SystemDefinition? GetSystem(string id) => _systems.GetValueOrDefault(id);
    public EmulatorManifest? GetEmulator(string id) => _emulators.GetValueOrDefault(id);
    public DependencyDefinition? GetDependency(string id) => _dependencies.GetValueOrDefault(id);

    public IReadOnlyList<SystemDefinition> SystemsForExtension(string extension) =>
        _byExtension.TryGetValue(extension.ToLowerInvariant(), out var list) ? list : [];

    public IEnumerable<EmulatorManifest> EmulatorsForSystem(string systemId) =>
        Emulators.Where(e => e.Systems.Contains(systemId, StringComparer.OrdinalIgnoreCase));

    private static T Read<T>(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException($"Manifesto vazio: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Manifesto inválido ({Path.GetFileName(path)}): {ex.Message}", ex);
        }
    }

    private List<string> Validate()
    {
        var warnings = new List<string>();

        foreach (var dup in Systems.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"Sistema duplicado: {dup.Key}");
        foreach (var dup in Emulators.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"Emulador duplicado: {dup.Key}");

        foreach (var system in Systems)
        {
            if (system.DefaultEmulator is { } emu && GetEmulator(emu) is null)
                warnings.Add($"{system.Id}: defaultEmulator '{emu}' não existe");
        }

        foreach (var emu in Emulators)
        {
            if (emu.Source.Type is not ("github" or "forgejo" or "url" or "local"))
                warnings.Add($"{emu.Id}: source.type '{emu.Source.Type}' desconhecido");
            if (emu.Source.Type == "local" && string.IsNullOrWhiteSpace(emu.Source.Path))
                warnings.Add($"{emu.Id}: fonte local exige 'path'");
            if (emu.Source.Type is "github" or "forgejo" && (emu.Source.Repo is null || emu.Source.Asset is null))
                warnings.Add($"{emu.Id}: fonte github exige 'repo' e 'asset'");
            if (emu.Source.Type == "url" && emu.Source.Url is null)
                warnings.Add($"{emu.Id}: fonte url exige 'url'");
            if (string.IsNullOrWhiteSpace(emu.Executable))
                warnings.Add($"{emu.Id}: 'executable' vazio");

            if (emu.Controller is { } controller)
            {
                if (controller.Format is not ("ini" or "flat" or "qt"))
                    warnings.Add($"{emu.Id}: controller.format '{controller.Format}' desconhecido");
                if (string.IsNullOrWhiteSpace(controller.File) || Path.IsPathRooted(controller.File) || controller.File.Contains(".."))
                    warnings.Add($"{emu.Id}: controller.file precisa ser relativo à pasta do emulador");
            }

            foreach (var sys in emu.Systems.Where(s => GetSystem(s) is null))
                warnings.Add($"{emu.Id}: sistema '{sys}' não existe em systems.json");
            foreach (var dep in emu.Dependencies.Where(d => GetDependency(d) is null))
                warnings.Add($"{emu.Id}: dependência '{dep}' não existe em dependencies.json");

            foreach (var profile in emu.Launch)
            {
                foreach (var sys in profile.Systems.Where(s => !emu.Systems.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    warnings.Add($"{emu.Id}: perfil de launch cita '{sys}', que não está em 'systems'");
                if (profile.Core is not null && emu.Cores is null)
                    warnings.Add($"{emu.Id}: perfil usa core '{profile.Core}' mas não há 'cores'");
            }

            foreach (var sys in emu.Systems.Where(s => !emu.Launch.Any(p => p.Systems.Contains(s, StringComparer.OrdinalIgnoreCase))))
                warnings.Add($"{emu.Id}: sem perfil de launch para '{sys}'");
        }

        return warnings;
    }
}
