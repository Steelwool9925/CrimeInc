namespace UnderworldEmpire.Domain.Timed;

/// <summary>
/// A single timed-job definition as the domain sees it, projected from the content pack's
/// <c>timed-jobs.json</c>. Immutable: the catalog is loaded once at startup and never mutated.
/// </summary>
/// <remarks>
/// Mirrors <c>Jobs/Job.cs</c>, but with no stat/item/risk blocks — a timed job never fails its
/// roll (see <see cref="TimedJobService"/>), so there is nothing to gate on besides level and
/// energy.
/// </remarks>
public sealed record TimedJob
{
    /// <summary>Stable content id, unique within the pack. Used as the route value.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing name, supplied by content.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing flavour text, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>How long the appointment runs before it is ready to collect.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>Energy consumed at start; always &gt;= 1 so a timed job can never be free.</summary>
    public required int EnergyCost { get; init; }

    /// <summary>Minimum player level permitted to start this appointment; always &gt;= 1.</summary>
    public required int LevelRequirement { get; init; }

    /// <summary>Lowest cash payout, inclusive. Rolled at collect, not at start.</summary>
    public required long CashMin { get; init; }

    /// <summary>Highest cash payout, inclusive.</summary>
    public required long CashMax { get; init; }

    /// <summary>Experience granted on collect. Fixed, not rolled.</summary>
    public required long Xp { get; init; }

    /// <summary>
    /// Validates the invariants the engine relies on. Called by the content loader after its own
    /// error-collecting pass, and directly by tests that build a <see cref="TimedJob"/> by hand.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Id"/> or <see cref="Name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric invariant is violated.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Timed job id must not be blank.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Timed job '{Id}' has a blank name.", nameof(Name));
        if (DurationSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds), DurationSeconds,
                $"Timed job '{Id}' must run for at least 1 second.");
        if (EnergyCost < 1)
            throw new ArgumentOutOfRangeException(nameof(EnergyCost), EnergyCost,
                $"Timed job '{Id}' must cost at least 1 energy.");
        if (LevelRequirement < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelRequirement), LevelRequirement,
                $"Timed job '{Id}' must require at least level 1.");
        if (CashMin < 0)
            throw new ArgumentOutOfRangeException(nameof(CashMin), CashMin,
                $"Timed job '{Id}' cannot pay negative cash.");
        if (CashMax < CashMin)
            throw new ArgumentOutOfRangeException(nameof(CashMax), CashMax,
                $"Timed job '{Id}' has cashMax below cashMin ({CashMin}).");
        if (Xp < 0)
            throw new ArgumentOutOfRangeException(nameof(Xp), Xp,
                $"Timed job '{Id}' cannot grant negative XP.");
    }
}
