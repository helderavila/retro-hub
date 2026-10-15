using RetroHub.Core.Models;

namespace RetroHub.Core.Scanning;

/// <summary>
/// Local covers while the scraper (ScreenScraper/IGDB) does not exist:
///   1. image with the same name as the ROM next to it;
///   2. covers/{system}/{title}.png|jpg in the data folder.
/// </summary>
public static class CoverResolver
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Value stored in cover_path when the user chose "no cover"; automatic lookup respects it.</summary>
    public const string NoCover = "none";

    public static bool UserRemovedCover(Game game) => game.CoverPath == NoCover;

    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".wmv", ".avi"];

    /// <summary>Preview video (TV Mode): next to the ROM with the same name, or data\videos\{system}\{title}.mp4.</summary>
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
