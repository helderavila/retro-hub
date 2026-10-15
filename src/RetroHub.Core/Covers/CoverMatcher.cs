using System.Text.RegularExpressions;
using RetroHub.Core.Scanning;

namespace RetroHub.Core.Covers;

/// <summary>
/// Escolhe, numa lista de nomes No-Intro/Redump ("Chrono Trigger (USA)"), a capa que corresponde a um jogo.
/// Ordem: nome do arquivo idêntico → mesmo título (sem tags, artigo, pontuação e acentos) →
/// título contido no outro ("Zelda - A Link to the Past" ⊂ "Legend of Zelda, The - A Link to the Past").
/// Desempata pela região do arquivo, depois EUA/Mundo/Brasil/Europa/Japão, e prefere versões finais.
/// </summary>
public static partial class CoverMatcher
{
    private static readonly string[] RegionPreference = ["USA", "World", "Brazil", "Europe", "Japan"];

    /// <summary>Na busca aproximada, o título menor precisa cobrir ao menos esta fração do maior.</summary>
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
            // Sem diferenciar maiúsculas na busca, mas devolvendo sempre a grafia do servidor (que diferencia).
            Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in list)
                Exact.TryAdd(name, name);
        }
    }

    /// <summary>Chave de comparação: "Legend of Zelda, The - A Link..." e "The Legend of Zelda: A Link..." viram a mesma.</summary>
    public static string Key(string name)
    {
        var clean = TitleCleaner.Clean(name + ".x").ToLowerInvariant();
        if (clean.StartsWith("the ")) clean = clean[4..];
        clean = clean.Replace("&", "and");
        // "Pokémon" == "Pokemon": FormD separa o acento (marca combinante), que não é letra e cai no filtro.
        return new string(clean.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetterOrDigit).ToArray());
    }

    /// <param name="romFileStem">Nome do arquivo sem extensão — se já estiver no padrão No-Intro, casa direto.</param>
    /// <param name="title">Título limpo do jogo na biblioteca.</param>
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
    /// Um título contém o outro e cobre a maior parte dele. Só aceita se houver um único jogo assim —
    /// "Super Mario" bate em Bros., Bros. 3 e World, e capa errada é pior que nenhuma.
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

    /// <summary>Regra do libretro-thumbnails: &amp;*/:`&lt;&gt;?\| viram "_".</summary>
    public static string Sanitize(string name) => Regex.Replace(name, @"[&*/:`<>?\\|]", "_");
}
