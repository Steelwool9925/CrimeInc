namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// One player's holding of one property: how many units they own, what those units have banked, and
/// when that figure was last accurate. Persisted per (player, property) pair, the same shape as
/// <see cref="Items.PlayerItem"/>.
/// </summary>
/// <remarks>
/// <para>
/// A row is created empty and then filled by <see cref="AddUnits"/>, so a caller can create-or-fetch
/// without deciding the quantity — the same lifecycle as an inventory holding or a mastery row.
/// </para>
/// <para>
/// <b>Two fields, not one.</b> Income could be derived from a timestamp alone if the rate never
/// changed, but buying another unit changes it. <see cref="UncollectedCash"/> banks everything earned
/// at the old rate so the new one is never applied retroactively, and
/// <see cref="AccruedAtUtc"/> carries the fraction of a coin that had not finished earning. Every
/// method that could change the rate settles first, for that reason.
/// </para>
/// <para>
/// Rate, price and window are deliberately <b>not</b> copied onto this row. A retune of
/// <c>properties.json</c> must move every player's income; a snapshot taken at purchase time would
/// freeze balance into player data — the same rule <see cref="Items.PlayerItem"/> follows for stats.
/// That is why every method here takes the definition rather than remembering one.
/// </para>
/// </remarks>
public class PlayerProperty
{
    /// <summary>Owning player. Half of the composite primary key.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Content id of the property. Half of the composite primary key.</summary>
    public string PropertyId { get; init; } = string.Empty;

    /// <summary>How many units the player owns. Zero on a freshly created row.</summary>
    public int Quantity { get; private set; }

    /// <summary>Cash earned and banked but not yet collected, accurate as at <see cref="AccruedAtUtc"/>.</summary>
    /// <remarks>
    /// Not the whole answer to "what can I collect?" — time has usually passed since. Ask
    /// <see cref="PendingAt"/> for that.
    /// </remarks>
    public long UncollectedCash { get; private set; }

    /// <summary>The instant <see cref="UncollectedCash"/> was accurate for.</summary>
    /// <remarks>
    /// Advanced only by the time a payout actually consumed, so a part-earned coin is not lost to a
    /// frequent reader. See <see cref="PropertyIncome"/>.
    /// </remarks>
    public DateTimeOffset AccruedAtUtc { get; private set; }

    /// <summary>Whether the player owns at least one unit.</summary>
    public bool IsOwned => Quantity > 0;

    /// <summary>
    /// What collecting would pay right now, without collecting it.
    /// </summary>
    /// <param name="definition">This property's definition from content.</param>
    /// <param name="nowUtc">The instant to evaluate at.</param>
    public long PendingAt(Property definition, DateTimeOffset nowUtc)
    {
        EnsureMatches(definition);

        return PropertyIncome.Accrue(definition, Quantity, UncollectedCash, AccruedAtUtc, nowUtc)
            .Uncollected;
    }

    /// <summary>The most this holding can bank before income stops, given what the player owns.</summary>
    public long CapAt(Property definition)
    {
        EnsureMatches(definition);

        return PropertyIncome.CapFor(definition, Quantity);
    }

    /// <summary>
    /// Adds <paramref name="count"/> units, banking everything the existing units earned at the old rate
    /// first.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is not positive.</exception>
    public void AddUnits(Property definition, int count, DateTimeOffset nowUtc)
    {
        EnsureMatches(definition);

        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Units added must be >= 1.");

        // Settle before the rate changes: the time already elapsed was earned by the units owned then,
        // not by the ones being bought now.
        Settle(definition, nowUtc);

        Quantity += count;
    }

    /// <summary>
    /// Collects everything pending, emptying the holding and leaving it earning again from this instant.
    /// </summary>
    /// <remarks>
    /// Returns the cash rather than crediting the player: the domain does not reach into
    /// <see cref="Player"/> from here, so the caller applies the payout in the same transaction that
    /// persists the emptied holding.
    /// </remarks>
    /// <returns>Cash collected. Zero when nothing had accrued — which is a no-op, not an error.</returns>
    public long CollectAt(Property definition, DateTimeOffset nowUtc)
    {
        EnsureMatches(definition);

        Settle(definition, nowUtc);

        long collected = UncollectedCash;
        UncollectedCash = 0;

        return collected;
    }

    /// <summary>Banks everything earned up to <paramref name="nowUtc"/>.</summary>
    private void Settle(Property definition, DateTimeOffset nowUtc)
    {
        var accrual = PropertyIncome.Accrue(definition, Quantity, UncollectedCash, AccruedAtUtc, nowUtc);

        UncollectedCash = accrual.Uncollected;
        AccruedAtUtc = accrual.AccruedAtUtc;
    }

    /// <summary>
    /// Refuses a definition belonging to some other property.
    /// </summary>
    /// <remarks>
    /// Accruing against the wrong definition pays the wrong rate and never throws on its own — it just
    /// quietly produces the wrong balance. Ordinal comparison, because content ids are keys rather than
    /// display text.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the definition is for a different property.</exception>
    private void EnsureMatches(Property definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!string.Equals(definition.Id, PropertyId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Definition '{definition.Id}' does not match holding '{PropertyId}'.", nameof(definition));
    }
}
