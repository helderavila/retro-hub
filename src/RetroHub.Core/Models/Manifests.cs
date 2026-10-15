namespace RetroHub.Core.Models;

// ── systems.json ────────────────────────────────────────────────────────────

public sealed class SystemsManifest
{
    public int SchemaVersion { get; set; } = 1;
    public List<SystemDefinition> Systems { get; set; } = [];
}

public sealed class SystemDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Manufacturer { get; set; } = "";

    /// <summary>Extensões aceitas (com ponto, minúsculas). Podem se repetir entre sistemas.</summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>Nomes de pasta que indicam este sistema (ex.: "ps2", "playstation 2").</summary>
    public List<string> FolderAliases { get; set; } = [];

    public string? DefaultEmulator { get; set; }

    /// <summary>Pasta do sistema em thumbnails.libretro.com (ex.: "Sony - PlayStation 2"). Sem ela, não há busca de capa.</summary>
    public string? Thumbnails { get; set; }

    /// <summary>Pasta do .dat oficial em libretro-database/metadat ("no-intro", "redump", "fbneo-split"). O nome do arquivo é o de <see cref="Thumbnails"/>.</summary>
    public string? Dat { get; set; }

    public BiosRule BiosRule { get; set; } = BiosRule.None;
    public List<BiosRequirement> Bios { get; set; } = [];

    /// <summary>Se o emulador padrão traz BIOS emulada (HLE), descreve qual. O sistema roda sem dump.</summary>
    public string? BiosHle { get; set; }
}

public enum BiosRule
{
    /// <summary>BIOS não é necessária (a lista é apenas informativa).</summary>
    None,
    /// <summary>Basta uma das BIOS listadas.</summary>
    Any,
    /// <summary>Todas as BIOS não opcionais são necessárias.</summary>
    All,
}

public sealed class BiosRequirement
{
    /// <summary>Nome esperado do arquivo. Aceita curingas (*, ?) quando não há MD5.</summary>
    public string File { get; set; } = "";
    public List<string> Md5 { get; set; } = [];
    public long? Size { get; set; }

    /// <summary>Texto ASCII que precisa aparecer nos primeiros 64 KiB (usado quando não há MD5, ex.: "RESET" no ROMDIR do PS2).</summary>
    public string? Signature { get; set; }

    public string Description { get; set; } = "";
    public bool Optional { get; set; }
}

// ── emulators/*.json ────────────────────────────────────────────────────────

public sealed class EmulatorManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Homepage { get; set; }
    public string? License { get; set; }
    public List<string> Systems { get; set; } = [];
    public SourceSpec Source { get; set; } = new();

    /// <summary>Executável relativo à pasta de instalação. Aceita curingas.</summary>
    public string Executable { get; set; } = "";

    public ExtractOptions Extract { get; set; } = new();
    public List<PostInstallAction> PostInstall { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Pasta (relativa à instalação) onde o emulador procura BIOS.</summary>
    public string? BiosDir { get; set; }

    public List<LaunchProfile> Launch { get; set; } = [];
    public CoreSource? Cores { get; set; }

    /// <summary>Mapeamento de controle gravado no arquivo de configuração do emulador.</summary>
    public ControllerConfig? Controller { get; set; }

    /// <summary>Texto para emuladores sem mapeamento automático (ex.: como configurar o controle neles).</summary>
    public string? ControllerNote { get; set; }

    /// <summary>Onde ficam save states e saves (para listar, continuar e fazer backup).</summary>
    public SavesConfig? Saves { get; set; }

    /// <summary>Arquivos (relativos à instalação) que uma atualização nunca sobrescreve — configurações do usuário.</summary>
    public List<string> Preserve { get; set; } = [];

    /// <summary>Resolução interna e proporção de tela, gravadas na configuração do emulador.</summary>
    public GraphicsConfig? Graphics { get; set; }

    /// <summary>RetroAchievements: chaves que ligam as conquistas no emulador.</summary>
    public AchievementsConfig? Achievements { get; set; }

    /// <summary>Arquivo do manifesto quando ele veio da pasta do usuário (adicionado por ele); null = veio com o app.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? UserManifestPath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCustom => UserManifestPath is not null;
}

public sealed class GraphicsConfig
{
    public string File { get; set; } = "";
    public string Format { get; set; } = "ini";

    /// <summary>Níveis prontos: "Original", "Melhorado", "Máximo" (cada um grava várias chaves de uma vez).</summary>
    public GraphicsOption? Presets { get; set; }

    public GraphicsOption? Resolution { get; set; }
    public GraphicsOption? Aspect { get; set; }

    /// <summary>Liga/desliga independentes (widescreen, texturas HD, filtro CRT…).</summary>
    public List<GraphicsToggle> Toggles { get; set; } = [];
}

public sealed class GraphicsToggle
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Explicação curta na tela (ex.: onde colocar o pacote de texturas).</summary>
    public string? Hint { get; set; }
    public Dictionary<string, Dictionary<string, string>> On { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> Off { get; set; } = [];
    /// <summary>Arquivo criado ao ligar e apagado ao desligar (ex.: preset de shader global do RetroArch).</summary>
    public ToggleFile? File { get; set; }
}

public sealed class ToggleFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>Uma opção de imagem: rótulo mostrado → chaves gravadas (seção → chave → valor).</summary>
public sealed class GraphicsOption
{
    /// <summary>Ordem = ordem dos chips na tela. Ex.: "2x" → { "EmuCore/GS": { "upscale_multiplier": "2" } }.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Values { get; set; } = [];
}

public sealed class AchievementsConfig
{
    public string File { get; set; } = "";
    public string Format { get; set; } = "ini";
    /// <summary>Chaves para ligar. Placeholders: {user}, {password}.</summary>
    public Dictionary<string, Dictionary<string, string>> Enable { get; set; } = [];
    /// <summary>true se o emulador recebe usuário/senha pelo arquivo; false se o login é feito dentro dele (token).</summary>
    public bool TakesCredentials { get; set; }
}

public sealed class SavesConfig
{
    public List<StateLocation> States { get; set; } = [];

    /// <summary>
    /// Argumentos para abrir já no save state. Placeholders: os do launch + {state} (arquivo) e {slot} (número).
    /// Sem isso, o jogo abre normalmente e o state é carregado pelo menu do emulador.
    /// </summary>
    public string? LoadStateArgs { get; set; }

    /// <summary>Pastas (relativas à instalação) copiadas no backup: memory cards, saves, states.</summary>
    public List<string> Backup { get; set; } = [];
}

public sealed class StateLocation
{
    public string Dir { get; set; } = "";
    /// <summary>Padrão de arquivo. Placeholders: {serial}, {serialNoDash}, {romName}.</summary>
    public string Pattern { get; set; } = "";
    /// <summary>Regex com o número do slot no grupo 1 (vazio = slot 0).</summary>
    public string? Slot { get; set; }
}

public sealed class ControllerConfig
{
    /// <summary>Arquivo relativo à pasta do emulador (ex.: "inis/PCSX2.ini").</summary>
    public string File { get; set; } = "";

    /// <summary>"ini" (<c>[Seção]</c> + <c>chave = valor</c>) ou "flat" (<c>chave = "valor"</c>, estilo retroarch.cfg).</summary>
    public string Format { get; set; } = "ini";

    /// <summary>Seção → chave → valor. No formato "flat", use a seção "".</summary>
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = [];

    /// <summary>Texto curto exibido na UI ("Pad 1 · DualShock 2").</summary>
    public string Description { get; set; } = "";
}

public sealed class SourceSpec
{
    /// <summary>
    /// "github" (API de releases), "forgejo" (Forgejo/Gitea, ver <see cref="Api"/>), "url" (link direto fixado)
    /// ou "local" (emulador que já está no computador, em <see cref="Path"/>: nada é baixado nem apagado).
    /// </summary>
    public string Type { get; set; } = "github";

    /// <summary>"local": pasta do emulador ou o próprio .exe.</summary>
    public string? Path { get; set; }

    /// <summary>Base da API para "forgejo" (ex.: "https://codeberg.org/api/v1").</summary>
    public string? Api { get; set; }

    // github
    public string? Repo { get; set; }
    public string? Tag { get; set; }
    public bool Prerelease { get; set; }
    /// <summary>Regex que seleciona o asset da release.</summary>
    public string? Asset { get; set; }

    // url
    public string? Url { get; set; }
    public string? Version { get; set; }

    /// <summary>SHA-256 esperado. Se omitido, usa o digest publicado pelo GitHub (quando houver).</summary>
    public string? Sha256 { get; set; }
}

public sealed class ExtractOptions
{
    /// <summary>Se o arquivo tiver uma única pasta raiz, sobe o conteúdo dela um nível.</summary>
    public bool StripRoot { get; set; } = true;
}

public sealed class PostInstallAction
{
    /// <summary>"createFile" ou "createDir".</summary>
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Content { get; set; }
}

public sealed class LaunchProfile
{
    public List<string> Systems { get; set; } = [];
    /// <summary>Core libretro (apenas RetroArch).</summary>
    public string? Core { get; set; }
    /// <summary>Argumentos. Placeholders: {rom} {romDir} {romName} {emuDir} {core}.</summary>
    public string Args { get; set; } = "\"{rom}\"";
}

public sealed class CoreSource
{
    /// <summary>URL com {core}, ex.: .../latest/{core}_libretro.dll.zip</summary>
    public string UrlTemplate { get; set; } = "";
    public string TargetDir { get; set; } = "cores";
}

// ── dependencies.json ───────────────────────────────────────────────────────

public sealed class DependenciesManifest
{
    public List<DependencyDefinition> Dependencies { get; set; } = [];
}

public sealed class DependencyDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WingetId { get; set; } = "";
    public string Description { get; set; } = "";
}
