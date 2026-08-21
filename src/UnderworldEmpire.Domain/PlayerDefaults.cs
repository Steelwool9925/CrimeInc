namespace UnderworldEmpire.Domain;

/// <summary>
/// Default capacity constants for new <see cref="Player"/> instances.
/// New players start with every meter at full. Adjust these (and balance
/// via <see cref="MeterRates"/>) as game-design requirements evolve.
/// </summary>
public static class PlayerDefaults
{
    /// <summary>Default maximum Energy for a new player.</summary>
    public const int EnergyMax = 10;

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
