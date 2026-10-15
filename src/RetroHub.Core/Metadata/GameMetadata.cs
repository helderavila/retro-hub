using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroHub.Core.Metadata;

/// <summary>Game info (stored in games.metadata_json).</summary>
public sealed record GameMetadata
{
    /// <summary>Official No-Intro/Redump name ("Chrono Trigger (USA)").</summary>
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Developer { get; init; }
    public string? Publisher { get; init; }
    public string? Genre { get; init; }
    public string? Year { get; init; }
    public string? Region { get; init; }
    public string? Players { get; init; }
    /// <summary>Serial from the official list; useful when the file (e.g. .chd) does not allow reading the serial directly.</summary>
    public string? Serial { get; init; }
    /// <summary>true = the user edited the info; automatic lookup no longer overwrites it.</summary>
    public bool? ManualInfo { get; init; }

    /// <summary>"ok" (matches the official dump), "unknown" (not found: modified, corrupted or uncatalogued), "unsupported".</summary>
    public string? VerifyStatus { get; init; }
    public string? VerifyDetail { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }

    /// <summary>English format (translation key) of the "ok" <see cref="VerifyDetail"/>; {0} = dump name.</summary>
    public const string MatchedDumpFormat = "Matches the official dump: {0}";

    /// <summary>Prefixes "ok" details may start with: English, and Portuguese (current pt-BR translation and data stored by older versions).</summary>
    private static readonly string[] MatchedDumpPrefixes = ["Matches the official dump: ", "Bate com o dump oficial: "];

    /// <summary>"ok" detail without the "Matches the official dump: " prefix (English or Portuguese), i.e. the dump name.</summary>
    public static string? StripMatchedDumpPrefix(string? detail)
    {
        if (detail is null)
            return null;
        foreach (var prefix in MatchedDumpPrefixes)
            if (detail.StartsWith(prefix, StringComparison.Ordinal))
                return detail[prefix.Length..];
        return detail;
    }

    [JsonIgnore]
    public bool HasInfo => Description is not null || Developer is not null || Genre is not null || Year is not null || Publisher is not null;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static GameMetadata? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return JsonSerializer.Deserialize<GameMetadata>(json, Json); }
        catch (JsonException) { return null; }
    }
}
