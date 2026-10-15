using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Install;

public sealed record ResolvedDownload(string Url, string FileName, string Version, string? Sha256);

/// <summary>Turns a manifest "source" into a concrete URL + version + expected hash.</summary>
public sealed class DownloadSourceResolver(HttpClient http)
{
    public Task<ResolvedDownload> ResolveAsync(SourceSpec source, CancellationToken ct) =>
        source.Type.ToLowerInvariant() switch
        {
            "github" => ResolveReleaseAsync(source, "https://api.github.com/", ct),
            // Forgejo/Gitea (e.g. codeberg.org) — same release structure as GitHub, under /api/v1.
            "forgejo" => ResolveReleaseAsync(source, (source.Api ?? throw new InvalidDataException(L.T("Forgejo source without 'api'."))).TrimEnd('/') + "/", ct),
            "url" => Task.FromResult(ResolveUrl(source)),
            _ => throw new InvalidDataException(L.F("Unknown source type: {0}", source.Type)),
        };

    private static ResolvedDownload ResolveUrl(SourceSpec source)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
            throw new InvalidDataException(L.T("'url' source without a URL."));

        var url = source.Version is null ? source.Url : source.Url.Replace("{version}", source.Version);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        return new ResolvedDownload(url, fileName, source.Version ?? L.T("unknown"), Normalize(source.Sha256));
    }

    private async Task<ResolvedDownload> ResolveReleaseAsync(SourceSpec source, string apiBase, CancellationToken ct)
    {
        var repo = source.Repo ?? throw new InvalidDataException(L.F("'{0}' source without 'repo'.", source.Type));
        var assetPattern = source.Asset ?? throw new InvalidDataException(L.F("'{0}' source without 'asset'.", source.Type));

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
            throw new HttpRequestException(L.F("{0} returned {1} when querying {2}.", new Uri(apiBase).Host, (int)response.StatusCode, repo));

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        var release = doc.RootElement;
        if (release.ValueKind == JsonValueKind.Array)
        {
            release = release.EnumerateArray().FirstOrDefault(r => !(r.TryGetProperty("draft", out var d) && d.GetBoolean()));
            if (release.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException(L.F("No published release in {0}.", repo));
        }

        var regex = new Regex(assetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!regex.IsMatch(name))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString()!;

            // GitHub publishes "digest": "sha256:..." for release assets.
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

        throw new InvalidOperationException(L.F("No asset of {0} matches /{1}/.", repo, assetPattern));
    }

    private static string? Normalize(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? null : sha.Trim().ToLowerInvariant();
}
