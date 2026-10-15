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

    /// <summary>Accepted extensions (with dot, lowercase). May repeat across systems.</summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>Folder names that indicate this system (e.g. "ps2", "playstation 2").</summary>
    public List<string> FolderAliases { get; set; } = [];

    public string? DefaultEmulator { get; set; }

    /// <summary>System folder on thumbnails.libretro.com (e.g. "Sony - PlayStation 2"). Without it, there is no cover lookup.</summary>
    public string? Thumbnails { get; set; }

    /// <summary>Folder of the official .dat in libretro-database/metadat ("no-intro", "redump", "fbneo-split"). The file name is that of <see cref="Thumbnails"/>.</summary>
    public string? Dat { get; set; }

    public BiosRule BiosRule { get; set; } = BiosRule.None;
    public List<BiosRequirement> Bios { get; set; } = [];

    /// <summary>If the default emulator ships an emulated BIOS (HLE), describes which. The system runs without a dump.</summary>
    public string? BiosHle { get; set; }
}

public enum BiosRule
{
    /// <summary>BIOS is not required (the list is informational only).</summary>
    None,
    /// <summary>Any one of the listed BIOS files is enough.</summary>
    Any,
    /// <summary>All non-optional BIOS files are required.</summary>
    All,
}

public sealed class BiosRequirement
{
    /// <summary>Expected file name. Accepts wildcards (*, ?) when there is no MD5.</summary>
    public string File { get; set; } = "";
    public List<string> Md5 { get; set; } = [];
    public long? Size { get; set; }

    /// <summary>ASCII text that must appear in the first 64 KiB (used when there is no MD5, e.g. "RESET" in the PS2 ROMDIR).</summary>
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

    /// <summary>Executable relative to the install folder. Accepts wildcards.</summary>
    public string Executable { get; set; } = "";

    public ExtractOptions Extract { get; set; } = new();
    public List<PostInstallAction> PostInstall { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Folder (relative to the install) where the emulator looks for BIOS.</summary>
    public string? BiosDir { get; set; }

    public List<LaunchProfile> Launch { get; set; } = [];
    public CoreSource? Cores { get; set; }

    /// <summary>Controller mapping written to the emulator config file.</summary>
    public ControllerConfig? Controller { get; set; }

    /// <summary>Text for emulators without automatic mapping (e.g. how to set up the controller in them).</summary>
    public string? ControllerNote { get; set; }

    /// <summary>Where save states and saves live (for listing, resuming and backup).</summary>
    public SavesConfig? Saves { get; set; }

    /// <summary>Files (relative to the install) that an update never overwrites (user settings).</summary>
    public List<string> Preserve { get; set; } = [];

    /// <summary>Internal resolution and aspect ratio, written to the emulator config.</summary>
    public GraphicsConfig? Graphics { get; set; }

    /// <summary>RetroAchievements: keys that enable achievements in the emulator.</summary>
    public AchievementsConfig? Achievements { get; set; }

    /// <summary>Manifest file when it came from the user folder (added by the user); null = shipped with the app.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? UserManifestPath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCustom => UserManifestPath is not null;
}

public sealed class GraphicsConfig
{
    public string File { get; set; } = "";
    public string Format { get; set; } = "ini";

    /// <summary>Ready-made levels such as "Original", "Enhanced", "Maximum" (each writes several keys at once).</summary>
    public GraphicsOption? Presets { get; set; }

    public GraphicsOption? Resolution { get; set; }
    public GraphicsOption? Aspect { get; set; }

    /// <summary>Independent on/off toggles (widescreen, HD textures, CRT filter…).</summary>
    public List<GraphicsToggle> Toggles { get; set; } = [];
}

public sealed class GraphicsToggle
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Short on-screen explanation (e.g. where to put the texture pack).</summary>
    public string? Hint { get; set; }
    public Dictionary<string, Dictionary<string, string>> On { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> Off { get; set; } = [];
    /// <summary>File created when on and deleted when off (e.g. RetroArch global shader preset).</summary>
    public ToggleFile? File { get; set; }
}

public sealed class ToggleFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>A picture option: displayed label → written keys (section → key → value).</summary>
public sealed class GraphicsOption
{
    /// <summary>Order = order of the chips on screen. E.g. "2x" → { "EmuCore/GS": { "upscale_multiplier": "2" } }.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Values { get; set; } = [];
}

public sealed class AchievementsConfig
{
    public string File { get; set; } = "";
    public string Format { get; set; } = "ini";
    /// <summary>Keys to enable. Placeholders: {user}, {password}.</summary>
    public Dictionary<string, Dictionary<string, string>> Enable { get; set; } = [];
    /// <summary>true if the emulator takes user/password via the file; false if login happens inside it (token).</summary>
    public bool TakesCredentials { get; set; }
}

public sealed class SavesConfig
{
    public List<StateLocation> States { get; set; } = [];

    /// <summary>
    /// Arguments to start directly in the save state. Placeholders: the launch ones + {state} (file) and {slot} (number).
    /// Without it, the game starts normally and the state is loaded from the emulator menu.
    /// </summary>
    public string? LoadStateArgs { get; set; }

    /// <summary>Folders (relative to the install) copied in the backup: memory cards, saves, states.</summary>
    public List<string> Backup { get; set; } = [];
}

public sealed class StateLocation
{
    public string Dir { get; set; } = "";
    /// <summary>File pattern. Placeholders: {serial}, {serialNoDash}, {romName}.</summary>
    public string Pattern { get; set; } = "";
    /// <summary>Regex with the slot number in group 1 (empty = slot 0).</summary>
    public string? Slot { get; set; }
}

public sealed class ControllerConfig
{
    /// <summary>File relative to the emulator folder (e.g. "inis/PCSX2.ini").</summary>
    public string File { get; set; } = "";

    /// <summary>"ini" (<c>[Section]</c> + <c>key = value</c>) or "flat" (<c>key = "value"</c>, retroarch.cfg style).</summary>
    public string Format { get; set; } = "ini";

    /// <summary>Section → key → value. In the "flat" format, use section "".</summary>
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = [];

    /// <summary>Short text shown in the UI ("Pad 1 · DualShock 2").</summary>
    public string Description { get; set; } = "";
}

public sealed class SourceSpec
{
    /// <summary>
    /// "github" (releases API), "forgejo" (Forgejo/Gitea, see <see cref="Api"/>), "url" (pinned direct link)
    /// or "local" (emulator already on the computer, at <see cref="Path"/>: nothing is downloaded or deleted).
    /// </summary>
    public string Type { get; set; } = "github";

    /// <summary>"local": emulator folder or the .exe itself.</summary>
    public string? Path { get; set; }

    /// <summary>API base for "forgejo" (e.g. "https://codeberg.org/api/v1").</summary>
    public string? Api { get; set; }

    // github
    public string? Repo { get; set; }
    public string? Tag { get; set; }
    public bool Prerelease { get; set; }
    /// <summary>Regex that selects the release asset.</summary>
    public string? Asset { get; set; }

    // url
    public string? Url { get; set; }
    public string? Version { get; set; }

    /// <summary>Expected SHA-256. If omitted, uses the digest published by GitHub (when available).</summary>
    public string? Sha256 { get; set; }
}

public sealed class ExtractOptions
{
    /// <summary>If the archive has a single root folder, moves its contents up one level.</summary>
    public bool StripRoot { get; set; } = true;
}

public sealed class PostInstallAction
{
    /// <summary>"createFile" or "createDir".</summary>
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Content { get; set; }
}

public sealed class LaunchProfile
{
    public List<string> Systems { get; set; } = [];
    /// <summary>libretro core (RetroArch only).</summary>
    public string? Core { get; set; }
    /// <summary>Arguments. Placeholders: {rom} {romDir} {romName} {emuDir} {core}.</summary>
    public string Args { get; set; } = "\"{rom}\"";
}

public sealed class CoreSource
{
    /// <summary>URL with {core}, e.g. .../latest/{core}_libretro.dll.zip</summary>
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
