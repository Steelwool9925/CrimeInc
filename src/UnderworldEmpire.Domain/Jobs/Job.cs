namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// A single job definition as the domain sees it, projected from the content pack's
/// <c>jobs.json</c>. Immutable: the catalog is loaded once at startup and never mutated.
/// </summary>
/// <remarks>
/// Every number here is a tunable balance value supplied by content, never hard-coded in the
/// engine — see <c>content/crime/README.md</c>. The loader validates raw content and collects all
/// errors before constructing any <see cref="Job"/>, so the guards below are a safety net for
/// programmatic construction (tests, future generators) rather than the primary validation path.
/// </remarks>
public sealed record Job
{
    /// <summary>Stable content id, unique within the pack. Used as the route value and the mastery key.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing job name, supplied by content.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing flavour text, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Grouping key for the UI (for example <c>downtown</c>). Purely presentational.</summary>
    public string Area { get; init; } = string.Empty;

    /// <summary>Energy consumed per attempt; always &gt;= 1 so a job can never be free.</summary>
    public required int EnergyCost { get; init; }

    /// <summary>Minimum player level permitted to attempt this job; always &gt;= 1.</summary>
    public required int LevelRequirement { get; init; }

    /// <summary>Lowest cash payout, inclusive.</summary>
    public required long CashMin { get; init; }

    /// <summary>Highest cash payout, inclusive.</summary>
    public required long CashMax { get; init; }

    /// <summary>Experience granted per successful attempt. Fixed, not rolled.</summary>
    public required long Xp { get; init; }

    /// <summary>
    /// Validates the invariants the engine relies on. Called by the content loader after its own
    /// error-collecting pass, and directly by tests that build a <see cref="Job"/> by hand.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Id"/> or <see cref="Name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric invariant is violated.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Job id must not be blank.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Job '{Id}' has a blank name.", nameof(Name));
        if (EnergyCost < 1)
            throw new ArgumentOutOfRangeException(nameof(EnergyCost), EnergyCost,
                $"Job '{Id}' must cost at least 1 energy.");
        if (LevelRequirement < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelRequirement), LevelRequirement,
                $"Job '{Id}' must require at least level 1.");
        if (CashMin < 0)
            throw new ArgumentOutOfRangeException(nameof(CashMin), CashMin,
                $"Job '{Id}' cannot pay negative cash.");
        if (CashMax < CashMin)
            throw new ArgumentOutOfRangeException(nameof(CashMax), CashMax,
                $"Job '{Id}' has cashMax below cashMin ({CashMin}).");
        if (Xp < 0)
            throw new ArgumentOutOfRangeException(nameof(Xp), Xp,
                $"Job '{Id}' cannot grant negative XP.");
    }
}
