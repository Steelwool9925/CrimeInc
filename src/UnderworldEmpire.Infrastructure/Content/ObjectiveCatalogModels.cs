using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>objectives.json</c> schema. Extra JSON fields are ignored, like the other
/// content files; every property is defaulted so a malformed file yields a collected validation error
/// naming the field rather than a byte offset.
/// </summary>
public sealed record ObjectiveCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("objectives")]
    public List<ObjectiveJson> Objectives { get; init; } = [];
}

/// <summary>One raw objective row.</summary>
public sealed record ObjectiveJson
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("target")]
    public long Target { get; init; }

    [JsonPropertyName("minLevel")]
    public int MinLevel { get; init; } = 1;

    /// <summary>Nullable so an omitted block is a named error, not a silently free objective.</summary>
    [JsonPropertyName("reward")]
    public ObjectiveRewardJson? Reward { get; init; }
}

/// <summary>What claiming an objective pays.</summary>
public sealed record ObjectiveRewardJson
{
    [JsonPropertyName("cash")]
    public long Cash { get; init; }

    [JsonPropertyName("xp")]
    public long Xp { get; init; }
}
