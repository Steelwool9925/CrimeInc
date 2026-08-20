namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// A holding's income position at an instant: what it has banked, and the point in time that figure was
/// accurate for.
/// </summary>
/// <param name="Uncollected">Cash earned but not yet collected.</param>
/// <param name="AccruedAtUtc">
///     The instant <paramref name="Uncollected"/> is accurate for. Not necessarily "now": time that has
///     elapsed but not yet produced a whole coin is left on the clock rather than banked or discarded.
/// </param>
public readonly record struct IncomeAccrual(long Uncollected, DateTimeOffset AccruedAtUtc);

/// <summary>
/// The lazy income-accrual maths for properties: a pure function of (definition, units, banked cash,
/// last-settled instant, now). No clock, no storage, no side effects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lazy, like the meters.</b> Nothing runs on a timer. Income is derived from elapsed time whenever
/// somebody asks, exactly as <see cref="Meter"/> derives regen — a server with a million idle properties
/// does no work for any of them.
/// </para>
/// <para>
/// <b>Why the clock moves by less than the elapsed time.</b> Cash is whole numbers, so a settle usually
/// leaves a fraction of a coin unearned. Resetting the clock to <c>now</c> would throw that fraction
/// away, which means a client polling every second would earn strictly less than one polling once an
/// hour — and a player tapping <i>collect</i> repeatedly could earn nothing at all. Instead the clock
/// advances only by the time the payout actually consumed, so the remainder survives into the next
/// settle and frequent reads are exactly as profitable as rare ones.
/// </para>
/// <para>
/// <b>Why the cap discards time.</b> Once the ceiling is reached the clock <i>does</i> jump to
/// <c>now</c>. That is the point of a cap: the hours a full property spent waiting are worth nothing,
/// and banking them as a head start on the next window would quietly undo the reason to come back.
/// </para>
/// </remarks>
public static class PropertyIncome
{
    /// <summary>
    /// The most uncollected cash <paramref name="quantity"/> units of <paramref name="definition"/> can
    /// hold.
    /// </summary>
    /// <remarks>
    /// Rate and ceiling scale together, so buying a second unit doubles the takings rather than filling
    /// the same ceiling twice as fast — owning more never shortens the time you may stay away.
    /// </remarks>
    public static long CapFor(Property definition, int quantity)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return quantity < 1 ? 0L : definition.CapPerUnit * quantity;
    }

    /// <summary>
    /// Settles a holding forward to <paramref name="nowUtc"/>, returning what it has banked and the
    /// instant that figure is accurate for.
    /// </summary>
    /// <param name="definition">The property definition from content, supplying rate and window.</param>
    /// <param name="quantity">Units owned. Zero earns nothing.</param>
    /// <param name="uncollected">Cash already banked at <paramref name="accruedAtUtc"/>.</param>
    /// <param name="accruedAtUtc">The instant <paramref name="uncollected"/> was accurate for.</param>
    /// <param name="nowUtc">The instant to settle to.</param>
    public static IncomeAccrual Accrue(
        Property definition,
        int quantity,
        long uncollected,
        DateTimeOffset accruedAtUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);

        long cap = CapFor(definition, quantity);

        // Nothing owned: no rate and no ceiling. The clock still moves to now — an unbought property has
        // no elapsed time worth remembering, and leaving it at an ancient default would hand the first
        // purchase a fully-loaded property.
        if (quantity < 1)
            return new IncomeAccrual(Math.Min(uncollected, cap), nowUtc);

        // Already full. The waiting is worth nothing, so the clock restarts here rather than carrying a
        // head start into the next window.
        if (uncollected >= cap)
            return new IncomeAccrual(cap, nowUtc);

        long elapsedTicks = (nowUtc - accruedAtUtc).Ticks;

        // A clock that reads backwards (an NTP correction, a host with a skewed time) must not take
        // banked cash away, and must not move the settle point backwards either.
        if (elapsedTicks <= 0)
            return new IncomeAccrual(uncollected, accruedAtUtc);

        // Past a full window the answer is the cap whatever the exact elapsed time, and short-circuiting
        // here also keeps the arithmetic below away from absurd inputs.
        long windowTicks = definition.AccrualCapMinutes * TimeSpan.TicksPerMinute;
        if (elapsedTicks >= windowTicks)
            return new IncomeAccrual(cap, nowUtc);

        // Int128 for the intermediates: elapsed ticks times a per-hour rate times the units owned
        // overflows a long for a large enough holding, and a wrapped multiply would surface as a
        // negative balance rather than as an error.
        Int128 ratePerHour = (Int128)definition.IncomePerHour * quantity;
        long earned = (long)((Int128)elapsedTicks * ratePerHour / TimeSpan.TicksPerHour);

        long room = cap - uncollected;
        if (earned >= room)
            return new IncomeAccrual(cap, nowUtc);

        // The time those whole coins actually cost. Floored, so any rounding leaves the player holding
        // slightly more unspent time rather than slightly less — never the other way round.
        long consumedTicks = (long)((Int128)earned * TimeSpan.TicksPerHour / ratePerHour);

        return new IncomeAccrual(uncollected + earned, accruedAtUtc.AddTicks(consumedTicks));
    }
}
