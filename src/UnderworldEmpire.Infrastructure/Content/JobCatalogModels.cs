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

    /// <summary>
    /// Pack-wide risk rules. <see langword="null"/> when the block is absent — deliberately not
    /// defaulted.
    /// </summary>
    /// <remarks>
    /// Defaulted to a fresh instance, an omitted block would read as a ceiling of zero: every job in
    /// the pack impossible, reported by nothing. Nullable makes the omission a named validation error
    /// instead. Same reasoning as <see cref="JobJson.Requirements"/> and <see cref="JobJson.Risk"/>.
    /// </remarks>
    [JsonPropertyName("risk")]
    public RiskRulesJson? Risk { get; init; }

    [JsonPropertyName("jobs")]
    public List<JobJson> Jobs { get; init; } = [];
}

/// <summary>Pack-wide risk rules, applied to every job.</summary>
public sealed record RiskRulesJson
{
    [JsonPropertyName("maxSuccessChance")]
    public double MaxSuccessChance { get; init; }

    [JsonPropertyName("consolationCashFraction")]
    public double ConsolationCashFraction { get; init; }

    [JsonPropertyName("consolationXpFraction")]
    public double ConsolationXpFraction { get; init; }
}

/// <summary>Minimum effective stats to attempt a job.</summary>
public sealed record JobRequirementsJson
{
    [JsonPropertyName("attack")]
    public int Attack { get; init; }

    [JsonPropertyName("defense")]
    public int Defense { get; init; }
}

/// <summary>Per-job risk tuning.</summary>
public sealed record JobRiskJson
{
    [JsonPropertyName("baseSuccessChance")]
    public double BaseSuccessChance { get; init; }

    [JsonPropertyName("successChancePerSurplusPoint")]
    public double SuccessChancePerSurplusPoint { get; init; }

    [JsonPropertyName("healthLossMin")]
    public int HealthLossMin { get; init; }

    [JsonPropertyName("healthLossMax")]
    public int HealthLossMax { get; init; }

    /// <summary>Success-chance gain per point of surplus held-item quantity. Defaults to 0 like its siblings.</summary>
    [JsonPropertyName("extraItemSuccessChance")]
    public double ExtraItemSuccessChance { get; init; }
}

/// <summary>
/// One item gate on a job: the attempt needs <see cref="MinQuantity"/> of <see cref="ItemId"/> and uses
/// them all up, and a player may add up to <see cref="MaxExtras"/> more to improve the odds.
/// </summary>
public sealed record JobItemRequirementJson
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("minQuantity")]
    public int MinQuantity { get; init; }

    /// <summary>
    /// The most extra copies a player may commit on top of <see cref="MinQuantity"/>. Optional: absent
    /// means none, which keeps every pack written before this field valid. When above zero the job's
    /// <c>risk.extraItemSuccessChance</c> must also be above zero, or the extras would change nothing.
    /// </summary>
    [JsonPropertyName("maxExtras")]
    public int MaxExtras { get; init; }
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

    /// <summary>
    /// Stat minimums. <see langword="null"/> when the block is absent — deliberately not defaulted,
    /// because zeros would read as "ungated", which is exactly what an author who forgot the block
    /// would fail to notice.
    /// </summary>
    [JsonPropertyName("requirements")]
    public JobRequirementsJson? Requirements { get; init; }

    /// <summary>
    /// Risk tuning. <see langword="null"/> when absent — zeros would read as "always fails", which is
    /// worse than loud.
    /// </summary>
    [JsonPropertyName("risk")]
    public JobRiskJson? Risk { get; init; }

    /// <summary>
    /// Item-quantity gates this job additionally requires, alongside <see cref="Requirements"/>.
    /// <see langword="null"/> or absent means ungated — unlike <see cref="Requirements"/> and
    /// <see cref="Risk"/>, every job that predates this field must keep working without it, so
    /// omission here is the ordinary case rather than a named error.
    /// </summary>
    [JsonPropertyName("itemRequirements")]
    public List<JobItemRequirementJson>? ItemRequirements { get; init; }
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
