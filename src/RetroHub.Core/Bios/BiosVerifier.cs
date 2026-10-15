using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Bios;

public enum BiosStatus
{
    Ok,
    /// <summary>Hash certo, nome diferente — será copiado com o nome esperado.</summary>
    Misnamed,
    /// <summary>Arquivo com o nome certo, mas o hash não bate (dump ruim ou região errada).</summary>
    HashMismatch,
    Missing,
}

public sealed record BiosCheck(BiosRequirement Requirement, BiosStatus Status, string? FoundPath)
{
    public bool IsUsable => Status is BiosStatus.Ok or BiosStatus.Misnamed;
}

public sealed record BiosImportItem(string System, string FileName, string Source);

public sealed record BiosImportReport(
    int FilesExamined,
    IReadOnlyList<BiosImportItem> Imported,
    int AlreadyPresent,
    int CopiedToEmulators,
    IReadOnlyList<SystemBiosReport> Reports);

public sealed record SystemBiosReport(SystemDefinition System, IReadOnlyList<BiosCheck> Checks)
{
    public bool Satisfied => System.BiosRule switch
    {
        BiosRule.All => Checks.Where(c => !c.Requirement.Optional).All(c => c.IsUsable),
        BiosRule.Any => Checks.Any(c => c.IsUsable),
        _ => true,
    };

    /// <summary>Roda sem dump graças à BIOS emulada do emulador.</summary>
    public bool UsesHle => System.BiosHle is not null && !Checks.Any(c => c.IsUsable);
}

/// <summary>
/// Confere BIOS por MD5. Nunca baixa nada: só procura na pasta de BIOS do Retro Hub,
/// numa pasta extra configurada e nas pastas de BIOS dos emuladores instalados.
/// </summary>
public sealed class BiosVerifier(ManifestRepository manifests, AppPaths paths, LibraryDatabase db)
{
    public const string ExtraDirSetting = "bios_dir";
    private const long MaxBiosSize = 64L * 1024 * 1024;

    private sealed record BiosFile(string Path, string Name, long Size, string Md5);

    public IReadOnlyList<string> SearchDirectories()
    {
        var dirs = new List<string> { paths.Bios };
        if (db.GetSetting(ExtraDirSetting) is { Length: > 0 } extra)
            dirs.Add(extra);

        foreach (var installed in db.GetInstalledEmulators())
        {
            if (manifests.GetEmulator(installed.Id)?.BiosDir is { } biosDir)
                dirs.Add(Path.Combine(installed.InstallDir, biosDir));
        }

        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<SystemBiosReport>> VerifyAsync(CancellationToken ct = default)
    {
        var files = new List<BiosFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var dir in SearchDirectories())
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*", options))
            {
                var info = new FileInfo(path);
                if (info.Length is 0 or > MaxBiosSize)
                    continue;
                files.Add(new BiosFile(path, info.Name, info.Length, await FileHasher.Md5Async(path, ct)));
            }
        }

        return manifests.Systems
            .Where(s => s.Bios.Count > 0)
            .Select(s => new SystemBiosReport(s, s.Bios.Select(req => Check(req, files)).ToList()))
            .ToList();
    }

    private static BiosCheck Check(BiosRequirement req, List<BiosFile> files)
    {
        var named = files.Where(f => Glob.IsMatch(f.Name, req.File)).ToList();

        if (req.Md5.Count > 0)
        {
            var hashes = req.Md5.Select(h => h.ToLowerInvariant()).ToHashSet();
            var byHash = files.Where(f => hashes.Contains(f.Md5)).ToList();

            if (byHash.FirstOrDefault(f => Glob.IsMatch(f.Name, req.File)) is { } exact)
                return new BiosCheck(req, BiosStatus.Ok, exact.Path);
            if (byHash.Count > 0)
                return new BiosCheck(req, BiosStatus.Misnamed, byHash[0].Path);
            if (named.Count > 0)
                return new BiosCheck(req, BiosStatus.HashMismatch, named[0].Path);
            return new BiosCheck(req, BiosStatus.Missing, null);
        }

        var match = named.FirstOrDefault(f => (req.Size is null || f.Size == req.Size)
                                              && (req.Signature is null || ContainsSignature(File.ReadAllBytes(f.Path), req.Signature)));
        return match is null
            ? new BiosCheck(req, BiosStatus.Missing, null)
            : new BiosCheck(req, BiosStatus.Ok, match.Path);
    }

    // ── Importação ──────────────────────────────────────────────────────────

    private sealed record Candidate(string Name, long Size, string Source, Func<byte[]> Read);

    /// <summary>
    /// Procura BIOS conhecidas em pastas, arquivos soltos e .zip (pelo MD5, não pelo nome),
    /// copia para a pasta de BIOS do Retro Hub com o nome esperado e depois para os emuladores instalados.
    /// Só lê arquivos cujo tamanho bate com o de alguma BIOS — apontar para um disco inteiro continua rápido.
    /// </summary>
    public async Task<BiosImportReport> ImportAsync(IEnumerable<string> sources, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var requirements = manifests.Systems.SelectMany(s => s.Bios.Select(r => (System: s, Req: r))).ToList();
        var sizes = requirements.Select(r => r.Req.Size).OfType<long>().ToHashSet();
        // Arquivos sem tamanho nem hash fixos (ex.: arquivos de chaves de texto) são reconhecidos pelo nome exato.
        var names = requirements.Where(r => r.Req.Md5.Count == 0 && r.Req.Size is null && !Glob.HasWildcards(r.Req.File))
            .Select(r => r.Req.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(paths.Bios);

        var imported = new List<BiosImportItem>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int examined = 0, alreadyPresent = 0;

        await Task.Run(() =>
        {
            foreach (var candidate in EnumerateCandidates(sources, sizes, names))
            {
                ct.ThrowIfCancellationRequested();
                examined++;
                progress?.Report(candidate.Source);

                byte[] data;
                try { data = candidate.Read(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { continue; }

                var md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(data)).ToLowerInvariant();
                var hit = requirements.FirstOrDefault(r => MatchesContent(r.Req, candidate.Name, data, md5));
                if (hit.Req is null)
                    continue;

                var destName = Glob.HasWildcards(hit.Req.File) ? candidate.Name : hit.Req.File;
                var destination = Path.Combine(paths.Bios, destName);

                if (File.Exists(destination) && FileMd5(destination) == md5)
                {
                    alreadyPresent++;
                    continue;
                }
                if (!written.Add(destination))
                    continue; // outra cópia da mesma BIOS já entrou nesta importação

                File.WriteAllBytes(destination, data);
                imported.Add(new BiosImportItem(hit.System.ShortName, destName, candidate.Source));
            }
        }, ct);

        var reports = await VerifyAsync(ct);
        var copied = SyncToEmulators(reports);
        return new BiosImportReport(examined, imported, alreadyPresent, copied, reports);
    }

    private static bool MatchesContent(BiosRequirement req, string name, byte[] data, string md5)
    {
        if (req.Md5.Count > 0)
            return req.Md5.Any(h => h.Equals(md5, StringComparison.OrdinalIgnoreCase));

        return (req.Size is null || data.LongLength == req.Size)
               && Glob.IsMatch(name, req.File)
               && (req.Signature is null || ContainsSignature(data, req.Signature));
    }

    private static IEnumerable<Candidate> EnumerateCandidates(IEnumerable<string> sources, HashSet<long> sizes, HashSet<string> names)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var source in sources)
        {
            var files = Directory.Exists(source)
                ? Directory.EnumerateFiles(source, "*", options)
                : File.Exists(source) ? [source] : [];

            foreach (var file in files)
            {
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in ZipCandidates(file, sizes, names))
                        yield return entry;
                    continue;
                }

                long length;
                try { length = new FileInfo(file).Length; }
                catch (IOException) { continue; }

                if (sizes.Contains(length) || (names.Contains(Path.GetFileName(file)) && length < 1024 * 1024))
                    yield return new Candidate(Path.GetFileName(file), length, file, () => File.ReadAllBytes(file));
            }
        }
    }

    private static IEnumerable<Candidate> ZipCandidates(string zipPath, HashSet<long> sizes, HashSet<string> names)
    {
        System.IO.Compression.ZipArchive zip;
        try { zip = System.IO.Compression.ZipFile.OpenRead(zipPath); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { yield break; }

        using (zip)
        {
            foreach (var entry in zip.Entries.Where(e => sizes.Contains(e.Length) || (names.Contains(e.Name) && e.Length < 1024 * 1024)))
            {
                yield return new Candidate(entry.Name, entry.Length, $"{zipPath} › {entry.FullName}", () =>
                {
                    using var stream = entry.Open();
                    using var ms = new MemoryStream((int)entry.Length);
                    stream.CopyTo(ms);
                    return ms.ToArray();
                });
            }
        }
    }

    private static bool ContainsSignature(byte[] data, string signature) =>
        data.AsSpan(0, Math.Min(data.Length, 64 * 1024)).IndexOf(System.Text.Encoding.ASCII.GetBytes(signature)) >= 0;

    private static string FileMd5(string path) =>
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Copia as BIOS válidas para a pasta de BIOS de cada emulador instalado, com o nome esperado.</summary>
    public int SyncToEmulators(IEnumerable<SystemBiosReport> reports)
    {
        var reportList = reports.ToList();
        var copied = 0;

        foreach (var installed in db.GetInstalledEmulators())
        {
            var manifest = manifests.GetEmulator(installed.Id);
            if (manifest?.BiosDir is null)
                continue;

            var target = Path.Combine(installed.InstallDir, manifest.BiosDir);
            Directory.CreateDirectory(target);

            foreach (var report in reportList.Where(r => manifest.Systems.Contains(r.System.Id, StringComparer.OrdinalIgnoreCase)))
            {
                foreach (var check in report.Checks.Where(c => c.IsUsable && c.FoundPath is not null))
                {
                    var name = Glob.HasWildcards(check.Requirement.File)
                        ? Path.GetFileName(check.FoundPath!)
                        : check.Requirement.File;
                    var destination = Path.Combine(target, name);

                    if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(check.FoundPath!), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(check.FoundPath!).Length)
                        continue;

                    File.Copy(check.FoundPath!, destination, overwrite: true);
                    copied++;
                }
            }
        }

        return copied;
    }
}
