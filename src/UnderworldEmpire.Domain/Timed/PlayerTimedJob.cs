namespace UnderworldEmpire.Domain.Timed;

/// <summary>
/// One player's single running appointment: a <c>(TimedJobId, StartedAtUtc)</c> snapshot.
/// Readiness and remaining time are computed on read, exactly as <see cref="Meter.CurrentAt"/>
/// computes a meter's current value — there is no stored "ready" flag, because a flag would need
/// something to flip it, and this project never runs background work.
/// </summary>
/// <remarks>
/// Its own EF entity, unlike <see cref="Meter"/> (a value object embedded via
/// <c>ComplexProperty</c>): Tier 1 allows at most one slot, so one row per player, keyed on
/// <see cref="PlayerId"/> alone — no composite beyond that, unlike <c>PlayerJobMastery</c>'s
/// per-job rows.
/// </remarks>
public class PlayerTimedJob
{
    /// <summary>The owning player. Primary key — at most one row per player at Tier 1.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>Content id of the running appointment.</summary>
    public string TimedJobId { get; set; } = string.Empty;

    /// <summary>When the appointment was started. Readiness and remaining time are derived from this.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Whether the appointment is ready to collect at <paramref name="now"/>.</summary>
    /// <param name="now">The wall-clock instant to evaluate at.</param>
    /// <param name="duration">The appointment's content-supplied duration.</param>
    public bool IsReadyAt(DateTimeOffset now, TimeSpan duration) => now >= StartedAtUtc + duration;

    /// <summary>
    /// Seconds remaining until the appointment is ready at <paramref name="now"/>, floored at
    /// zero once it is ready.
    /// </summary>
    /// <param name="now">The wall-clock instant to evaluate at.</param>
    /// <param name="duration">The appointment's content-supplied duration.</param>
    public int RemainingAt(DateTimeOffset now, TimeSpan duration)
    {
        var remaining = (StartedAtUtc + duration) - now;
        return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
    }
}
