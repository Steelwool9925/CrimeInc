using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>jobs.json</c> schema. Extra JSON fields are silently ignored, matching
/// the tolerance <see cref="ContentPack"/> already applies to <c>pack.json</c>.
/// </summary>
/// <remarks>
/// These are raw deserialization shapes with permissive defaults — every property is nullable or
/// defaulted so that a malformed file produces a collected validation error naming the missing
/// field, rather than a <c>JsonException</c> that names only a byte offset.
/// </remarks>
public sealed record JobCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("mastery")]
    public MasteryRulesJson Mastery { get; init; } = new();

    [JsonPropertyName("jobs")]
    public List<JobJson> Jobs { get; init; } = [];
}

/// <summary>Pack-wide mastery thresholds, applied to every job.</summary>
public sealed record MasteryRulesJson
{
    [JsonPropertyName("completionsPerRank")]
    public int CompletionsPerRank { get; init; }

    [JsonPropertyName("maxRank")]
    public int MaxRank { get; init; }
}

/// <summary>One entry in the <c>jobs</c> array.</summary>
public sealed record JobJson
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("area")]
    public string Area { get; init; } = string.Empty;

    [JsonPropertyName("energyCost")]
    public int EnergyCost { get; init; }

    [JsonPropertyName("levelRequirement")]
    public int LevelRequirement { get; init; }

    [JsonPropertyName("payout")]
    public JobPayoutJson Payout { get; init; } = new();
}

/// <summary>The reward block for one job. Cash is a range; XP is fixed.</summary>
public sealed record JobPayoutJson
{
    [JsonPropertyName("cashMin")]
    public long CashMin { get; init; }

    [JsonPropertyName("cashMax")]
    public long CashMax { get; init; }

    [JsonPropertyName("xp")]
    public long Xp { get; init; }
}
