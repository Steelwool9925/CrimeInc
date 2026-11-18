using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>timed-jobs.json</c> schema. Extra JSON fields are ignored, like the
/// other content files; every property is defaulted so a malformed file yields a collected
/// validation error naming the field rather than a byte offset.
/// </summary>
public sealed record TimedJobCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("timedJobs")]
    public List<TimedJobJson> TimedJobs { get; init; } = [];
}

/// <summary>One raw timed-job row.</summary>
public sealed record TimedJobJson
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; init; }

    [JsonPropertyName("energyCost")]
    public int EnergyCost { get; init; }

    [JsonPropertyName("levelRequirement")]
    public int LevelRequirement { get; init; } = 1;

    /// <summary>Nullable so an omitted block is a named error, not a silently free appointment.</summary>
    [JsonPropertyName("payout")]
    public TimedJobPayoutJson? Payout { get; init; }
}

/// <summary>What collecting a timed job pays.</summary>
public sealed record TimedJobPayoutJson
{
    [JsonPropertyName("cashMin")]
    public long CashMin { get; init; }

    [JsonPropertyName("cashMax")]
    public long CashMax { get; init; }

    [JsonPropertyName("xp")]
    public long Xp { get; init; }
}
