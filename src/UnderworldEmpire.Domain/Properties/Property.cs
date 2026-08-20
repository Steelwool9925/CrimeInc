namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// A single income-producing property definition as the domain sees it, projected from the content
/// pack's <c>properties.json</c>. Immutable: the catalog is loaded once at startup and never mutated.
/// </summary>
/// <remarks>
/// Every number here is a tunable balance value supplied by content, never hard-coded in the engine —
/// see <c>content/crime/README.md</c>. The loader validates raw content and collects all errors before
/// constructing any <see cref="Property"/>, so the guards in <see cref="EnsureValid"/> are a safety net
/// for programmatic construction (tests, future generators) rather than the primary validation path.
/// Same division of labour as <see cref="Items.Item"/>.
/// </remarks>
public sealed record Property
{
    /// <summary>Stable content id, unique within the pack. The route value and the holdings key.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing property name, supplied by content.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing flavour text, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Minimum player level permitted to buy this property; always &gt;= 1.</summary>
    public required int LevelRequirement { get; init; }

    /// <summary>Cash cost of one unit. Fixed, not rolled — a price the player can plan against.</summary>
    public required long Price { get; init; }

    /// <summary>Cash one unit earns per hour of elapsed time. Always &gt;= 1.</summary>
    /// <remarks>
    /// Expressed per hour rather than per tick so a content author tunes a figure they can reason about.
    /// Income is accrued lazily from elapsed time, so the engine never runs a timer per property.
    /// </remarks>
    public required long IncomePerHour { get; init; }

    /// <summary>
    /// How many minutes of income one unit can hold before accrual stops. Always &gt;= 1.
    /// </summary>
    /// <remarks>
    /// The reason to come back: uncollected income stops growing at this window, so a property left alone
    /// for a week pays the same as one left alone for its window. Expressed in minutes rather than hours
    /// so an early-game property can have a window short enough to watch fill.
    /// </remarks>
    public required int AccrualCapMinutes { get; init; }

    /// <summary>
    /// The most uncollected cash <b>one unit</b> can hold: the income earned over a full window.
    /// </summary>
    /// <remarks>
    /// Floored to whole cash. <see cref="EnsureValid"/> refuses a rate/window pair that floors to zero,
    /// because such a property would charge for a purchase and then never pay anything back.
    /// </remarks>
    public long CapPerUnit => IncomePerHour * AccrualCapMinutes / 60L;

    /// <summary>
    /// Validates the invariants the engine relies on. Called by the content loader after its own
    /// error-collecting pass, and directly by tests that build a <see cref="Property"/> by hand.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Id"/> or <see cref="Name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric invariant is violated.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Property id must not be blank.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Property '{Id}' has a blank name.", nameof(Name));
        if (LevelRequirement < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelRequirement), LevelRequirement,
                $"Property '{Id}' must require at least level 1.");
        if (Price < 0)
            throw new ArgumentOutOfRangeException(nameof(Price), Price,
                $"Property '{Id}' cannot cost negative cash.");

        // A property is bought for its income, so one that pays nothing is a content mistake rather than
        // a design choice — it would sit in the store taking cash and returning nothing.
        if (IncomePerHour < 1)
            throw new ArgumentOutOfRangeException(nameof(IncomePerHour), IncomePerHour,
                $"Property '{Id}' must earn at least 1 cash per hour.");

        if (AccrualCapMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(AccrualCapMinutes), AccrualCapMinutes,
                $"Property '{Id}' must hold at least one minute of income.");

        // Rate and window are independently valid but can still multiply out to nothing once floored —
        // 30/hour over one minute is half a coin. Caught here rather than at collection time, where it
        // would look like a payout bug instead of the content mistake it is.
        if (CapPerUnit < 1)
            throw new ArgumentOutOfRangeException(nameof(AccrualCapMinutes), AccrualCapMinutes,
                $"Property '{Id}' earns {IncomePerHour}/hour over a {AccrualCapMinutes}-minute window, " +
                "which floors to zero cash — it could never pay out.");
    }
}
