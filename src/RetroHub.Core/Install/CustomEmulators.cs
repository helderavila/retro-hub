using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Install;

/// <summary>
/// Emuladores adicionados pelo usuário: viram manifestos em <see cref="AppPaths.UserManifests"/>\emulators.
/// Dois jeitos: apontar um .exe que já está no computador (fonte "local") ou importar um manifesto .json completo.
/// </summary>
public sealed partial class CustomEmulators(AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    private string Folder => Path.Combine(paths.UserManifests, "emulators");

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex ValidId();

    /// <summary>"Meu Emulador 2" → "meu-emulador-2" (sem repetir um id que já existe).</summary>
    public string NewId(string name)
    {
        var baseId = NonSlug().Replace(RemoveAccents(name).ToLowerInvariant(), "-").Trim('-');
        if (baseId.Length == 0) baseId = "emulador";
        var id = baseId;
        for (var i = 2; manifests.GetEmulator(id) is not null; i++)
            id = $"{baseId}-{i}";
        return id;
    }

    /// <summary>
    /// Cria um emulador "local" a partir de um .exe que o usuário já tem. Não copia nem altera nada na pasta dele.
    /// <paramref name="args"/> aceita {rom}, {romDir}, {romName}, {emuDir}.
    /// </summary>
    public EmulatorManifest CreateLocal(string name, string executablePath, IReadOnlyList<string> systems, string args)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Dê um nome ao emulador.");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Escolha o arquivo .exe do emulador.", executablePath);
        if (systems.Count == 0) throw new ArgumentException("Marque pelo menos um sistema.");
        var unknown = systems.Where(s => manifests.GetSystem(s) is null).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Sistema desconhecido: {string.Join(", ", unknown)}");
        if (!args.Contains("{rom}")) throw new ArgumentException("Os argumentos precisam ter {rom} (o arquivo do jogo).");

        var manifest = new EmulatorManifest
        {
            Id = NewId(name),
            Name = name.Trim(),
            Description = "Adicionado por você.",
            Systems = [.. systems],
            Source = new SourceSpec { Type = "local", Path = executablePath },
            Executable = Path.GetFileName(executablePath),
            Launch = [new LaunchProfile { Systems = [.. systems], Args = args.Trim() }],
        };
        Save(manifest);
        manifests.Reload();
        return manifests.GetEmulator(manifest.Id)!;
    }

    /// <summary>Copia um manifesto .json para a pasta do usuário (o mesmo formato dos que vêm com o app).</summary>
    public EmulatorManifest Import(string jsonPath)
    {
        var manifest = ManifestRepository.ReadEmulator(jsonPath);
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("O manifesto precisa ter \"id\" e \"name\".");
        // O id vira nome de arquivo: só minúsculas, números e hífen.
        if (!ValidId().IsMatch(manifest.Id))
            throw new InvalidDataException($"id inválido: '{manifest.Id}' (use letras minúsculas, números e hífen).");
        var unknown = manifest.Systems.Where(s => manifests.GetSystem(s) is null).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"Sistema desconhecido: {string.Join(", ", unknown)}. Adicione o sistema em {Path.Combine(paths.UserManifests, "systems")} primeiro.");

        File.Copy(jsonPath, Path.Combine(Folder, manifest.Id + ".json"), overwrite: true);
        manifests.Reload();
        return manifests.GetEmulator(manifest.Id)!;
    }

    /// <summary>Tira o emulador do Retro Hub: apaga o manifesto do usuário e o registro. Pastas "local" do usuário ficam intactas.</summary>
    public void Remove(EmulatorManifest manifest, EmulatorInstaller installer)
    {
        if (manifest.UserManifestPath is not { } file)
            throw new InvalidOperationException($"{manifest.Name} vem com o Retro Hub e não pode ser removido daqui.");
        if (db.GetInstalledEmulators().Any(e => string.Equals(e.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
            installer.Uninstall(manifest);
        File.Delete(file);
        manifests.Reload();
    }

    private void Save(EmulatorManifest manifest)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(ManifestRepository.JsonOptions)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(Path.Combine(Folder, manifest.Id + ".json"), json, new UTF8Encoding(false));
    }

    private static string RemoveAccents(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}
