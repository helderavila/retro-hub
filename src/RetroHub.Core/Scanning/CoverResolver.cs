using RetroHub.Core.Models;

namespace RetroHub.Core.Scanning;

/// <summary>
/// Capas locais enquanto o scraper (ScreenScraper/IGDB) não existe:
///   1. imagem com o mesmo nome da ROM ao lado dela;
///   2. covers/{sistema}/{título}.png|jpg na pasta de dados.
/// </summary>
public static class CoverResolver
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Valor gravado em cover_path quando o usuário escolheu "sem capa" — a busca automática respeita.</summary>
    public const string NoCover = "none";

    public static bool UserRemovedCover(Game game) => game.CoverPath == NoCover;

    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".wmv", ".avi"];

    /// <summary>Vídeo de prévia (Modo TV): ao lado da ROM com o mesmo nome, ou data\videos\{sistema}\{título}.mp4.</summary>
    public static string? FindVideo(Game game, AppPaths paths)
    {
        var dir = Path.GetDirectoryName(game.Path);
        var stem = Path.GetFileNameWithoutExtension(game.Path);
        var safeTitle = string.Concat(game.Title.Split(Path.GetInvalidFileNameChars()));
        return VideoExtensions.Select(ext => dir is null ? null : Path.Combine(dir, stem + ext))
            .Concat(VideoExtensions.Select(ext => Path.Combine(paths.Root, "videos", game.SystemId, safeTitle + ext)))
            .FirstOrDefault(p => p is not null && File.Exists(p));
    }

    public static string? Find(Game game, AppPaths paths)
    {
        if (UserRemovedCover(game))
            return null;
        if (game.CoverPath is { } stored && File.Exists(stored))
            return stored;

        var dir = Path.GetDirectoryName(game.Path);
        var stem = Path.GetFileNameWithoutExtension(game.Path);
        var safeTitle = string.Concat(game.Title.Split(Path.GetInvalidFileNameChars()));

        var candidates = ImageExtensions.Select(ext => dir is null ? null : Path.Combine(dir, stem + ext))
            .Concat(ImageExtensions.Select(ext => Path.Combine(paths.Covers, game.SystemId, safeTitle + ext)));

        return candidates.FirstOrDefault(p => p is not null && File.Exists(p));
    }
}
