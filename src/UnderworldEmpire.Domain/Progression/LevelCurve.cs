namespace UnderworldEmpire.Domain.Progression;

/// <summary>
/// The experience curve: how much cumulative XP each level requires, and which level a given XP
/// total earns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is code and not content.</b> Every other balance value in this game lives in the
/// content pack, per <c>content/crime/README.md</c>. The curve is the deliberate exception: it is a
/// formula rather than theme-facing text or art, and expressing it as data would mean either a
/// table of thresholds per level (which cannot be extended without an edit) or an expression
/// language in JSON. The trade-off is that retuning the curve is a code change. Flagged in the
/// feature plan for review.
/// </para>
/// <para>
/// <b>The formula.</b> Cumulative XP to reach level <c>L</c> is
/// <c>XpPerLevelStep × (L − 1) × L</c>, so thresholds run 0, 100, 300, 600, 1000, 1500 … and the
/// gap between consecutive levels grows linearly (100, 200, 300 …). Level 2 landing at exactly 100
/// is load-bearing: the crime pack is tuned so ten runs of its cheapest job — one full starting
/// energy bar — award exactly 100 XP, which is what makes the CP2 checkpoint reachable without
/// waiting on energy regeneration.
/// </para>
/// </remarks>
public static class LevelCurve
{
    /// <summary>Scaling constant for the quadratic curve. See the formula in the type remarks.</summary>
    public const long XpPerLevelStep = 50;

    /// <summary>
    /// Cumulative XP required to <b>be</b> at <paramref name="level"/>. Level 1 requires 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is below 1.</exception>
    public static long XpToReach(int level)
    {
        if (level < 1)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be >= 1.");

        // Widen before multiplying: at the max level this product comfortably exceeds int range.
        long l = level;
        return XpPerLevelStep * (l - 1) * l;
    }

    /// <summary>
    /// XP still needed to reach the next level from <paramref name="xp"/>, or
    /// <see langword="null"/> at <see cref="PlayerDefaults.MaxLevel"/>.
    /// </summary>
    public static long? XpUntilNextLevel(long xp)
    {
        int level = LevelForXp(xp);
        if (level >= PlayerDefaults.MaxLevel)
            return null;

        return XpToReach(level + 1) - xp;
    }

    /// <summary>
    /// The level a cumulative <paramref name="xp"/> total earns, clamped to
    /// <see cref="PlayerDefaults.MaxLevel"/>.
    /// </summary>
    /// <remarks>
    /// Solves the quadratic for a starting estimate, then corrects by stepping. The correction is
    /// not defensive padding: <see cref="Math.Sqrt(double)"/> on a large <see cref="long"/> can land
    /// a whole level either side of the boundary once the value exceeds 2^53, and an off-by-one here
    /// would grant or withhold a real level.
    /// </remarks>
    public static int LevelForXp(long xp)
    {
        if (xp <= 0)
            return 1;

        // 50L² - 50L - xp = 0  →  L = (1 + sqrt(1 + 4xp/50)) / 2
        double estimate = (1 + Math.Sqrt(1 + 4.0 * xp / XpPerLevelStep)) / 2;
        int level = Math.Clamp((int)estimate, 1, PlayerDefaults.MaxLevel);

        // Walk down while the estimate overshoots the player's XP.
        while (level > 1 && XpToReach(level) > xp)
            level--;

        // Walk up while the next level is already affordable.
        while (level < PlayerDefaults.MaxLevel && XpToReach(level + 1) <= xp)
            level++;

        return level;
    }
}
