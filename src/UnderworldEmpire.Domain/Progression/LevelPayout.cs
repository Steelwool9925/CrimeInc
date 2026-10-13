namespace UnderworldEmpire.Domain.Progression;

/// <summary>
/// The pay bonus a player's level earns on every job — the second, varying grant that makes each
/// level mean something beyond its skill point.
/// </summary>
/// <remarks>
/// Pure and content-agnostic: the per-level rate comes from the pack, so the domain never loads
/// content. Level 1 earns nothing and each level after it adds one step, so reaching level 2 is the
/// first thing that changes a payout. The level is clamped to <see cref="PlayerDefaults.MaxLevel"/>
/// because lowering the cap demotes no one: an account made under the old cap may sit above it and
/// must not out-earn a player who has simply reached it.
/// </remarks>
public static class LevelPayout
{
    /// <summary>
    /// The extra share of cash and XP that <paramref name="level"/> pays: 0.08 is +8%.
    /// </summary>
    /// <param name="level">The player's level; anything below 1 earns nothing.</param>
    /// <param name="bonusPerLevel">Content-supplied bonus per level past the first. Must not be negative.</param>
    public static double BonusFor(int level, double bonusPerLevel)
    {
        if (bonusPerLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(bonusPerLevel), bonusPerLevel,
                "The level payout bonus must not be negative.");

        int effective = Math.Min(level, PlayerDefaults.MaxLevel);
        return effective <= 1 ? 0 : (effective - 1) * bonusPerLevel;
    }
}
