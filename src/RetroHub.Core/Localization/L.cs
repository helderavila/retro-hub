using System.Globalization;
using System.Text.Json;

namespace RetroHub.Core.Localization;

/// <summary>
/// Interface text. The English text is the key: <c>L.T("Install")</c> returns it as is in English and the
/// translation in other languages (i18n\{language}\*.json, flat objects "English": "Translation").
/// A missing translation falls back to English, so nothing ever shows a raw key.
/// The language is chosen once at startup (changing it asks for a restart).
/// </summary>
public static class L
{
    public const string English = "en";
    public const string Portuguese = "pt-BR";
    public const string Setting = "language";

    public static readonly IReadOnlyList<(string Code, string Name)> Languages = [(English, "English"), (Portuguese, "Português")];

    private static Dictionary<string, string> _strings = [];

    public static string Language { get; private set; } = English;
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Loads every .json in <paramref name="i18nDir"/>\{language}. Unknown language or missing folder = English.</summary>
    public static void Load(string? language, string i18nDir)
    {
        _strings = [];
        Language = English;
        Culture = CultureInfo.GetCultureInfo("en-US");
        if (string.IsNullOrWhiteSpace(language) || language == English)
            return;

        var dir = Path.Combine(i18nDir, language);
        if (!Directory.Exists(dir))
            return;

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), options);
                foreach (var entry in doc.RootElement.EnumerateObject())
                    if (entry.Value.ValueKind == JsonValueKind.String)
                        _strings[entry.Name] = entry.Value.GetString()!;
            }
            catch (JsonException) { /* a broken file only loses its own translations */ }
        }
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language);
    }

    /// <summary>For tests: use exactly these translations.</summary>
    public static void Use(string language, IReadOnlyDictionary<string, string> strings)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language == English ? "en-US" : language);
        _strings = new Dictionary<string, string>(strings);
    }

    /// <summary>Translated text (English when there is no translation).</summary>
    public static string T(string english) =>
        Language != English && _strings.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>Translated format string filled with <paramref name="args"/>: <c>L.F("{0} games", n)</c>.</summary>
    public static string F(string english, params object?[] args) => string.Format(Culture, T(english), args);

    /// <summary>Short date in the interface language (10/13/2026 · 13/10/2026).</summary>
    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("d", Culture);

    public static string DateTime(DateTimeOffset value) => value.ToLocalTime().ToString("g", Culture);
}
