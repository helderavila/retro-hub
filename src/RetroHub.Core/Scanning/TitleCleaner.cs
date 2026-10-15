using System.Text.RegularExpressions;

namespace RetroHub.Core.Scanning;

/// <summary>"Legend of Zelda, The - A Link to the Past (USA) [!].sfc" → "The Legend of Zelda - A Link to the Past".</summary>
public static partial class TitleCleaner
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?<name>.+?), (?<article>The|A|An|O|Os|As)(?<rest>( - .*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingArticle();

    public static string Clean(string path)
    {
        var raw = Path.GetFileNameWithoutExtension(path);
        var title = raw.Replace('_', ' ');
        title = Tags().Replace(title, "");
        title = Spaces().Replace(title, " ").Trim(' ', '-', '.');

        var m = TrailingArticle().Match(title);
        if (m.Success)
            title = $"{m.Groups["article"].Value} {m.Groups["name"].Value}{m.Groups["rest"].Value}";

        return title.Length > 0 ? title : raw;
    }
}
