using RetroHub.Core.Models;

namespace RetroHub.Core.Input;

/// <summary>
/// Ajustes de imagem e conquistas gravados direto nos arquivos de configuração dos emuladores
/// (só as chaves envolvidas; o resto do arquivo fica como está).
/// </summary>
public static class EmulatorSettings
{
    public const string Original = "Original";
    public const string Enhanced = "Melhorado";
    public const string Maximum = "Máximo";

    /// <summary>Qual opção (ex.: "2x", "Máximo") está ativa hoje no arquivo do emulador, ou null se nenhuma bate.</summary>
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
            throw new ArgumentException($"Opção desconhecida: {choice}");
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

    /// <summary>Liga as conquistas. Usuário/senha só vão para emuladores que aceitam pelo arquivo (RetroArch).</summary>
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
            throw new InvalidDataException($"Arquivo fora da pasta do emulador: {relative}");
        return path;
    }

    private static Dictionary<(string, string), string>? Read(InstalledEmulator installed, GraphicsConfig graphics)
    {
        var path = SafePath(installed, graphics.File);
        return File.Exists(path) ? Read(path, graphics.Format) : null;
    }

    /// <summary>Lê um .ini ("[Seção]" + "chave = valor") ou .cfg plano ("chave = \"valor\"") em (seção, chave) → valor.</summary>
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
