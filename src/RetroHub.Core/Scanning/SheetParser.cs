using System.Text.RegularExpressions;

namespace RetroHub.Core.Scanning;

/// <summary>Lê .cue, .gdi e .m3u e devolve os caminhos (absolutos) que eles referenciam.</summary>
public static partial class SheetParser
{
    private const long MaxSheetSize = 1 << 20;

    [GeneratedRegex(@"^\s*FILE\s+(?:""(?<f>[^""]+)""|(?<f>\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex CueFile();

    [GeneratedRegex(@"^\s*\d+\s+\d+\s+\d+\s+\d+\s+(?:""(?<f>[^""]+)""|(?<f>\S+))")]
    private static partial Regex GdiTrack();

    public static bool IsSheet(string extension) => extension is ".cue" or ".gdi" or ".m3u";

    /// <summary>Todos os arquivos que formam o jogo: o próprio arquivo + trilhas/discos referenciados (recursivo: .m3u → .cue → .bin).</summary>
    public static IReadOnlyList<string> FilesOfGame(string path)
    {
        var files = new List<string>();
        var pending = new Queue<string>([Path.GetFullPath(path)]);
        while (pending.Count > 0 && files.Count < 256)
        {
            var file = pending.Dequeue();
            if (files.Contains(file, StringComparer.OrdinalIgnoreCase) || !File.Exists(file))
                continue;
            files.Add(file);
            if (IsSheet(Path.GetExtension(file).ToLowerInvariant()))
                foreach (var reference in References(file))
                    pending.Enqueue(reference);
        }
        return files;
    }

    public static IEnumerable<string> References(string sheetPath)
    {
        var info = new FileInfo(sheetPath);
        if (!info.Exists || info.Length > MaxSheetSize)
            yield break;

        var dir = info.DirectoryName!;
        var ext = info.Extension.ToLowerInvariant();

        foreach (var raw in File.ReadLines(sheetPath))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            string? file = ext switch
            {
                ".cue" => CueFile().Match(line) is { Success: true } m ? m.Groups["f"].Value : null,
                ".gdi" => GdiTrack().Match(line) is { Success: true } m ? m.Groups["f"].Value : null,
                ".m3u" => line.StartsWith('#') ? null : line,
                _ => null,
            };

            if (file is not null && file.IndexOfAny(Path.GetInvalidPathChars()) < 0)
                yield return Path.GetFullPath(Path.Combine(dir, file));
        }
    }
}
