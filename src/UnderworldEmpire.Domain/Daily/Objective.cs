namespace UnderworldEmpire.Domain.Daily;

/// <summary>What an objective counts. Each maps to one fact an action reports.</summary>
public enum ObjectiveType
{
    /// <summary>Win this many <i>different</i> jobs in one UTC day. The type that gives every job a purpose.</summary>
    CompleteDistinctJobs,

    /// <summary>Win jobs this many times in one UTC day: any job, or one named job when <see cref="Objective.JobId"/> is set.</summary>
    CompleteJobTimes,

    /// <summary>Earn this much cash from jobs in one UTC day.</summary>
    EarnCash,

    /// <summary>Collect from properties this many times in one UTC day.</summary>
    CollectProperties
}

/// <summary>
/// One entry in the content pack's objective pool, projected from <c>objectives.json</c>. Immutable:
/// the pool is loaded once at startup.
/// </summary>
/// <remarks>
/// Every number is a tunable content value. The loader validates raw content and collects all errors
/// first, so the guards in <see cref="EnsureValid"/> are a safety net for programmatic construction.
/// </remarks>
public sealed record Objective
{
    /// <summary>Stable content id, unique within the pool. Keys the player's progress and the claim route.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing title.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing detail, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>What is being counted.</summary>
    public required ObjectiveType Type { get; init; }

    /// <summary>The count (or cash amount) that completes the objective; always &gt;= 1.</summary>
    public required long Target { get; init; }

    /// <summary>
    /// The job a <see cref="ObjectiveType.CompleteJobTimes"/> objective is about, or
    /// <see langword="null"/> for any job. Meaningless for the other types.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// The lowest player level this objective is offered to; always &gt;= 1. Keeps a level-1 player from
    /// being handed a job they cannot yet attempt.
    /// </summary>
    public int MinLevel { get; init; } = 1;

    /// <summary>Cash paid on claim; never negative.</summary>
    public long RewardCash { get; init; }

    /// <summary>Experience paid on claim; never negative.</summary>
    public long RewardXp { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> when a value breaks the invariants above.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Objective id is required.");
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException($"Objective '{Id}' needs a name.");
        if (!Enum.IsDefined(Type)) throw new ArgumentException($"Objective '{Id}' has an unknown type.");
        if (Target < 1) throw new ArgumentException($"Objective '{Id}' target must be >= 1.");
        if (MinLevel < 1) throw new ArgumentException($"Objective '{Id}' minLevel must be >= 1.");
        if (RewardCash < 0 || RewardXp < 0) throw new ArgumentException($"Objective '{Id}' rewards cannot be negative.");
        if (RewardCash == 0 && RewardXp == 0) throw new ArgumentException($"Objective '{Id}' must pay something.");
    }
}
