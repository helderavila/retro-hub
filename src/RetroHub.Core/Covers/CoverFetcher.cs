using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Covers;

public sealed record CoverFound(long GameId, string Path);

/// <summary>
/// Baixa capas de thumbnails.libretro.com (sem conta, sem chave).
/// A lista de capas de cada sistema é baixada uma vez e guardada por 7 dias em covers\_index;
/// só a imagem que casar com o jogo é baixada.
/// </summary>
public sealed partial class CoverFetcher(HttpClient http, AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    private const string BaseUrl = "https://thumbnails.libretro.com/";
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, Lazy<Task<CoverMatcher.Index?>>> _indexes = new();

    [GeneratedRegex(@"href=""([^""?/][^""]*?\.png)""", RegexOptions.IgnoreCase)]
    private static partial Regex PngLink();

    /// <summary>Busca capa para todos os jogos sem capa. Reporta cada capa encontrada assim que baixa.</summary>
    public async Task<(int Found, int Missing)> FetchMissingAsync(IEnumerable<Game> games, IProgress<CoverFound>? progress = null,
        CancellationToken ct = default)
    {
        var pending = games.Where(g => !CoverResolver.UserRemovedCover(g) && CoverResolver.Find(g, paths) is null).ToList();
        int found = 0, missing = 0;

        await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (game, token) =>
        {
            string? path = null;
            try { path = await FetchAsync(game, token); }
            catch (HttpRequestException) { }
            catch (IOException) { }

            if (path is null)
            {
                Interlocked.Increment(ref missing);
                return;
            }
            Interlocked.Increment(ref found);
            progress?.Report(new CoverFound(game.Id, path));
        });

        return (found, missing);
    }

    /// <summary>Baixa a capa de um jogo e grava no banco. Retorna o caminho, ou null se não houver capa.</summary>
    public async Task<string?> FetchAsync(Game game, CancellationToken ct = default)
    {
        if (CoverResolver.Find(game, paths) is { } local)
            return local;

        var system = manifests.GetSystem(game.SystemId);
        if (system?.Thumbnails is null)
            return null;

        var index = await _indexes.GetOrAdd(system.Id, _ => new Lazy<Task<CoverMatcher.Index?>>(() => LoadIndexAsync(system, ct))).Value;
        if (index is null)
            return null;

        var name = CoverMatcher.BestMatch(index, Path.GetFileNameWithoutExtension(game.Path), game.Title);
        return name is null ? null : await DownloadAsync(game, name, ct);
    }

    public string? ThumbnailUrl(string systemId, string name) =>
        manifests.GetSystem(systemId)?.Thumbnails is { } folder
            ? $"{BaseUrl}{Uri.EscapeDataString(folder)}/Named_Boxarts/{Uri.EscapeDataString(name)}.png"
            : null;

    /// <summary>"Trocar capa…": capas do acervo cujo nome contém todas as palavras buscadas.</summary>
    public async Task<IReadOnlyList<string>> SearchAsync(string systemId, string query, CancellationToken ct = default)
    {
        var system = manifests.GetSystem(systemId);
        if (system?.Thumbnails is null)
            return [];
        var index = await _indexes.GetOrAdd(system.Id, _ => new Lazy<Task<CoverMatcher.Index?>>(() => LoadIndexAsync(system, ct))).Value;
        if (index is null)
            return [];

        var words = query.Split([' ', '-', ':', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(CoverMatcher.Key).Where(w => w.Length > 0).ToList();
        return index.Exact.Values
            .Where(n => { var key = CoverMatcher.Key(n); return words.All(key.Contains); })
            .OrderBy(n => n.Length)
            .Take(60)
            .ToList();
    }

    /// <summary>Baixa a capa com este nome exato do acervo e grava no jogo.</summary>
    public async Task<string?> DownloadAsync(Game game, string name, CancellationToken ct = default)
    {
        var system = manifests.GetSystem(game.SystemId);
        if (system?.Thumbnails is null)
            return null;

        var destination = Path.Combine(paths.Covers, system.Id, name + ".png");
        if (!File.Exists(destination))
        {
            var url = $"{BaseUrl}{Uri.EscapeDataString(system.Thumbnails)}/Named_Boxarts/{Uri.EscapeDataString(name)}.png";
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var partial = destination + ".part";
            await File.WriteAllBytesAsync(partial, await response.Content.ReadAsByteArrayAsync(ct), ct);
            File.Move(partial, destination, overwrite: true);
        }

        db.SetCover(game.Id, destination);
        game.CoverPath = destination;
        return destination;
    }

    public const string TitleScreens = "Named_Titles";
    public const string Snapshots = "Named_Snaps";

    /// <summary>
    /// Tela de título (<see cref="TitleScreens"/>) ou imagem do jogo rodando (<see cref="Snapshots"/>):
    /// mesmo acervo e mesmo nome da capa. Fica em covers\{sistema}\_{pasta}; um .none marca "não existe" por 7 dias.
    /// </summary>
    public async Task<string?> FetchArtAsync(Game game, string folder, CancellationToken ct = default)
    {
        var system = manifests.GetSystem(game.SystemId);
        if (system?.Thumbnails is null)
            return null;

        var name = CatalogName(game, system.Id);
        if (name is null)
        {
            var index = await _indexes.GetOrAdd(system.Id, _ => new Lazy<Task<CoverMatcher.Index?>>(() => LoadIndexAsync(system, ct))).Value;
            if (index is null)
                return null;
            name = CoverMatcher.BestMatch(index, Path.GetFileNameWithoutExtension(game.Path), game.Title);
            if (name is null)
                return null;
        }

        var destination = Path.Combine(paths.Covers, system.Id, "_" + folder, name + ".png");
        var missing = destination + ".none";
        if (File.Exists(destination))
            return destination;
        if (File.Exists(missing) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missing) < IndexMaxAge)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var url = $"{BaseUrl}{Uri.EscapeDataString(system.Thumbnails)}/{folder}/{Uri.EscapeDataString(name)}.png";
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await File.WriteAllTextAsync(missing, "", ct);
            return null;
        }
        if (!response.IsSuccessStatusCode)
            return null;

        var partial = destination + ".part";
        await File.WriteAllBytesAsync(partial, await response.Content.ReadAsByteArrayAsync(ct), ct);
        File.Move(partial, destination, overwrite: true);
        return destination;
    }

    /// <summary>Nome no acervo quando a capa veio de lá (covers\{sistema}\{nome}.png); null para capa própria ou sem capa.</summary>
    public string? CatalogName(Game game, string systemId)
    {
        if (game.CoverPath is not { } cover || cover == CoverResolver.NoCover)
            return null;
        var folder = Path.GetFullPath(Path.Combine(paths.Covers, systemId));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(cover)), folder, StringComparison.OrdinalIgnoreCase))
            return null;
        var name = Path.GetFileNameWithoutExtension(cover);
        return name.StartsWith("custom-", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    /// <summary>Usa uma imagem do computador como capa (copiada para a pasta de capas).</summary>
    public string SetCustomCover(Game game, string imagePath)
    {
        var destination = Path.Combine(paths.Covers, game.SystemId, $"custom-{game.Id}{Path.GetExtension(imagePath).ToLowerInvariant()}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(imagePath, destination, overwrite: true);
        db.SetCover(game.Id, destination);
        game.CoverPath = destination;
        return destination;
    }

    /// <summary>Sem capa — e a busca automática não coloca outra.</summary>
    public void RemoveCover(Game game)
    {
        db.SetCover(game.Id, CoverResolver.NoCover);
        game.CoverPath = CoverResolver.NoCover;
    }

    private async Task<CoverMatcher.Index?> LoadIndexAsync(SystemDefinition system, CancellationToken ct)
    {
        var cache = Path.Combine(paths.Covers, "_index", system.Id + ".txt");
        string[] names;

        if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < IndexMaxAge)
        {
            names = await File.ReadAllLinesAsync(cache, ct);
        }
        else
        {
            try
            {
                var html = await http.GetStringAsync($"{BaseUrl}{Uri.EscapeDataString(system.Thumbnails!)}/Named_Boxarts/", ct);
                names = PngLink().Matches(html)
                    .Select(m => WebUtility.HtmlDecode(Uri.UnescapeDataString(m.Groups[1].Value)))
                    .Select(n => n[..^4])
                    .Distinct()
                    .ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                await File.WriteAllLinesAsync(cache, names, ct);
            }
            catch (HttpRequestException) when (File.Exists(cache))
            {
                names = await File.ReadAllLinesAsync(cache, ct); // offline: usa o cache vencido
            }
            catch (HttpRequestException)
            {
                _indexes.TryRemove(system.Id, out _); // tenta de novo na próxima vez
                return null;
            }
        }

        return new CoverMatcher.Index(names);
    }
}
