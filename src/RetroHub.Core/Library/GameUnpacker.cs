using RetroHub.Core.Install;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;
using SharpCompress.Archives;

namespace RetroHub.Core.Library;

/// <summary>Whether a compressed game is worth extracting.</summary>
public enum UnpackAdvice
{
    /// <summary>Its emulator cannot open .zip/.7z (discs on DuckStation, PCSX2, Dolphin, PPSSPP): extract to play.</summary>
    Recommended,
    /// <summary>Its emulator opens it compressed (RetroArch); extracting only helps verification and covers.</summary>
    Optional,
    /// <summary>The system needs the archive itself (arcade ROM sets): never extracted.</summary>
    KeepCompressed,
}

/// <param name="UnpackedBytes">Size once extracted (null if the archive cannot be read).</param>
/// <param name="TargetDir">Where it goes: &lt;games folder&gt;/&lt;system folder&gt;.</param>
public sealed record CompressedGame(Game Game, SystemDefinition? System, UnpackAdvice Advice, long? UnpackedBytes, string TargetDir);

/// <summary>A .zip/.7z to extract: a game already in the library (<paramref name="GameId"/>) or a file picked from anywhere.</summary>
/// <param name="SystemId">null = detect it (from the archive contents, then from the extracted disc's header).</param>
public sealed record UnpackItem(string ArchivePath, long? GameId, string? SystemId);

/// <param name="NewPath">The game's file after extracting; null when it failed (see <paramref name="Error"/>).</param>
public sealed record UnpackResult(UnpackItem Item, string? NewPath, string? Error);

/// <summary>
/// Extracts games kept as .zip/.7z into the games folder (&lt;folder&gt;/&lt;system&gt;/) and points the library at the extracted file.
/// The game keeps its id, so play time, favorites, collections and cover stay. Each game is all or nothing: it is extracted
/// next to its destination first and only moved into place when complete; a failure or cancel leaves the archive as it was.
/// </summary>
public sealed class GameUnpacker(ManifestRepository manifests, LibraryDatabase db, RomScanner scanner)
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z" };

    /// <summary>Files archive tools add that are not part of the game.</summary>
    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase) { "__MACOSX", ".DS_Store", "Thumbs.db", "desktop.ini" };

    public static bool IsCompressed(string path) => ArchiveExtensions.Contains(Path.GetExtension(path));

    /// <summary>The library's compressed games, with advice and destination.</summary>
    public IReadOnlyList<CompressedGame> FindCompressed(IEnumerable<Game> games, string gamesRoot) =>
        games.Where(g => IsCompressed(g.Path) && File.Exists(g.Path)).Select(g => Describe(g, gamesRoot)).ToList();

    /// <summary>Whether a compressed game of this system is worth extracting (no file access).</summary>
    public UnpackAdvice Advice(string systemId) => Advise(manifests.GetSystem(systemId));

    /// <summary>
    /// An archive picked from anywhere (not in the library yet): the system comes from what is inside it or from its folder name;
    /// unknown (empty SystemId) when that is not enough — it is then detected from the extracted disc, or the user picks it.
    /// </summary>
    public CompressedGame DescribeFile(string path, string gamesRoot)
    {
        var full = Path.GetFullPath(path);
        var systemId = scanner.Identify(full)?.SystemId ?? "";
        return Describe(new Game { Path = full, SystemId = systemId, Title = TitleCleaner.Clean(full) }, gamesRoot);
    }

    public CompressedGame Describe(Game game, string gamesRoot)
    {
        var system = manifests.GetSystem(game.SystemId);
        return new CompressedGame(game, system, Advise(system), UnpackedSize(game.Path), TargetDir(gamesRoot, system, game.SystemId));
    }

    private UnpackAdvice Advise(SystemDefinition? system)
    {
        // Unknown system: almost always a disc (cartridges are recognized by the extension inside the archive).
        if (system is null)
            return UnpackAdvice.Recommended;
        // Arcade sets are .zip by definition: the system lists the archive extension itself.
        if (system.Extensions.Any(e => ArchiveExtensions.Contains(e)))
            return UnpackAdvice.KeepCompressed;
        var emulator = system.DefaultEmulator is { } id ? manifests.GetEmulator(id) : null;
        return emulator?.OpensArchives == true ? UnpackAdvice.Optional : UnpackAdvice.Recommended;
    }

    /// <summary>&lt;games folder&gt;/&lt;first folder alias, e.g. "ps2"&gt;: a folder the scanner recognizes.</summary>
    private static string TargetDir(string gamesRoot, SystemDefinition? system, string systemId) =>
        Path.Combine(gamesRoot, system?.FolderAliases.FirstOrDefault() ?? systemId);

    private static long? UnpackedSize(string path)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path);
            return archive.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or NotSupportedException
                                       or SharpCompress.Common.ArchiveException)
        {
            return null;
        }
    }

    /// <param name="removeOriginal">Sends the archive away after success (the app's Trash/Recycle Bin); null = keep it
    /// (it is then left out of future scans, so it does not come back as a duplicate).</param>
    public Task<IReadOnlyList<UnpackResult>> UnpackAsync(IReadOnlyList<Game> games, string gamesRoot, Action<string>? removeOriginal,
        IProgress<InstallUpdate> progress, CancellationToken ct) =>
        UnpackAsync(games.Select(g => new UnpackItem(g.Path, g.Id, g.SystemId)).ToList(), gamesRoot, removeOriginal, progress, ct);

    /// <summary>Library games and/or archives picked from anywhere. New games are added to the library once extracted.</summary>
    public async Task<IReadOnlyList<UnpackResult>> UnpackAsync(IReadOnlyList<UnpackItem> items, string gamesRoot, Action<string>? removeOriginal,
        IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        var results = new List<UnpackResult>();
        Directory.CreateDirectory(gamesRoot);
        EnsureScanned(gamesRoot);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var name = Path.GetFileName(item.ArchivePath);
            ct.ThrowIfCancellationRequested();
            progress.Report(new InstallUpdate(L.F("{0} of {1}: {2}", i + 1, items.Count, name), 0, $"→ {name}"));
            try
            {
                var path = await Task.Run(() => Unpack(item, gamesRoot, removeOriginal, progress, ct), ct);
                results.Add(new UnpackResult(item, path, null));
                progress.Report(new InstallUpdate(null, 1, $"✓ {path}"));
            }
            catch (OperationCanceledException)
            {
                progress.Report(InstallUpdate.Line(L.F("· {0}: cancelled, the archive is untouched", name)));
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                                           or NotSupportedException or SharpCompress.Common.ArchiveException)
            {
                results.Add(new UnpackResult(item, null, ex.Message));
                progress.Report(InstallUpdate.Line($"✗ {name}: {ex.Message}"));
            }
        }
        return results;
    }

    private string Unpack(UnpackItem item, string gamesRoot, Action<string>? removeOriginal, IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        var archivePath = item.ArchivePath;
        if (!File.Exists(archivePath))
            throw new FileNotFoundException(L.F("File not found: {0}", archivePath));

        var systemId = string.IsNullOrEmpty(item.SystemId) ? null : item.SystemId;
        if (systemId is not null)
            CheckExtractable(systemId);

        // Extracted next to the destination (same disk: the final move is instant) and only moved in when complete.
        // Unknown system: next to the games folder, until the extracted disc says which system it is.
        var stagingParent = systemId is null ? gamesRoot : TargetDir(gamesRoot, manifests.GetSystem(systemId), systemId);
        Directory.CreateDirectory(stagingParent);
        var needed = UnpackedSize(archivePath);
        if (needed is { } bytes && FreeSpace(stagingParent) is { } free && free < bytes + (64L << 20))
            throw new IOException(L.F("not enough disk space ({0} needed, {1} free)", FormatSize(bytes), FormatSize(free)));

        var staging = Path.Combine(stagingParent, ".retrohub-unpack-" + Guid.NewGuid().ToString("N"));
        string? placed = null;
        var registered = false;
        try
        {
            Extract(archivePath, staging, needed, progress, ct);
            RemoveJunk(staging);

            if (systemId is null)
            {
                systemId = FindGameFile(staging, null, gamesRoot)?.SystemId
                    ?? throw new InvalidDataException(L.T("could not tell which system this game is for: choose it in the list"));
                CheckExtractable(systemId);
                progress.Report(InstallUpdate.Line($"  {L.T(manifests.GetSystem(systemId)?.Name ?? systemId)}"));
            }

            var targetDir = TargetDir(gamesRoot, manifests.GetSystem(systemId), systemId);
            Directory.CreateDirectory(targetDir);
            placed = Place(staging, targetDir, Path.GetFileNameWithoutExtension(archivePath));

            var rom = FindGameFile(placed, systemId, gamesRoot)
                ?? throw new InvalidDataException(L.T("no game for this system inside the archive"));
            if (item.GameId is { } id)
            {
                db.ReplaceGameFile(id, rom, excludeOldPath: removeOriginal is null);
            }
            else
            {
                db.UpsertScanned([rom], source: "manual");
                if (removeOriginal is null)
                    db.ExcludePath(archivePath); // a kept archive inside a ROM folder must not come back as a second copy
            }
            registered = true;

            if (removeOriginal is not null)
            {
                try { removeOriginal(archivePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    progress.Report(InstallUpdate.Line(L.F("! could not move the archive away: {0}", ex.Message)));
                }
            }
            return rom.Path;
        }
        catch when (placed is not null && !registered)
        {
            TryDelete(placed); // a game is never left extracted but not in the library
            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private void CheckExtractable(string systemId)
    {
        if (Advice(systemId) == UnpackAdvice.KeepCompressed)
            throw new InvalidOperationException(L.T("this system plays the archive itself (arcade ROM set)"));
    }

    private static void Extract(string archivePath, string destination, long? total, IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long done = 0;
        var lastReport = 0L;
        var buffer = new byte[1 << 20];

        void Write(string? key, Func<Stream> open)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(key))
                return;
            var target = Path.GetFullPath(Path.Combine(destination, key));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException(L.F("unsafe path inside the archive: {0}", key));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using var source = open();
            using var output = File.Create(target);
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                done += read;
                if (total > 0 && Environment.TickCount64 - lastReport > 150)
                {
                    lastReport = Environment.TickCount64;
                    progress.Report(new InstallUpdate(null, Math.Min(0.99, (double)done / total.Value), null));
                }
            }
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        if (archive.IsSolid || archive.Type == SharpCompress.Common.ArchiveType.SevenZip)
        {
            // Sequential reader: solid archives are decompressed once instead of once per file.
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
                if (!reader.Entry.IsDirectory)
                    Write(reader.Entry.Key, reader.OpenEntryStream);
        }
        else
        {
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                Write(entry.Key, entry.OpenEntryStream);
        }
    }

    private static void RemoveJunk(string dir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).ToList())
        {
            if (!JunkNames.Contains(Path.GetFileName(entry)) && !Path.GetFileName(entry).StartsWith("._", StringComparison.Ordinal))
                continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else if (File.Exists(entry)) File.Delete(entry);
        }
    }

    /// <summary>
    /// One file → straight into the system folder. Several files (.cue + .bin tracks, multi-disc) → a folder named after the
    /// archive (or the archive's own single root folder). Never overwrites: a name already taken gets " (2)", " (3)"…
    /// Returns the placed file or folder.
    /// </summary>
    private static string Place(string staging, string targetDir, string archiveName)
    {
        var files = Directory.GetFiles(staging);
        var dirs = Directory.GetDirectories(staging);
        if (files.Length == 1 && dirs.Length == 0)
        {
            var target = Unique(Path.Combine(targetDir, Path.GetFileName(files[0])));
            File.Move(files[0], target);
            return target;
        }
        if (files.Length == 0 && dirs.Length == 1)
        {
            var target = Unique(Path.Combine(targetDir, Path.GetFileName(dirs[0])));
            Directory.Move(dirs[0], target);
            return target;
        }
        if (files.Length == 0 && dirs.Length == 0)
            throw new InvalidDataException(L.T("the archive is empty"));

        var folder = Unique(Path.Combine(targetDir, archiveName));
        Directory.Move(staging, folder);
        return folder;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Directory.Exists(path) ? "" : Path.GetExtension(path);
        if (Directory.Exists(path)) name = Path.GetFileName(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// The file the library should open: a .cue/.gdi/.m3u over its tracks, then the largest file of the game's system
    /// (<paramref name="systemId"/> null = any system the scanner recognizes).
    /// </summary>
    private ScannedRom? FindGameFile(string placed, string? systemId, string gamesRoot)
    {
        var files = File.Exists(placed) ? [placed] : Directory.EnumerateFiles(placed, "*", SearchOption.AllDirectories).ToList();
        var referenced = files.Where(f => SheetParser.IsSheet(Path.GetExtension(f).ToLowerInvariant()))
            .SelectMany(SheetParser.References).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return files.Where(f => !referenced.Contains(Path.GetFullPath(f)))
            .Select(f => scanner.Identify(f, gamesRoot))
            .OfType<ScannedRom>()
            .Where(r => systemId is null || r.SystemId.Equals(systemId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => Path.GetExtension(r.Path).ToLowerInvariant() is ".m3u" ? 3 : SheetParser.IsSheet(Path.GetExtension(r.Path).ToLowerInvariant()) ? 2 : 0)
            .ThenByDescending(r => r.SizeBytes)
            .FirstOrDefault();
    }

    /// <summary>The games folder becomes a ROM folder when no existing one already covers it, so later scans see it.</summary>
    private void EnsureScanned(string gamesRoot)
    {
        var full = Path.GetFullPath(gamesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var covered = db.GetRomFolders().Any(f =>
            full.StartsWith(Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!covered)
            db.AddRomFolder(Path.GetFullPath(gamesRoot));
    }

    private static long? FreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0} MB";
}
