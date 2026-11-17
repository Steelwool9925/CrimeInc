namespace UnderworldEmpire.Domain.Daily;

/// <summary>
/// How far one player has carried their daily login streak. Persisted per player — the whole
/// primary key is <see cref="PlayerId"/>, one row, created lazily on the player's first-ever
/// claim. Same shape as <see cref="UnderworldEmpire.Domain.Jobs.PlayerJobMastery"/>: plain
/// properties, pure mutation, no EF dependency.
/// </summary>
public class DailyStreak
{
    /// <summary>Owning player. The whole primary key — one row per player.</summary>
    public Guid PlayerId { get; init; }

    /// <summary>
    /// The UTC day the streak was last claimed. Default (<c>0001-01-01</c>) on a row that has
    /// never been claimed, which is never "yesterday" relative to any real clock — so the first
    /// ever claim always starts the streak at 1 rather than extending a phantom one.
    /// </summary>
    public DateOnly LastClaimedUtcDate { get; private set; }

    /// <summary>Consecutive UTC days claimed up to and including <see cref="LastClaimedUtcDate"/>.</summary>
    public int CurrentStreak { get; private set; }

    /// <summary>The highest <see cref="CurrentStreak"/> this player has ever reached.</summary>
    public int LongestStreak { get; private set; }

    /// <summary>Whether a claim at <paramref name="nowUtc"/> would be accepted.</summary>
    public bool IsClaimableAt(DateTimeOffset nowUtc) => UtcDay.Of(nowUtc) != LastClaimedUtcDate;

    /// <summary>
    /// What <see cref="CurrentStreak"/> would become if claimed at <paramref name="nowUtc"/>,
    /// without mutating anything. Shared by <see cref="Claim"/> and by a read-only status view
    /// that wants to preview the next reward without claiming it.
    /// </summary>
    public int StreakIfClaimedAt(DateTimeOffset nowUtc)
    {
        var today = UtcDay.Of(nowUtc);
        if (LastClaimedUtcDate == today)
            return CurrentStreak;

        bool consecutive = LastClaimedUtcDate == today.AddDays(-1);
        return consecutive ? CurrentStreak + 1 : 1;
    }

    /// <summary>
    /// Takes today's claim: refuses if already claimed today, otherwise extends the streak when
    /// yesterday was claimed too, or resets it to 1 after any gap.
    /// </summary>
    public DailyFailure Claim(DateTimeOffset nowUtc)
    {
        if (!IsClaimableAt(nowUtc))
            return DailyFailure.AlreadyClaimedToday;

        CurrentStreak = StreakIfClaimedAt(nowUtc);
        LongestStreak = Math.Max(LongestStreak, CurrentStreak);
        LastClaimedUtcDate = UtcDay.Of(nowUtc);

        return DailyFailure.None;
    }
}
