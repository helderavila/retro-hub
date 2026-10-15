using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RetroHub.Core.Install;

public static class ArchiveExtractor
{
    /// <summary>Extrai .zip (nativo) ou .7z/.rar/.tar.* (SharpCompress) para <paramref name="destination"/>.</summary>
    public static void Extract(string archivePath, string destination, bool stripRoot)
    {
        Directory.CreateDirectory(destination);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
        }
        else
        {
            // Extração sequencial (eficiente em 7z "sólido"); o SharpCompress bloqueia path traversal.
            ArchiveFactory.WriteToDirectory(archivePath, destination,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }

        if (stripRoot)
            StripSingleRoot(destination);
    }

    /// <summary>Se a pasta contém apenas uma subpasta (ex.: "RetroArch-Win64/"), sobe o conteúdo dela.</summary>
    private static void StripSingleRoot(string dir)
    {
        if (Directory.GetFiles(dir).Length != 0)
            return;
        var dirs = Directory.GetDirectories(dir);
        if (dirs.Length != 1)
            return;

        var temp = dir.TrimEnd(Path.DirectorySeparatorChar) + "_root";
        Directory.Move(dirs[0], temp);
        Directory.Delete(dir, recursive: true);
        Directory.Move(temp, dir);
    }

    /// <param name="preserve">Caminhos relativos que não são sobrescritos se já existirem (configurações do usuário).</param>
    public static void CopyDirectory(string source, string destination, IReadOnlyCollection<string>? preserve = null)
    {
        var keep = new HashSet<string>((preserve ?? []).Select(p => p.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            // Arquivo listado, ou qualquer coisa dentro de uma pasta listada (ex.: uma pasta "user" de modo portátil).
            if (File.Exists(target) && keep.Any(k => relative.Equals(k, StringComparison.OrdinalIgnoreCase)
                                                    || relative.StartsWith(k.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                continue;
            File.Copy(file, target, overwrite: true);
        }
    }
}
