using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Install;

/// <summary>
/// User-added emulators: they become manifests in <see cref="AppPaths.UserManifests"/>\emulators.
/// Two ways: point to an .exe already on the computer ("local" source) or import a complete .json manifest.
/// </summary>
public sealed partial class CustomEmulators(AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    private string Folder => Path.Combine(paths.UserManifests, "emulators");

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex ValidId();

    /// <summary>"My Emulator 2" → "my-emulator-2" (without reusing an existing id).</summary>
    public string NewId(string name)
    {
        var baseId = NonSlug().Replace(RemoveAccents(name).ToLowerInvariant(), "-").Trim('-');
        if (baseId.Length == 0) baseId = "emulator";
        var id = baseId;
        for (var i = 2; manifests.GetEmulator(id) is not null; i++)
            id = $"{baseId}-{i}";
        return id;
    }

    /// <summary>
    /// Creates a "local" emulator from an .exe the user already has. Nothing in its folder is copied or changed.
    /// <paramref name="args"/> accepts {rom}, {romDir}, {romName}, {emuDir}.
    /// </summary>
    public EmulatorManifest CreateLocal(string name, string executablePath, IReadOnlyList<string> systems, string args)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(L.T("Give the emulator a name."));
        executablePath = Util.Platform.ResolveAppBundle(executablePath); // macOS: "Foo.app" → its binary
        if (!File.Exists(executablePath)) throw new FileNotFoundException(L.T("Choose the emulator's .exe file."), executablePath);
        if (systems.Count == 0) throw new ArgumentException(L.T("Select at least one system."));
        var unknown = systems.Where(s => manifests.GetSystem(s) is null).ToList();
        if (unknown.Count > 0) throw new ArgumentException(L.F("Unknown system: {0}", string.Join(", ", unknown)));
        if (!args.Contains("{rom}")) throw new ArgumentException(L.T("The arguments must include {rom} (the game file)."));

        var manifest = new EmulatorManifest
        {
            Id = NewId(name),
            Name = name.Trim(),
            Description = "Added by you.",
            Systems = [.. systems],
            Source = new SourceSpec { Type = "local", Path = executablePath },
            Executable = Path.GetFileName(executablePath),
            Launch = [new LaunchProfile { Systems = [.. systems], Args = args.Trim() }],
        };
        Save(manifest);
        manifests.Reload();
        return manifests.GetEmulator(manifest.Id)!;
    }

    /// <summary>Copies a .json manifest to the user's folder (same format as the ones shipped with the app).</summary>
    public EmulatorManifest Import(string jsonPath)
    {
        var manifest = manifests.ReadEmulator(jsonPath);
        if (!manifest.SupportedHere)
            throw new InvalidDataException(L.T("This manifest has no download for this system (add a \"macos\" block)."));
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException(L.T("The manifest must have \"id\" and \"name\"."));
        // The id becomes a file name: lowercase letters, digits and hyphens only.
        if (!ValidId().IsMatch(manifest.Id))
            throw new InvalidDataException(L.F("Invalid id: '{0}' (use lowercase letters, numbers and hyphens).", manifest.Id));
        var unknown = manifest.Systems.Where(s => manifests.GetSystem(s) is null).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException(L.F("Unknown system: {0}. Add the system in {1} first.", string.Join(", ", unknown), Path.Combine(paths.UserManifests, "systems")));

        File.Copy(jsonPath, Path.Combine(Folder, manifest.Id + ".json"), overwrite: true);
        manifests.Reload();
        return manifests.GetEmulator(manifest.Id)!;
    }

    /// <summary>Removes the emulator from Retro Hub: deletes the user manifest and the record. The user's "local" folders stay intact.</summary>
    public void Remove(EmulatorManifest manifest, EmulatorInstaller installer)
    {
        if (manifest.UserManifestPath is not { } file)
            throw new InvalidOperationException(L.F("{0} ships with Retro Hub and cannot be removed here.", manifest.Name));
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
