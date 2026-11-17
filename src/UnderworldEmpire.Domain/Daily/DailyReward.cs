namespace UnderworldEmpire.Domain.Daily;

/// <summary>
/// One entry in the content pack's daily-login reward table, projected from
/// <c>daily-rewards.json</c>. Immutable: the table is loaded once at startup.
/// </summary>
/// <remarks>
/// Every number is a tunable content value. The loader validates raw content and collects all
/// errors first, so the guards in <see cref="EnsureValid"/> are a safety net for programmatic
/// construction, exactly as on <see cref="Objective"/>.
/// </remarks>
public sealed record DailyReward
{
    /// <summary>The streak day this reward belongs to; always &gt;= 1.</summary>
    public required int Day { get; init; }

    /// <summary>Cash paid on claim; never negative.</summary>
    public required long Cash { get; init; }

    /// <summary>Experience paid on claim; never negative.</summary>
    public required long Xp { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> when a value breaks the invariants above.</summary>
    public void EnsureValid()
    {
        if (Day < 1) throw new ArgumentException("Daily reward day must be >= 1.");
        if (Cash < 0 || Xp < 0) throw new ArgumentException($"Daily reward for day {Day} cannot be negative.");
        if (Cash == 0 && Xp == 0) throw new ArgumentException($"Daily reward for day {Day} must pay something.");
    }
}
