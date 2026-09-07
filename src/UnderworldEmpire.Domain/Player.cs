using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Domain;

/// <summary>
/// A game account. Deliberately minimal at scaffold time (S0.2) — just enough to
/// produce the first EF migration. Extended in M1 (S1.1) with resource meters
/// (energy / stamina / health); device-token identity, currencies, and level/XP/stats
/// follow in later sessions.
/// </summary>
public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// The guest bearer credential. Always server-generated via
    /// <c>DeviceTokenGenerator</c>; clients never supply their own token.
    /// </summary>
    public string DeviceToken { get; set; } = string.Empty;

    /// <summary>
    /// The account username, lower-cased, if this player has claimed an account. Null for a
    /// guest player who has never registered — the guest bootstrap flow is unaffected by this
    /// being unset.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// PBKDF2 password hash (see <c>PasswordHasher</c> in Infrastructure), set together with
    /// <see cref="Username"/> when an account is claimed. Null exactly when <see cref="Username"/>
    /// is null.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Action-gated resource; regenerates at <see cref="MeterRates.Energy"/>.</summary>
    public Meter Energy { get; set; } =
        Meter.Full(PlayerDefaults.EnergyMax, DateTimeOffset.UtcNow);

    /// <summary>Combat-gated resource; regenerates at <see cref="MeterRates.Stamina"/>.</summary>
    public Meter Stamina { get; set; } =
        Meter.Full(PlayerDefaults.StaminaMax, DateTimeOffset.UtcNow);

    /// <summary>Vitality resource; regenerates at <see cref="MeterRates.Health"/>.</summary>
    public Meter Health { get; set; } =
        Meter.Full(PlayerDefaults.HealthMax, DateTimeOffset.UtcNow);

    // -------------------------------------------------------------------------
    // Progression — leveling/allocation logic is M2; only scalars live here.
    // -------------------------------------------------------------------------

    /// <summary>Current level. Leveling logic is M2. Tunable balance value.</summary>
    public int Level { get; set; } = PlayerDefaults.StartingLevel;

    /// <summary>Accumulated experience points. Leveling logic is M2.</summary>
    public long Xp { get; set; } = PlayerDefaults.StartingXp;

    /// <summary>Unspent skill points available for allocation (M2+).</summary>
    public int SkillPoints { get; set; } = PlayerDefaults.StartingSkillPoints;

    /// <summary>Base attack stat. Allocation logic is M2. Tunable balance value.</summary>
    public int Attack { get; set; } = PlayerDefaults.BaseAttack;

    /// <summary>Base defense stat. Allocation logic is M2. Tunable balance value.</summary>
    public int Defense { get; set; } = PlayerDefaults.BaseDefense;

    /// <summary>
    /// Adds <paramref name="amount"/> to <see cref="Xp"/> without applying the level curve.
    /// </summary>
    /// <remarks>
    /// The raw accumulator. Callers that want levelling behaviour use
    /// <see cref="ApplyXpAndLevelUp"/>; this exists so XP can be granted in contexts that must not
    /// trigger a level-up as a side effect.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void AddXp(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        Xp += amount;
    }

    /// <summary>
    /// Adds <paramref name="amount"/> to <see cref="Xp"/> and applies the level curve, refilling
    /// meters and granting skill points for every level crossed.
    /// </summary>
    /// <remarks>
    /// Resolves <b>all</b> levels gained in one pass rather than looping one level at a time: a
    /// single large payout can cross several thresholds, and a per-level loop would grant the right
    /// points but reset the meter clock repeatedly for no reason.
    /// </remarks>
    /// <param name="amount">XP to grant; must be non-negative.</param>
    /// <param name="nowUtc">Instant used as the refill timestamp when a level is gained.</param>
    /// <returns>What the grant produced; <see cref="LevelUpOutcome.LeveledUp"/> is false if no threshold was crossed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public LevelUpOutcome ApplyXpAndLevelUp(long amount, DateTimeOffset nowUtc)
    {
        AddXp(amount);

        int levelBefore = Level;
        int levelAfter = LevelCurve.LevelForXp(Xp);

        // Guard against a level that is somehow already ahead of the curve (hand-edited data, or a
        // curve retune that lowered thresholds): never demote a player.
        if (levelAfter <= levelBefore)
            return LevelUpOutcome.None(levelBefore);

        int levelsGained = levelAfter - levelBefore;
        int skillPointsGranted = levelsGained * PlayerDefaults.SkillPointsPerLevel;

        Level = levelAfter;
        GrantSkillPoints(skillPointsGranted);

        // Levelling refills every meter. This is what lets a player keep going after a grind
        // instead of stopping to wait, and it is the visible reward CP2 asks the tester to confirm.
        Energy = Energy.RefillAt(nowUtc);
        Stamina = Stamina.RefillAt(nowUtc);
        Health = Health.RefillAt(nowUtc);

        return new LevelUpOutcome(levelsGained, levelAfter, skillPointsGranted, MetersRefilled: true);
    }

    /// <summary>Grants <paramref name="amount"/> unspent skill points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void GrantSkillPoints(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        SkillPoints += amount;
    }

    /// <summary>
    /// Spends one skill point on <paramref name="target"/>, raising that stat by
    /// <see cref="PlayerDefaults.StatIncreasePerSkillPoint"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no skill points remain.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="target"/> is not a known stat.</exception>
    public void SpendSkillPoint(SkillTarget target)
    {
        if (SkillPoints < 1)
            throw new InvalidOperationException("no skill points available");

        // Decrement only after the target is known good, so an invalid target cannot burn a point.
        switch (target)
        {
            case SkillTarget.Attack:
                Attack += PlayerDefaults.StatIncreasePerSkillPoint;
                break;
            case SkillTarget.Defense:
                Defense += PlayerDefaults.StatIncreasePerSkillPoint;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown skill target.");
        }

        SkillPoints--;
    }

    /// <summary>Soft in-game currency. Display label supplied by the content pack.</summary>
    public long Cash { get; set; } = PlayerDefaults.StartingCash;

    /// <summary>Hard premium currency. Display label supplied by the content pack.</summary>
    public long PremiumCurrency { get; set; } = PlayerDefaults.StartingPremiumCurrency;

    /// <summary>Increases <see cref="Cash"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void AddCash(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        Cash += amount;
    }

    /// <summary>Decreases <see cref="Cash"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="amount"/> exceeds <see cref="Cash"/>.</exception>
    public void SpendCash(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        if (amount > Cash)
            throw new InvalidOperationException("insufficient cash");
        Cash -= amount;
    }

    /// <summary>Increases <see cref="PremiumCurrency"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    public void AddPremiumCurrency(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        PremiumCurrency += amount;
    }

    /// <summary>Decreases <see cref="PremiumCurrency"/> by <paramref name="amount"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="amount"/> exceeds <see cref="PremiumCurrency"/>.</exception>
    public void SpendPremiumCurrency(long amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be non-negative.");
        if (amount > PremiumCurrency)
            throw new InvalidOperationException("insufficient premium currency");
        PremiumCurrency -= amount;
    }
}
