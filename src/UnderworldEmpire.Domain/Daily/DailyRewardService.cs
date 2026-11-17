namespace UnderworldEmpire.Domain.Daily;

/// <summary>
/// Pure rules for daily-login rewards. Holds no state and reads no content: the reward table is
/// passed in on every call, same idiom as <see cref="DailyObjectiveService"/>.
/// </summary>
public sealed class DailyRewardService
{
    /// <summary>
    /// The reward for <paramref name="streakDay"/> (1-based), clamped at the table's last entry
    /// for any day beyond what content defines — an escalating table caps rather than runs out.
    /// </summary>
    /// <param name="table">
    ///     The pack's reward table, ordered by day ascending and contiguous from 1 — guaranteed by
    ///     <c>ContentPackLoader.ValidateDailyRewards</c>, so a direct index lookup is safe here.
    /// </param>
    /// <param name="streakDay">The streak day to look up; values below 1 are clamped to 1.</param>
    public DailyReward RewardFor(IReadOnlyList<DailyReward> table, int streakDay)
    {
        if (table.Count == 0)
            throw new ArgumentException("Daily reward table must not be empty.", nameof(table));

        int index = Math.Clamp(streakDay, 1, table.Count) - 1;
        return table[index];
    }
}
