using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.Core.Models;

namespace RetroHub.Core.Install;

public sealed record ResolvedDownload(string Url, string FileName, string Version, string? Sha256);

/// <summary>Transforma a "source" de um manifesto em uma URL concreta + versão + hash esperado.</summary>
public sealed class DownloadSourceResolver(HttpClient http)
{
    public Task<ResolvedDownload> ResolveAsync(SourceSpec source, CancellationToken ct) =>
        source.Type.ToLowerInvariant() switch
        {
            "github" => ResolveReleaseAsync(source, "https://api.github.com/", ct),
            // Forgejo/Gitea (ex.: codeberg.org) — mesma estrutura de releases do GitHub, em /api/v1.
            "forgejo" => ResolveReleaseAsync(source, (source.Api ?? throw new InvalidDataException("Fonte forgejo sem 'api'.")).TrimEnd('/') + "/", ct),
            "url" => Task.FromResult(ResolveUrl(source)),
            _ => throw new InvalidDataException($"Tipo de fonte desconhecido: {source.Type}"),
        };

    private static ResolvedDownload ResolveUrl(SourceSpec source)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
            throw new InvalidDataException("Fonte 'url' sem URL.");

        var url = source.Version is null ? source.Url : source.Url.Replace("{version}", source.Version);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        return new ResolvedDownload(url, fileName, source.Version ?? "desconhecida", Normalize(source.Sha256));
    }

    private async Task<ResolvedDownload> ResolveReleaseAsync(SourceSpec source, string apiBase, CancellationToken ct)
    {
        var repo = source.Repo ?? throw new InvalidDataException($"Fonte '{source.Type}' sem 'repo'.");
        var assetPattern = source.Asset ?? throw new InvalidDataException($"Fonte '{source.Type}' sem 'asset'.");

        var endpoint = source.Prerelease
            ? $"repos/{repo}/releases?per_page=10"
            : string.IsNullOrEmpty(source.Tag)
                ? $"repos/{repo}/releases/latest"
                : $"repos/{repo}/releases/tags/{Uri.EscapeDataString(source.Tag)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, apiBase + endpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{new Uri(apiBase).Host} respondeu {(int)response.StatusCode} ao consultar {repo}.");

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        var release = doc.RootElement;
        if (release.ValueKind == JsonValueKind.Array)
        {
            release = release.EnumerateArray().FirstOrDefault(r => !(r.TryGetProperty("draft", out var d) && d.GetBoolean()));
            if (release.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException($"Nenhuma release publicada em {repo}.");
        }

        var regex = new Regex(assetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!regex.IsMatch(name))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString()!;

            // O GitHub publica "digest": "sha256:..." para assets de releases.
            var sha = source.Sha256;
            if (sha is null && asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String)
            {
                var value = digest.GetString();
                if (value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
                    sha = value["sha256:".Length..];
            }

            var tag = release.GetProperty("tag_name").GetString() ?? "?";
            var version = tag;
            if (tag.Equals("latest", StringComparison.OrdinalIgnoreCase)
                && release.TryGetProperty("published_at", out var published)
                && published.GetString() is { Length: >= 10 } date)
            {
                version = $"latest ({date[..10]})";
            }

            return new ResolvedDownload(url, name, version, Normalize(sha));
        }

        throw new InvalidOperationException($"Nenhum asset de {repo} casa com /{assetPattern}/.");
    }

    private static string? Normalize(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? null : sha.Trim().ToLowerInvariant();
}
