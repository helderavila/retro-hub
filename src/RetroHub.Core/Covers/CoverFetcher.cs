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
/// Downloads covers from thumbnails.libretro.com (no account, no key).
/// Each system's cover list is downloaded once and kept for 7 days in covers\_index;
/// only the image matching the game is downloaded.
/// </summary>
public sealed partial class CoverFetcher(HttpClient http, AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    private const string BaseUrl = "https://thumbnails.libretro.com/";
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, Lazy<Task<CoverMatcher.Index?>>> _indexes = new();

    [GeneratedRegex(@"href=""([^""?/][^""]*?\.png)""", RegexOptions.IgnoreCase)]
    private static partial Regex PngLink();

    /// <summary>Looks up covers for all games without one. Reports each cover as soon as it is downloaded.</summary>
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

    /// <summary>Downloads a game's cover and stores it in the database. Returns the path, or null if there is no cover.</summary>
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

    /// <summary>"Change cover…": catalog covers whose name contains all search words.</summary>
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

    /// <summary>Downloads the catalog cover with this exact name and assigns it to the game.</summary>
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
    /// Title screen (<see cref="TitleScreens"/>) or in-game image (<see cref="Snapshots"/>):
    /// same catalog and same name as the cover. Stored in covers\{system}\_{folder}; a .none marks "does not exist" for 7 days.
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

    /// <summary>Catalog name when the cover came from it (covers\{system}\{name}.png); null for a custom cover or no cover.</summary>
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

    /// <summary>Uses an image from the computer as cover (copied to the covers folder).</summary>
    public string SetCustomCover(Game game, string imagePath)
    {
        var destination = Path.Combine(paths.Covers, game.SystemId, $"custom-{game.Id}{Path.GetExtension(imagePath).ToLowerInvariant()}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(imagePath, destination, overwrite: true);
        db.SetCover(game.Id, destination);
        game.CoverPath = destination;
        return destination;
    }

    /// <summary>No cover, and automatic lookup will not add one.</summary>
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
                names = await File.ReadAllLinesAsync(cache, ct); // offline: use the stale cache
            }
            catch (HttpRequestException)
            {
                _indexes.TryRemove(system.Id, out _); // try again next time
                return null;
            }
        }

        return new CoverMatcher.Index(names);
    }
}
