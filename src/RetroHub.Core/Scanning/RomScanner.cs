using System.IO.Compression;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Scanning;

/// <param name="Fingerprint">Identifies the content: copies of the same game in different folders have the same value.</param>
public sealed record ScannedRom(string Path, string SystemId, string Title, string? Serial, string? Crc32, long SizeBytes, string? Fingerprint = null);

public sealed record ScanProgress(int FilesSeen, int Identified, string? Current);

/// <summary>File that looks like a ROM but is not a game (e.g. a bonus video DVD).</summary>
public sealed record RejectedFile(string Path, string Reason);

public sealed record ScanReport(IReadOnlyList<ScannedRom> Roms, IReadOnlyList<string> Unidentified, IReadOnlyList<RejectedFile> Rejected);

/// <summary>
/// Layered identification:
///   1. extension unique to one system (.sfc → snes);
///   2. file content (disc header, extension inside the .zip);
///   3. folder name ("ps2/", "PlayStation 2/").
/// Files referenced by .cue/.gdi/.m3u do not become separate games.
/// </summary>
public sealed class RomScanner(ManifestRepository manifests)
{
    private const long MaxCrcSize = 64L * 1024 * 1024;

    private static readonly HashSet<string> DiscExtensions =
    [
        ".iso", ".bin", ".img", ".cue", ".chd", ".gdi", ".cdi", ".m3u", ".rvz", ".wia",
        ".gcz", ".gcm", ".wbfs", ".cso", ".zso", ".pbp", ".ecm",
    ];

    private static readonly HashSet<string> ArchiveExtensions = [".zip", ".7z"];

    public ScanReport Scan(IEnumerable<string> roots, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var roms = new List<ScannedRom>();
        var unidentified = new List<string>();
        var rejected = new List<RejectedFile>();
        var seen = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            var files = Directory.EnumerateFiles(root, "*", options).ToList();
            var referenced = CollectReferencedFiles(files);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                seen++;
                if (seen % 25 == 0)
                    progress?.Report(new ScanProgress(seen, roms.Count, file));

                if (referenced.Contains(file))
                    continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!IsCandidateExtension(ext))
                    continue;

                if (Identify(file, root, out var rejectReason) is { } rom)
                    roms.Add(rom);
                else if (rejectReason is not null)
                    rejected.Add(new RejectedFile(Path.GetFullPath(file), rejectReason));
                else
                    unidentified.Add(file);
            }
        }

        progress?.Report(new ScanProgress(seen, roms.Count, null));
        return new ScanReport(roms, unidentified, rejected);
    }

    /// <summary>Identifies a single file. <paramref name="root"/> limits the folder name lookup.</summary>
    public ScannedRom? Identify(string path, string? root = null) => Identify(path, root, out _);

    /// <param name="rejectReason">Set when the file was recognized as something that is not a game.</param>
    public ScannedRom? Identify(string path, string? root, out string? rejectReason)
    {
        rejectReason = null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var candidates = manifests.SystemsForExtension(ext);
        string? systemId = null;
        string? serial = null;

        if (ext == ".zip")
        {
            systemId = IdentifyZipContents(path) ?? MatchFolderAlias(path, root, manifests.Systems);
        }
        else if (ext == ".7z")
        {
            systemId = MatchFolderAlias(path, root, manifests.Systems);
        }
        else
        {
            if (candidates.Count == 0)
                return null;

            if (DiscExtensions.Contains(ext))
            {
                var disc = DiscInspector.Inspect(path);
                if (disc.IsVideoDisc)
                {
                    rejectReason = L.T("Video DVD (VIDEO_TS), not a game disc");
                    return null;
                }
                serial = disc.Serial;
                if (disc.SystemId is { } id && manifests.GetSystem(id) is not null)
                    systemId = id;
            }

            systemId ??= candidates.Count == 1 ? candidates[0].Id : MatchFolderAlias(path, root, candidates);
        }

        if (systemId is null)
            return null;

        // A 16-digit title ID in brackets in the file name ("Game [0100ABCD12345000][v0].ext") becomes the serial.
        if (serial is null && System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path), @"\[(0100[0-9A-Fa-f]{12})\]") is { Success: true } tid)
            serial = tid.Groups[1].Value.ToUpperInvariant();

        var size = new FileInfo(path).Length;
        var crc = !DiscExtensions.Contains(ext) && !ArchiveExtensions.Contains(ext) && size <= MaxCrcSize
            ? FileHasher.Crc32(path)
            : null;

        return new ScannedRom(Path.GetFullPath(path), systemId, TitleCleaner.Clean(path), serial, crc, size, Fingerprint(path, ext, crc, size));
    }

    /// <summary>Cartridges: full CRC32. Discs: file samples (for .cue/.gdi, of the first track).</summary>
    private static string? Fingerprint(string path, string ext, string? crc, long size)
    {
        if (crc is not null)
            return $"crc:{crc}:{size}";

        try
        {
            if (ext is ".cue" or ".gdi")
                return SheetParser.References(path).FirstOrDefault(File.Exists) is { } track ? FileHasher.SampleFingerprint(track) : null;
            if (ext == ".m3u")
                return null; // playlist: its discs are already identified individually
            return FileHasher.SampleFingerprint(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool IsCandidateExtension(string ext) =>
        ArchiveExtensions.Contains(ext) || manifests.SystemsForExtension(ext).Count > 0;

    /// <summary>Looks at the extensions inside the .zip; if they point to a single system, that is it.</summary>
    private string? IdentifyZipContents(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var systems = zip.Entries
                .Select(e => Path.GetExtension(e.FullName).ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Distinct()
                .Select(e => manifests.SystemsForExtension(e))
                .Where(list => list.Count == 1)
                .Select(list => list[0].Id)
                .Distinct()
                .ToList();
            return systems.Count == 1 ? systems[0] : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string? MatchFolderAlias(string path, string? root, IEnumerable<SystemDefinition> candidates)
    {
        var list = candidates.ToList();
        var stop = root is null ? null : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));

        while (dir is not null)
        {
            var name = Normalize(Path.GetFileName(dir));
            var match = list.FirstOrDefault(s =>
                Normalize(s.Id) == name || s.FolderAliases.Any(a => Normalize(a) == name));
            if (match is not null)
                return match.Id;

            if (stop is not null && string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), stop, StringComparison.OrdinalIgnoreCase))
                break;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static string Normalize(string name) =>
        new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static HashSet<string> CollectReferencedFiles(IEnumerable<string> files)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!SheetParser.IsSheet(Path.GetExtension(file).ToLowerInvariant()))
                continue;
            try
            {
                foreach (var r in SheetParser.References(file))
                    referenced.Add(r);
            }
            catch (IOException) { }
        }
        return referenced;
    }
}
