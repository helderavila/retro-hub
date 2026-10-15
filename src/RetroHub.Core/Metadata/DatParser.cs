using System.Text;

namespace RetroHub.Core.Metadata;

/// <summary>A <c>game ( … )</c> block from a clrmamepro file (libretro-database / No-Intro / Redump .dat).</summary>
public sealed class DatEntry
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Dictionary<string, string>> Roms { get; } = [];

    /// <summary>Canonical name ("Chrono Trigger (USA)"). Metadata .dat files use "comment" instead of "name".</summary>
    public string? Name => Fields.GetValueOrDefault("name") ?? Fields.GetValueOrDefault("comment");

    public string? Get(string key) => Fields.GetValueOrDefault(key);
}

/// <summary>clrmamepro format reader: <c>key "value"</c>, parenthesized blocks, nested <c>rom ( … )</c>.</summary>
public static class DatParser
{
    public static List<DatEntry> Parse(string text)
    {
        var tokens = Tokenize(text);
        var entries = new List<DatEntry>();
        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if ((token == "game" || token == "machine") && i + 1 < tokens.Count && tokens[i + 1] == "(")
            {
                i += 2;
                entries.Add(ParseGame(tokens, ref i));
            }
            else if (i + 1 < tokens.Count && tokens[i + 1] == "(")
            {
                i += 2;
                SkipBlock(tokens, ref i); // "clrmamepro ( … )" header and the like
            }
            else
            {
                i++;
            }
        }
        return entries;
    }

    private static DatEntry ParseGame(List<string> tokens, ref int i)
    {
        var entry = new DatEntry();
        while (i < tokens.Count && tokens[i] != ")")
        {
            var key = tokens[i++];
            if (i >= tokens.Count) break;

            if (tokens[i] == "(")
            {
                i++;
                var block = ParsePairs(tokens, ref i);
                if (key.Equals("rom", StringComparison.OrdinalIgnoreCase) || key.Equals("disk", StringComparison.OrdinalIgnoreCase))
                    entry.Roms.Add(block);
            }
            else
            {
                entry.Fields.TryAdd(key, tokens[i++]);
            }
        }
        i++; // ")"
        return entry;
    }

    private static Dictionary<string, string> ParsePairs(List<string> tokens, ref int i)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (i < tokens.Count && tokens[i] != ")")
        {
            var key = tokens[i++];
            if (i < tokens.Count && tokens[i] == "(")
            {
                i++;
                SkipBlock(tokens, ref i);
                continue;
            }
            if (i < tokens.Count && tokens[i] != ")")
                pairs.TryAdd(key, tokens[i++]);
        }
        i++;
        return pairs;
    }

    private static void SkipBlock(List<string> tokens, ref int i)
    {
        var depth = 1;
        while (i < tokens.Count && depth > 0)
        {
            if (tokens[i] == "(") depth++;
            else if (tokens[i] == ")") depth--;
            i++;
        }
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is '(' or ')') { tokens.Add(c.ToString()); i++; continue; }

            sb.Clear();
            if (c == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++; // \" inside descriptions
                    sb.Append(text[i++]);
                }
                i++;
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')'))
                    sb.Append(text[i++]);
            }
            tokens.Add(sb.ToString());
        }
        return tokens;
    }
}
