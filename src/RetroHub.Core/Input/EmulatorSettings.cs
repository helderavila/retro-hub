using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Input;

/// <summary>
/// Graphics and achievements settings written directly into the emulators' configuration files
/// (only the keys involved; the rest of the file stays as is).
/// </summary>
public static class EmulatorSettings
{
    public const string Original = "Original";
    public const string Enhanced = "Enhanced";
    public const string Maximum = "Maximum";

    /// <summary>Which option (e.g. "2x", "Maximum") is currently active in the emulator file, or null if none matches.</summary>
    public static string? Current(InstalledEmulator installed, GraphicsConfig graphics, GraphicsOption? option)
    {
        if (option is null)
            return null;
        var values = Read(installed, graphics);
        if (values is null)
            return null;
        return option.Values.FirstOrDefault(choice => Matches(values, choice.Value)).Key;
    }

    public static void Apply(InstalledEmulator installed, GraphicsConfig graphics, GraphicsOption option, string choice)
    {
        if (!option.Values.TryGetValue(choice, out var settings))
            throw new ArgumentException(L.F("Unknown option: {0}", choice));
        IniMerger.Merge(SafePath(installed, graphics.File), graphics.Format, settings);
    }

    public static bool IsOn(InstalledEmulator installed, GraphicsConfig graphics, GraphicsToggle toggle)
    {
        var values = Read(installed, graphics);
        if (values is null || !Matches(values, toggle.On))
            return false;
        return toggle.File is null || File.Exists(SafePath(installed, toggle.File.Path));
    }

    public static void SetToggle(InstalledEmulator installed, GraphicsConfig graphics, GraphicsToggle toggle, bool on)
    {
        IniMerger.Merge(SafePath(installed, graphics.File), graphics.Format, on ? toggle.On : toggle.Off);
        if (toggle.File is { } file)
        {
            var path = SafePath(installed, file.Path);
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Enables achievements. User/password only go to emulators that accept them via the file (RetroArch).</summary>
    public static string EnableAchievements(InstalledEmulator installed, AchievementsConfig config, string user, string password)
    {
        var settings = config.Enable.ToDictionary(
            section => section.Key,
            section => section.Value.ToDictionary(kv => kv.Key, kv => kv.Value.Replace("{user}", user).Replace("{password}", password)));
        var path = SafePath(installed, config.File);
        IniMerger.Merge(path, config.Format, settings);
        return path;
    }

    private static bool Matches(Dictionary<(string, string), string> values, Dictionary<string, Dictionary<string, string>> expected) =>
        expected.All(section => section.Value.All(kv =>
            values.TryGetValue((section.Key, kv.Key), out var current) &&
            current.Trim('"').Equals(kv.Value.Trim('"'), StringComparison.OrdinalIgnoreCase)));

    private static string SafePath(InstalledEmulator installed, string relative)
    {
        var root = Path.GetFullPath(installed.InstallDir) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(installed.InstallDir, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.F("File outside the emulator folder: {0}", relative));
        return path;
    }

    private static Dictionary<(string, string), string>? Read(InstalledEmulator installed, GraphicsConfig graphics)
    {
        var path = SafePath(installed, graphics.File);
        return File.Exists(path) ? Read(path, graphics.Format) : null;
    }

    /// <summary>Reads an .ini ("[Section]" + "key = value") or flat .cfg ("key = \"value\"") into (section, key) → value.</summary>
    private static Dictionary<(string, string), string> Read(string path, string format)
    {
        var values = new Dictionary<(string, string), string>(new SectionKeyComparer());
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']') && format != "flat") { section = line[1..^1]; continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            values[(section, line[..eq].Trim())] = line[(eq + 1)..].Trim().Trim('"');
        }
        return values;
    }

    private sealed class SectionKeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) v) =>
            HashCode.Combine(v.Item1.ToLowerInvariant(), v.Item2.ToLowerInvariant());
    }
}
