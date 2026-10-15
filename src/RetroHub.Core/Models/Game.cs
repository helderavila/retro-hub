namespace RetroHub.Core.Models;

public sealed class Game
{
    public long Id { get; set; }
    public string SystemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Serial { get; set; }
    public string? Crc32 { get; set; }
    public long SizeBytes { get; set; }
    public string? CoverPath { get; set; }
    public string? MetadataJson { get; set; }
    public bool Favorite { get; set; }
    public long PlaySeconds { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public string? EmulatorOverride { get; set; }
    /// <summary>"scan" ou "manual".</summary>
    public string Source { get; set; } = "scan";
}

public sealed class InstalledEmulator
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string? Sha256 { get; set; }
    /// <summary>True quando o hash baixado foi conferido contra um hash publicado.</summary>
    public bool Verified { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
}
