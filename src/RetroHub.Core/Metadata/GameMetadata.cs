using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroHub.Core.Metadata;

/// <summary>Informações do jogo (guardadas em games.metadata_json).</summary>
public sealed record GameMetadata
{
    /// <summary>Nome oficial No-Intro/Redump ("Chrono Trigger (USA)").</summary>
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Developer { get; init; }
    public string? Publisher { get; init; }
    public string? Genre { get; init; }
    public string? Year { get; init; }
    public string? Region { get; init; }
    public string? Players { get; init; }
    /// <summary>Serial pela lista oficial — útil quando o arquivo (ex.: .chd) não deixa ler o serial direto.</summary>
    public string? Serial { get; init; }
    /// <summary>true = o usuário editou as informações; a busca automática não sobrescreve mais.</summary>
    public bool? ManualInfo { get; init; }

    /// <summary>"ok" (bate com o dump oficial), "unknown" (não encontrado: modificado, corrompido ou não catalogado), "unsupported".</summary>
    public string? VerifyStatus { get; init; }
    public string? VerifyDetail { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }

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
