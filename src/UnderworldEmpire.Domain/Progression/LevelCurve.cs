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
/// was originally chosen against the crime pack's cheapest job, back when jobs could not fail: ten
/// runs of it were one full starting energy bar, and at 10 XP each that was exactly the level-2
/// threshold.
/// </para>
/// <para>
/// <b>M3R broke that premise, and this remark then described the breakage wrongly for some time.</b>
/// Once jobs could fail, a failure granted only the pack's consolation fraction of the XP. An
/// earlier version of this text put the resulting figure at "roughly 91 XP" and said level 2
/// "usually takes an eleventh run". Both were wrong, and not by a little: simulating the real rules
/// over 400 seeded first sessions measured <b>68.6 XP</b> on a full bar, around 15 runs to reach
/// 100, and — because the bar ran dry first — only <b>1%</b> of new players reaching level 2 in
/// their first session at all. The same paragraph also claimed the pack's
/// <c>completionsPerRank</c> had been dropped "from 10 to 8"; the shipped value is <b>6</b>.
/// </para>
/// <para>
/// <b>The premise was restored by moving the other two numbers, not the curve.</b>
/// <see cref="PlayerDefaults.EnergyMax"/> is now 20 and the starter job's base success chance is
/// 0.80, which puts a full opening bar at 20 &#215; (0.80 &#215; 10 + 0.20 &#215; 1) = <b>164 XP</b> —
/// clearing the 100 threshold around the thirteenth tap, with headroom. So level 2 is once again
/// reachable in one sitting and <see cref="XpPerLevelStep"/> did not have to move.
/// </para>
/// <para>
/// <b>The cap moved; the curve did not.</b> <see cref="PlayerDefaults.MaxLevel"/> was cut from 100 to
/// 20 so it sits just past the content (the last unlock is level 12), which is what turned the
/// audit's "98.4% of the curve unlocks nothing" into roughly 59%. The formula is untouched, so level
/// 20 costs <c>XpPerLevelStep × 19 × 20</c> = 19,000 XP cumulatively and the last step, 19 to 20, is
/// 1,900. Whether the quadratic's constant should itself be re-derived against the real earn rate is
/// a separate, measurement-dependent question deliberately left for later: the curve is pinned in two
/// places by <c>ProgressionTests</c>, so changing it is test-visible by design. See
/// <c>content/crime/README.md</c>.
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

        // Widen before multiplying so a large level cannot overflow int. (At today's cap of 20 the
        // product is small, but the cap is a tunable and this stays correct if it is ever raised.)
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
