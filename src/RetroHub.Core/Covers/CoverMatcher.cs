using System.Text.RegularExpressions;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Covers;

/// <summary>
/// Picks, from a list of No-Intro/Redump names ("Chrono Trigger (USA)"), the cover that matches a game.
/// Order: identical file name → same title (without tags, article, punctuation and accents) →
/// title contained in the other ("Zelda - A Link to the Past" ⊂ "Legend of Zelda, The - A Link to the Past").
/// Ties are broken by the file's region, then USA/World/Brazil/Europe/Japan, preferring final releases.
/// </summary>
public static partial class CoverMatcher
{
    private static readonly string[] RegionPreference = ["USA", "World", "Brazil", "Europe", "Japan"];

    /// <summary>In approximate matching, the shorter title must cover at least this fraction of the longer one.</summary>
    private const double MinContainmentRatio = 0.6;
    private const int MinContainmentLength = 8;

    [GeneratedRegex(@"\((Beta|Proto|Demo|Sample|Kiosk|Pirate|Unl|Hack)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Unwanted();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Tag();

    public sealed class Index
    {
        internal Dictionary<string, List<string>> ByKey { get; }
        internal Dictionary<string, string> Exact { get; }

        public Index(IEnumerable<string> names)
        {
            var list = names.Distinct().ToList();
            ByKey = list.GroupBy(Key).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.ToList());
            // Case-insensitive lookup, but always returning the server spelling (which is case-sensitive).
            Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in list)
                Exact.TryAdd(name, name);
        }
    }

    /// <summary>Comparison key: "Legend of Zelda, The - A Link..." and "The Legend of Zelda: A Link..." become the same.</summary>
    public static string Key(string name)
    {
        var clean = TitleCleaner.Clean(name + ".x").ToLowerInvariant();
        if (clean.StartsWith("the ")) clean = clean[4..];
        clean = clean.Replace("&", "and");
        // "Pokémon" == "Pokemon": FormD splits off the accent (combining mark), which is not a letter and gets filtered out.
        return new string(clean.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetterOrDigit).ToArray());
    }

    /// <param name="romFileStem">File name without extension; if it already follows No-Intro naming, it matches directly.</param>
    /// <param name="title">Clean game title in the library.</param>
    public static string? BestMatch(Index index, string romFileStem, string title)
    {
        if (index.Exact.TryGetValue(Sanitize(romFileStem), out var exact))
            return exact;

        var titleKey = Key(title);
        var candidates = index.ByKey.GetValueOrDefault(titleKey)
                         ?? index.ByKey.GetValueOrDefault(Key(romFileStem))
                         ?? Approximate(index, titleKey);
        if (candidates is null)
            return null;

        var romTags = Tag().Matches(romFileStem).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .OrderBy(c => Unwanted().IsMatch(c) ? 1 : 0)
            .ThenByDescending(c => Tag().Matches(c).Count(m => romTags.Contains(m.Groups[1].Value)))
            .ThenBy(c => RegionRank(c))
            .ThenBy(c => c.Length)
            .First();
    }

    /// <summary>
    /// One title contains the other and covers most of it. Only accepted if there is a single such game:
    /// "Super Mario" hits Bros., Bros. 3 and World, and a wrong cover is worse than none.
    /// </summary>
    private static List<string>? Approximate(Index index, string key)
    {
        if (key.Length < MinContainmentLength)
            return null;

        var hits = index.ByKey
            .Where(kv => kv.Key.Length >= MinContainmentLength
                         && (kv.Key.Contains(key) || key.Contains(kv.Key))
                         && (double)Math.Min(kv.Key.Length, key.Length) / Math.Max(kv.Key.Length, key.Length) >= MinContainmentRatio)
            .Take(2)
            .ToList();

        return hits.Count == 1 ? hits[0].Value : null;
    }

    private static int RegionRank(string name)
    {
        for (var i = 0; i < RegionPreference.Length; i++)
            if (name.Contains(RegionPreference[i], StringComparison.OrdinalIgnoreCase))
                return i;
        return RegionPreference.Length;
    }

    /// <summary>libretro-thumbnails rule: &amp;*/:`&lt;&gt;?\| become "_".</summary>
    public static string Sanitize(string name) => Regex.Replace(name, @"[&*/:`<>?\\|]", "_");
}
