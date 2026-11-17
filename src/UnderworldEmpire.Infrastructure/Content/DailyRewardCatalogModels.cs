using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>daily-rewards.json</c> schema. Extra JSON fields are ignored, like the
/// other content files; every property is defaulted so a malformed file yields a collected
/// validation error naming the field rather than a byte offset.
/// </summary>
public sealed record DailyRewardCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("dailyRewards")]
    public List<DailyRewardJson> DailyRewards { get; init; } = [];
}

/// <summary>One raw daily-reward row.</summary>
public sealed record DailyRewardJson
{
    [JsonPropertyName("day")]
    public int Day { get; init; }

    [JsonPropertyName("cash")]
    public long Cash { get; init; }

    [JsonPropertyName("xp")]
    public long Xp { get; init; }
}
