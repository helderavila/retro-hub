using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.Core.Localization;

namespace RetroHub.Core.Tests;

/// <summary>Interface languages: English by default, Portuguese from i18n\pt-BR\*.json.</summary>
[Collection("Localization")] // L is static: these tests must not run in parallel with each other
public class LocalizationTests
{
    private static string I18nDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetroHub.App", "i18n"));

    [Fact]
    public void English_is_the_key_and_portuguese_comes_from_the_files()
    {
        try
        {
            L.Load(L.Portuguese, I18nDir);
            Assert.Equal("pt-BR", L.Language);
            Assert.Equal("Zerados", L.T("Completed"));
            Assert.Equal("Máximo", L.T("Maximum"));
            Assert.Equal("Texto que ninguém traduziu", L.T("Texto que ninguém traduziu")); // missing → the key itself

            L.Load(L.English, I18nDir);
            Assert.Equal("Completed", L.T("Completed"));
            Assert.Equal("Maximum", L.T("Maximum"));
        }
        finally
        {
            L.Load(L.English, I18nDir);
        }
    }

    /// <summary>Texts only the Mac app has (Finder, Trash, Rosetta…).</summary>
    private static string MacI18nDir => Path.GetFullPath(Path.Combine(I18nDir, "..", "..", "RetroHub.Mac", "i18n"));

    [Fact]
    public void Every_translation_keeps_the_same_placeholders()
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(I18nDir, "pt-BR"), "*.json")
                     .Concat(Directory.EnumerateFiles(Path.Combine(MacI18nDir, "pt-BR"), "*.json")))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), options);
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                static string Holes(string s) => string.Join(",", Regex.Matches(s, @"\{\d+(?:[:,][^}]*)?\}").Select(m => m.Value).Order());
                if (Holes(entry.Name) != Holes(entry.Value.GetString() ?? ""))
                    problems.Add($"{Path.GetFileName(file)}: \"{entry.Name}\"");
            }
        }
        Assert.Empty(problems);
    }
}
