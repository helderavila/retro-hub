using System.IO.Compression;
using System.Text.RegularExpressions;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Metadata;
using RetroHub.Core.Models;

namespace RetroHub.Core.Saves;

/// <param name="Slot">Slot number (RetroArch: 0 = ".state"). null when the emulator does not number them (e.g. "resume").</param>
/// <param name="CanLoadDirectly">The emulator can start directly in this state from the command line.</param>
public sealed record SaveState(string Path, string EmulatorId, string EmulatorName, int? Slot, DateTimeOffset Modified, long Size, bool CanLoadDirectly)
{
    public string Label => Slot is { } s ? L.F("Slot {0}", s) : System.IO.Path.GetFileNameWithoutExtension(Path).Contains("resume", StringComparison.OrdinalIgnoreCase) ? L.T("On exit") : "Save";
}

/// <summary>Finds a game's save states in the installed emulators' folders (per the manifests).</summary>
public sealed class SaveStateService(ManifestRepository manifests, LibraryDatabase db)
{
    public IReadOnlyList<SaveState> Find(Game game)
    {
        var serial = game.Serial ?? GameMetadata.FromJson(game.MetadataJson)?.Serial;
        var romName = Path.GetFileNameWithoutExtension(game.Path);
        var states = new List<SaveState>();

        foreach (var installed in db.GetInstalledEmulators())
        {
            var manifest = manifests.GetEmulator(installed.Id);
            if (manifest?.Saves is not { } saves || !manifest.Systems.Contains(game.SystemId, StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var location in saves.States)
            {
                if ((location.Pattern.Contains("{serial") && serial is null))
                    continue;

                var dir = Path.Combine(installed.InstallDir, location.Dir);
                if (!Directory.Exists(dir))
                    continue;

                var pattern = location.Pattern
                    .Replace("{serial}", serial)
                    .Replace("{serialNoDash}", serial?.Replace("-", ""))
                    .Replace("{romName}", EscapeWildcards(romName));

                foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var info = new FileInfo(file);
                    states.Add(new SaveState(file, manifest.Id, manifest.Name, ParseSlot(location.Slot, file),
                        info.LastWriteTime, info.Length, saves.LoadStateArgs is not null));
                }
            }
        }

        return states.OrderByDescending(s => s.Modified).ToList();
    }

    /// <summary>The game's most recent save ("Continue"), if any.</summary>
    public SaveState? Latest(Game game) => Find(game).FirstOrDefault();

    /// <summary>Save thumbnail, when the emulator writes one: PCSX2 .p2s (zip with Screenshot.png) or a .png next to it (RetroArch).</summary>
    public static byte[]? Thumbnail(SaveState state)
    {
        try
        {
            if (File.Exists(state.Path + ".png"))
                return File.ReadAllBytes(state.Path + ".png");

            if (state.Path.EndsWith(".p2s", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(state.Path);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("Screenshot.png", StringComparison.OrdinalIgnoreCase));
                if (entry is null) return null;
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return null;
    }

    private static int? ParseSlot(string? regex, string file)
    {
        if (regex is null) return null;
        var m = Regex.Match(Path.GetFileName(file), regex, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Value.Length == 0 ? 0 : int.TryParse(m.Groups[1].Value, out var slot) ? slot : null;
    }

    /// <summary>ROM names with [ ] must not become wildcards in EnumerateFiles.</summary>
    private static string EscapeWildcards(string name) => name.Replace("*", "?");
}
