using System.Text.RegularExpressions;

namespace RetroHub.Core.Util;

public static class Glob
{
    public static bool HasWildcards(string pattern) => pattern.IndexOfAny(['*', '?']) >= 0;

    public static bool IsMatch(string name, string pattern) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
