namespace UnderworldEmpire.Domain;

/// <summary>
/// Default capacity constants for new <see cref="Player"/> instances.
/// New players start with every meter at full. Adjust these (and balance
/// via <see cref="MeterRates"/>) as game-design requirements evolve.
/// </summary>
public static class PlayerDefaults
{
    /// <summary>Default maximum Energy for a new player. Tunable balance value.</summary>
    /// <remarks>
    /// <para>
    /// <b>The property this value exists to produce:</b> one full opening bar, spent on the
    /// cheapest level-1 job, must be enough to reach level 2. That is the first session's only
    /// reward moment — no level-up means no skill point, no meter refill and no job unlock, so
    /// the game's entire reward vocabulary fires zero times.
    /// </para>
    /// <para>
    /// At 10 this failed. Measured over 400 seeded runs per temperament, a new player banked 68.6
    /// of the 100 XP level 2 needs, and only <b>1%</b> reached it in their first session. At 20,
    /// paired with the starter job's 0.80 success chance, 100% do:
    /// 20 energy &#215; (0.80 &#215; 10 XP + 0.20 &#215; 1 consolation XP) = 164 XP, clearing the
    /// threshold around the thirteenth tap.
    /// </para>
    /// <para>
    /// <b>This is a buffer, not a rate.</b> <see cref="Meter.CurrentAt"/> computes
    /// <c>min(Max, stored + elapsed / regenPerPoint)</c>, so this constant bounds how much energy
    /// can bank between visits, and <see cref="MeterRates.Energy"/> alone sets how much is earned
    /// per hour. Raising it lengthens the opening session and widens the away-storage window from
    /// 15 to 30 minutes; it does <b>not</b> change steady-state throughput, which stays at 40/hr.
    /// Do not reach for this constant to change the earn rate.
    /// </para>
    /// <para>
    /// Pinned by <c>FirstSessionTests</c>. Note this is only the default for <i>new</i> players
    /// — <c>Meter.Max</c> is persisted per player as the <c>EnergyMax</c> column, so existing
    /// rows keep whatever they were created with until a migration rewrites them.
    /// </para>
    /// </remarks>
    public const int EnergyMax = 20;

    /// <summary>Default maximum Stamina for a new player.</summary>
    public const int StaminaMax = 5;

    /// <summary>Default maximum Health for a new player.</summary>
    public const int HealthMax = 100;

    /// <summary>
    /// Soft-currency balance awarded to every new player. Tunable balance value.
    /// </summary>
    /// <remarks>
    /// Zero: a new player owns nothing and must work the first job before they can buy anything.
    /// The cheapest item in the crime pack (Brass Knuckles, 75) is therefore several successful
    /// jobs away rather than affordable on the first tap.
    /// </remarks>
    public const long StartingCash = 0;

    /// <summary>Hard-currency balance awarded to every new player. Tunable balance value.</summary>
    public const long StartingPremiumCurrency = 5;

    // -------------------------------------------------------------------------
    // Progression — M2 owns leveling/allocation logic; only defaults live here.
    // -------------------------------------------------------------------------

    /// <summary>Level assigned to every new player. Tunable balance value.</summary>
    public const int StartingLevel = 1;

    /// <summary>XP assigned to every new player.</summary>
    public const long StartingXp = 0;

    /// <summary>Unspent skill points assigned to every new player.</summary>
    public const int StartingSkillPoints = 0;

    /// <summary>Base attack stat for a new player. Tunable balance value.</summary>
    /// <remarks>
    /// A new player starts at 1, not 5, so the stat gates in <c>jobs.json</c> have somewhere to
    /// climb from. Only the pack's first job is reachable at 1/1; everything else is gated on
    /// stats, levels, or both.
    /// </remarks>
    public const int BaseAttack = 1;

    /// <summary>Base defense stat for a new player. Tunable balance value. See <see cref="BaseAttack"/>.</summary>
    public const int BaseDefense = 1;

    /// <summary>
    /// Highest attainable level. Bounds the level-up loop so a pathological XP grant cannot spin,
    /// and gives <see cref="Progression.LevelCurve"/> a defined ceiling. Tunable balance value.
    /// </summary>
    public const int MaxLevel = 100;

    /// <summary>Skill points granted per level gained. Tunable balance value.</summary>
    public const int SkillPointsPerLevel = 1;

    /// <summary>
    /// Stat increase per skill point spent. Tunable balance value — CP2 checks that spending one
    /// point visibly moves the stat on Home, so this must stay &gt;= 1.
    /// </summary>
    public const int StatIncreasePerSkillPoint = 1;
}
