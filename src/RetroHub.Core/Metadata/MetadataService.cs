using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using RetroHub.Core.Covers;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Informações e verificação de jogos usando o libretro-database (aberto, sem conta):
///   • metadat/{no-intro|redump}/…  → lista oficial de dumps (nome, serial, CRC, SHA-1, tamanho);
///   • metadat/{developer,publisher,genre,releaseyear}/… → informações (PS1/PS2 trazem até sinopse).
/// Os .dat ficam em data\metadata e são atualizados a cada 30 dias.
/// </summary>
public sealed class MetadataService(HttpClient http, AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/";
    private static readonly string[] InfoKinds = ["developer", "publisher", "genre", "releaseyear"];
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, Lazy<Task<SystemData?>>> _systems = new();

    /// <summary>Tudo que se sabe dos jogos de um sistema, indexado por nome, serial e CRC.</summary>
    private sealed class SystemData
    {
        public Dictionary<string, Dictionary<string, string>> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NameBySerial { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NameByCrc { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>SHA-1 e CRC+tamanho de cada arquivo dos dumps oficiais → nome do jogo.</summary>
        public Dictionary<string, string> DumpBySha1 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DumpByCrcSize { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CoverMatcher.Index Names { get; set; } = new([]);
        public bool HasVerifyDat { get; set; }
    }

    // ── Informações ─────────────────────────────────────────────────────────

    /// <summary>Preenche as informações dos jogos que ainda não têm. Retorna quantos foram encontrados.</summary>
    public async Task<int> FillMissingAsync(IEnumerable<Game> games, IProgress<long>? found = null, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var game in games.Where(g => GameMetadata.FromJson(g.MetadataJson) is not { ManualInfo: true } and not { Name: not null }))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = await LookupAsync(game, ct);
                if (info is null)
                    continue;
                var merged = (GameMetadata.FromJson(game.MetadataJson) ?? new GameMetadata()) with
                {
                    Name = info.Name, Description = info.Description, Developer = info.Developer, Publisher = info.Publisher,
                    Genre = info.Genre, Year = info.Year, Region = info.Region, Players = info.Players, Serial = info.Serial,
                };
                game.MetadataJson = merged.ToJson();
                db.SetMetadata(game.Id, game.MetadataJson);
                count++;
                found?.Report(game.Id);
            }
            catch (HttpRequestException) { /* sem internet: tenta na próxima */ }
        }
        return count;
    }

    public async Task<GameMetadata?> LookupAsync(Game game, CancellationToken ct = default)
    {
        var data = await GetSystemAsync(game.SystemId, ct);
        if (data is null)
            return null;

        string? name = null;
        if (game.Serial is { } serial)
            data.NameBySerial.TryGetValue(serial, out name);
        if (name is null && game.Crc32 is { } crc)
            data.NameByCrc.TryGetValue(crc, out name);
        name ??= CoverMatcher.BestMatch(data.Names, Path.GetFileNameWithoutExtension(game.Path), game.Title);
        if (name is null || !data.ByName.TryGetValue(name, out var f))
            return null;

        return new GameMetadata
        {
            Name = name,
            Description = f.GetValueOrDefault("description"),
            Developer = f.GetValueOrDefault("developer"),
            Publisher = f.GetValueOrDefault("publisher"),
            Genre = f.GetValueOrDefault("genre"),
            Year = f.GetValueOrDefault("releaseyear"),
            Region = f.GetValueOrDefault("region"),
            Players = f.GetValueOrDefault("users"),
            Serial = f.GetValueOrDefault("serial")?.Split(',')[0].Trim(),
        };
    }

    // ── Verificação ─────────────────────────────────────────────────────────

    /// <summary>
    /// Calcula SHA-1/CRC32 do arquivo inteiro (e das trilhas, se for .cue/.gdi) e compara com o dump oficial.
    /// Grava o resultado em games.metadata_json.
    /// </summary>
    public async Task<GameMetadata> VerifyAsync(Game game, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var data = await GetSystemAsync(game.SystemId, ct);
        var current = GameMetadata.FromJson(game.MetadataJson) ?? new GameMetadata();

        GameMetadata Save(string status, string detail, string? name = null)
        {
            var updated = current with { VerifyStatus = status, VerifyDetail = detail, VerifiedAt = DateTimeOffset.UtcNow, Name = name ?? current.Name };
            game.MetadataJson = updated.ToJson();
            db.SetMetadata(game.Id, game.MetadataJson);
            return updated;
        }

        if (data is null || !data.HasVerifyDat)
            return Save("unsupported", "Não há lista oficial de dumps para este sistema.");

        var ext = Path.GetExtension(game.Path).ToLowerInvariant();
        if (ext is ".chd" or ".rvz" or ".wia" or ".gcz" or ".cso" or ".zso" or ".pbp" or ".7z" or ".wbfs" or ".ecm")
            return Save("unsupported", $"Arquivos {ext} são comprimidos: o conteúdo não pode ser comparado direto com a lista oficial (que é de .iso/.bin).");

        // .zip de cartucho: o CRC de cada arquivo já está no índice do zip — nada para calcular.
        if (ext == ".zip")
        {
            using var zip = ZipFile.OpenRead(game.Path);
            foreach (var entry in zip.Entries)
                if (data.DumpByCrcSize.TryGetValue($"{entry.Crc32:x8}:{entry.Length}", out var zipped))
                    return Save("ok", $"Bate com o dump oficial: {zipped}", zipped);
            return Save("unknown", "Nenhum arquivo do .zip bate com a lista oficial (modificado, traduzido, hack ou corrompido).");
        }

        var files = SheetParser.FilesOfGame(game.Path)
            .Where(f => !SheetParser.IsSheet(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (files.Count == 0)
            return Save("unknown", "Arquivo não encontrado.");

        var total = files.Sum(f => new FileInfo(f).Length);
        long done = 0;
        var matches = new List<string?>();
        foreach (var file in files)
        {
            var (sha1, crc, size) = await Task.Run(() => HashFile(file, read =>
            {
                done += read;
                progress?.Report(total == 0 ? 1 : (double)done / total);
            }, ct), ct);

            matches.Add(data.DumpBySha1.GetValueOrDefault(sha1) ?? data.DumpByCrcSize.GetValueOrDefault($"{crc}:{size}"));
        }

        if (matches.All(m => m is not null) && matches.Distinct().Count() == 1)
            return Save("ok", $"Bate com o dump oficial: {matches[0]}", matches[0]);

        var ok = matches.Count(m => m is not null);
        return Save("unknown", files.Count == 1
            ? "Não bate com a lista oficial: o arquivo pode ser modificado (tradução, hack), corrompido ou de um dump não catalogado."
            : $"{ok} de {files.Count} trilhas batem com a lista oficial — as demais podem estar corrompidas ou ser de outra versão.");
    }

    /// <summary>
    /// Bytes a pular antes do hash: as listas oficiais catalogam a ROM "pura".
    /// NES: cabeçalho iNES de 16 bytes. SNES: cabeçalho de copiadora de 512 bytes (tamanho % 1024 == 512).
    /// </summary>
    internal static int HeaderSize(string path, long length)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".sfc" or ".smc" && length % 1024 == 512)
            return 512;
        if (ext == ".nes" && length > 16)
        {
            using var fs = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            fs.ReadExactly(magic);
            if (magic.SequenceEqual("NES\u001a"u8))
                return 16;
        }
        return 0;
    }

    private static (string Sha1, string Crc, long Size) HashFile(string path, Action<long> onRead, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var header = HeaderSize(path, stream.Length);
        stream.Position = header;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var crc = new System.IO.Hashing.Crc32();
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, read);
            crc.Append(buffer.AsSpan(0, read));
            onRead(read);
        }
        return (Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), crc.GetCurrentHashAsUInt32().ToString("x8"), stream.Length - header);
    }

    // ── Dados por sistema ───────────────────────────────────────────────────

    private async Task<SystemData?> GetSystemAsync(string systemId, CancellationToken ct)
    {
        try
        {
            return await _systems.GetOrAdd(systemId, id => new Lazy<Task<SystemData?>>(() => LoadSystemAsync(id, ct))).Value;
        }
        catch
        {
            _systems.TryRemove(systemId, out _); // sem internet agora: tenta de novo na próxima chamada
            throw;
        }
    }

    private async Task<SystemData?> LoadSystemAsync(string systemId, CancellationToken ct)
    {
        var system = manifests.GetSystem(systemId);
        if (system?.Thumbnails is null)
            return null;

        var data = new SystemData();
        var fileName = system.Thumbnails + ".dat";

        if (system.Dat is { } kind && await GetDatAsync(kind, fileName, ct) is { } verifyDat)
        {
            data.HasVerifyDat = true;
            foreach (var entry in verifyDat)
            {
                if (entry.Name is not { } name) continue;
                var record = Record(data, name);
                Merge(record, entry);
                if (entry.Get("serial") is { } serial)
                    foreach (var s in serial.Split(',', StringSplitOptions.TrimEntries))
                        data.NameBySerial.TryAdd(s, name);
                foreach (var rom in entry.Roms)
                {
                    if (rom.GetValueOrDefault("sha1") is { } sha1) data.DumpBySha1.TryAdd(sha1, name);
                    if (rom.GetValueOrDefault("crc") is { } crc)
                    {
                        data.NameByCrc.TryAdd(crc, name);
                        if (rom.GetValueOrDefault("size") is { } size) data.DumpByCrcSize.TryAdd($"{crc.ToLowerInvariant()}:{size}", name);
                    }
                }
            }
        }

        foreach (var info in InfoKinds)
        {
            if (await GetDatAsync(info, fileName, ct) is not { } infoDat)
                continue;
            foreach (var entry in infoDat)
            {
                // Cartuchos: casa pelo CRC; discos de PS1/PS2: pelo serial; senão pelo nome.
                var name = entry.Roms.Select(r => r.GetValueOrDefault("crc")).OfType<string>().Select(c => data.NameByCrc.GetValueOrDefault(c)).FirstOrDefault(n => n is not null)
                           ?? (entry.Get("serial") is { } s ? data.NameBySerial.GetValueOrDefault(s) : null)
                           ?? entry.Name;
                if (name is null) continue;
                Merge(Record(data, name), entry);
                if (entry.Get("serial") is { } serial) data.NameBySerial.TryAdd(serial, name);
                foreach (var crc in entry.Roms.Select(r => r.GetValueOrDefault("crc")).OfType<string>())
                    data.NameByCrc.TryAdd(crc, name);
            }
        }

        data.Names = new CoverMatcher.Index(data.ByName.Keys);
        return data;
    }

    private static Dictionary<string, string> Record(SystemData data, string name)
    {
        if (!data.ByName.TryGetValue(name, out var record))
            data.ByName[name] = record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return record;
    }

    private static void Merge(Dictionary<string, string> record, DatEntry entry)
    {
        foreach (var (key, value) in entry.Fields)
            if (key is not ("name" or "comment") && value.Length > 0)
                record.TryAdd(key, value);
    }

    /// <summary>Baixa (ou lê do cache) um .dat. 404 = este tipo de dado não existe para o sistema.</summary>
    private async Task<List<DatEntry>?> GetDatAsync(string kind, string fileName, CancellationToken ct)
    {
        var cache = Path.Combine(paths.Root, "metadata", kind, fileName);
        string? text = null;

        if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < MaxAge)
        {
            text = await File.ReadAllTextAsync(cache, ct);
        }
        else
        {
            var missingMarker = cache + ".404";
            if (File.Exists(missingMarker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missingMarker) < MaxAge)
                return null;
            try
            {
                using var response = await http.GetAsync($"{BaseUrl}{kind}/{Uri.EscapeDataString(fileName)}", ct);
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await File.WriteAllTextAsync(missingMarker, "", ct);
                    return null;
                }
                response.EnsureSuccessStatusCode();
                text = await response.Content.ReadAsStringAsync(ct);
                await File.WriteAllTextAsync(cache, text, ct);
            }
            catch (HttpRequestException) when (File.Exists(cache))
            {
                text = await File.ReadAllTextAsync(cache, ct); // offline: usa o cache vencido
            }
        }

        return DatParser.Parse(text);
    }
}
