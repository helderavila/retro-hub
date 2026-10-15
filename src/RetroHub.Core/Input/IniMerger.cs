using System.Text;

namespace RetroHub.Core.Input;

/// <summary>
/// Changes only the given keys in an .ini/.cfg, preserving the rest of the file (comments, order, other sections).
/// </summary>
public static class IniMerger
{
    public static void Merge(string path, string format, IReadOnlyDictionary<string, Dictionary<string, string>> settings)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        foreach (var (section, values) in settings)
            foreach (var (key, value) in values)
                Set(lines, format, section, key, value);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static void Set(List<string> lines, string format, string section, string key, string value)
    {
        var line = format switch
        {
            "flat" => $"{key} = \"{value}\"",  // retroarch.cfg
            "qt" => $"{key}={value}",           // QSettings (Qt emulators): no spaces, as the emulator itself writes
            _ => $"{key} = {value}",
        };

        int start, end;
        if (section.Length == 0)
        {
            start = 0;
            end = lines.Count;
        }
        else
        {
            var header = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                    lines.Add("");
                lines.Add($"[{section}]");
                lines.Add(line);
                return;
            }

            start = header + 1;
            end = lines.FindIndex(start, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
        }

        for (var i = start; i < end; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;
            var eq = lines[i].IndexOf('=');
            if (eq > 0 && lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = line;
                return;
            }
        }

        // Inserts at the end of the section, before the blank lines separating it from the next.
        var at = end;
        while (at > start && lines[at - 1].Trim().Length == 0)
            at--;
        lines.Insert(at, line);
    }
}
