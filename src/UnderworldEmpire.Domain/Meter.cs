namespace UnderworldEmpire.Domain;

/// <summary>
/// An immutable resource meter (energy / stamina / health) with lazy regeneration.
/// The current value is computed on read from a (StoredValue, StoredAtUtc) snapshot
/// plus elapsed time — no background job is required.
/// </summary>
public readonly record struct Meter
{
    /// <summary>Maximum capacity; always &gt;= 0.</summary>
    public int Max { get; init; }

    /// <summary>Value that was accurate at <see cref="StoredAtUtc"/>; 0 &lt;= value &lt;= Max.</summary>
    public int StoredValue { get; init; }

    /// <summary>UTC instant at which <see cref="StoredValue"/> was accurate.</summary>
    public DateTimeOffset StoredAtUtc { get; init; }

    /// <summary>Initialises a <see cref="Meter"/> with validation.</summary>
    public Meter(int max, int storedValue, DateTimeOffset storedAtUtc)
    {
        if (max < 0)
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max must be >= 0.");
        if (storedValue < 0 || storedValue > max)
            throw new ArgumentOutOfRangeException(nameof(storedValue), storedValue,
                $"StoredValue must be between 0 and Max ({max}).");

        Max = max;
        StoredValue = storedValue;
        StoredAtUtc = storedAtUtc;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>Creates a meter at full capacity.</summary>
    public static Meter Full(int max, DateTimeOffset atUtc) => new Meter(max, max, atUtc);

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current value at <paramref name="nowUtc"/>, applying lazy
    /// regeneration at the given rate.
    /// </summary>
    /// <param name="nowUtc">The wall-clock instant to evaluate at.</param>
    /// <param name="regenPerPoint">
    ///     Time required to regen one point. If &lt;= <see cref="TimeSpan.Zero"/>
    ///     or <see cref="StoredValue"/> is already at <see cref="Max"/>, no regen
    ///     is applied.
    /// </param>
    public int CurrentAt(DateTimeOffset nowUtc, TimeSpan regenPerPoint)
    {
        // No regen: disabled rate or already full
        if (regenPerPoint <= TimeSpan.Zero || StoredValue >= Max)
            return Math.Min(Max, StoredValue);

        TimeSpan elapsed = nowUtc - StoredAtUtc;
        if (elapsed <= TimeSpan.Zero)
            return StoredValue;

        int points = (int)(elapsed.Ticks / regenPerPoint.Ticks);
        return Math.Min(Max, StoredValue + points);
    }

    // -------------------------------------------------------------------------
    // Mutations (return new struct; clock resets on spend)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deducts <paramref name="amount"/> from the current value. Resets the regen
    /// clock to <paramref name="nowUtc"/> at the reduced value so that subsequent
    /// regen starts from here.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">amount is negative.</exception>
    /// <exception cref="InvalidOperationException">amount exceeds current value.</exception>
    public Meter SpendAt(int amount, DateTimeOffset nowUtc, TimeSpan regenPerPoint)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be >= 0.");

        int cur = CurrentAt(nowUtc, regenPerPoint);
        if (amount > cur)
            throw new InvalidOperationException(
                $"Insufficient meter: tried to spend {amount} but current value is {cur}.");

        return new Meter(Max, cur - amount, nowUtc);
    }

    /// <summary>
    /// Adds <paramref name="amount"/> to the current value, capped at
    /// <see cref="Max"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">amount is negative.</exception>
    public Meter GrantAt(int amount, DateTimeOffset nowUtc, TimeSpan regenPerPoint)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be >= 0.");

        int cur = CurrentAt(nowUtc, regenPerPoint);
        return new Meter(Max, Math.Min(Max, cur + amount), nowUtc);
    }

    /// <summary>Refills the meter to <see cref="Max"/> at <paramref name="nowUtc"/>.</summary>
    public Meter RefillAt(DateTimeOffset nowUtc) => new Meter(Max, Max, nowUtc);
}
