using RetroHub.Core.Covers;

namespace RetroHub.Core.Tests;

public class CoverMatcherTests
{
    private static readonly string[] Names =
    [
        "Chrono Trigger (USA)",
        "Chrono Trigger (Japan)",
        "Chrono Trigger (USA) (Beta)",
        "Legend of Zelda, The - A Link to the Past (USA)",
        "Legend of Zelda, The - A Link to the Past (Europe)",
        "Final Fantasy VII (USA) (Disc 1)",
        "Final Fantasy VII (Europe) (Disc 1)",
        "Pokemon - FireRed Version (USA, Europe)",
        "Sonic & Knuckles (World)",
        "Super Mario World (USA)",
        "Sonic The Hedgehog 2 (World)",
        "Super Mario Bros. (World)",
        "Super Mario Bros. 3 (USA)",
    ];

    private static string? Match(string romStem, string title) =>
        CoverMatcher.BestMatch(new CoverMatcher.Index(Names), romStem, title);

    [Theory]
    [InlineData("Chrono Trigger (Japan)", "Chrono Trigger", "Chrono Trigger (Japan)")]          // nome No-Intro exato
    [InlineData("chrono_trigger", "Chrono Trigger", "Chrono Trigger (USA)")]                     // prefere EUA e versão final
    [InlineData("zelda alttp", "The Legend of Zelda - A Link to the Past", "Legend of Zelda, The - A Link to the Past (USA)")]
    [InlineData("Legend of Zelda (E)", "The Legend of Zelda: A Link to the Past", "Legend of Zelda, The - A Link to the Past (USA)")]
    [InlineData("FF7 (Europe) (Disc 1)", "Final Fantasy VII", "Final Fantasy VII (Europe) (Disc 1)")] // respeita a região do arquivo
    [InlineData("pkmn fr", "Pokémon - FireRed Version", "Pokemon - FireRed Version (USA, Europe)")] // ignora acento
    [InlineData("sonic and knuckles", "Sonic and Knuckles", "Sonic & Knuckles (World)")]
    [InlineData("Jogo Que Nao Existe", "Jogo Que Nao Existe", null)]
    // O servidor diferencia maiúsculas: devolve a grafia do índice, não a do arquivo.
    [InlineData("Sonic the Hedgehog 2 (World)", "Sonic the Hedgehog 2", "Sonic The Hedgehog 2 (World)")]
    // Nomes abreviados: um título contido no outro.
    [InlineData("Zelda - A Link to the Past", "Zelda - A Link to the Past", "Legend of Zelda, The - A Link to the Past (USA)")]
    [InlineData("Pokemon FireRed", "Pokemon FireRed", "Pokemon - FireRed Version (USA, Europe)")]
    // Mas não chuta: "Super Mario" é curto demais perto de qualquer jogo da série.
    [InlineData("Super Mario", "Super Mario", null)]
    [InlineData("Super Mario Bros", "Super Mario Bros", "Super Mario Bros. (World)")]
    public void Finds_the_right_box_art(string romStem, string title, string? expected) =>
        Assert.Equal(expected, Match(romStem, title));
}
