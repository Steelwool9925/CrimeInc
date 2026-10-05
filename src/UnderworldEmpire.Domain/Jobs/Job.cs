namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// One item-quantity gate on a job: the player must hold at least <see cref="MinQuantity"/> copies
/// of <see cref="ItemId"/> to attempt it, and every attempt spends <see cref="QuantityConsumed"/> of
/// them — win or lose (see <see cref="JobOutcome.ItemsConsumed"/>).
/// </summary>
/// <remarks>
/// Sits alongside <see cref="Job.MeetsStatRequirements"/>, not instead of it: a job can gate on
/// stats, items, both, or neither, and the two gates are answered independently.
/// <para>
/// <b>Holding and spending are separate numbers on purpose.</b> This used to be a constant 1 inside
/// <c>JobService</c>, so a requirement of 2 meant "hold two, spend one" by an accident of that
/// constant, and a job that wanted to spend its whole requirement could not say so. The spend can
/// never exceed the hold: the gate only guarantees <see cref="MinQuantity"/> copies, so spending
/// more would overdraw the stack.
/// </para>
/// </remarks>
/// <param name="ItemId">Content id of the required weapon/armor item.</param>
/// <param name="MinQuantity">Minimum copies held to attempt the job. Always &gt;= 1.</param>
/// <param name="QuantityConsumed">
/// Copies spent per attempt. Defaults to 1, which is what every job did before this existed. Always
/// between 1 and <paramref name="MinQuantity"/>.
/// </param>
public readonly record struct ItemRequirement(string ItemId, int MinQuantity, int QuantityConsumed = 1);

/// <summary>
/// A single job definition as the domain sees it, projected from the content pack's
/// <c>jobs.json</c>. Immutable: the catalog is loaded once at startup and never mutated.
/// </summary>
/// <remarks>
/// Every number here is a tunable balance value supplied by content, never hard-coded in the
/// engine — see <c>content/crime/README.md</c>. The loader validates raw content and collects all
/// errors before constructing any <see cref="Job"/>, so the guards below are a safety net for
/// programmatic construction (tests, future generators) rather than the primary validation path.
/// </remarks>
public sealed record Job
{
    /// <summary>Stable content id, unique within the pack. Used as the route value and the mastery key.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing job name, supplied by content.</summary>
    public required string Name { get; init; }

    /// <summary>Player-facing flavour text, supplied by content.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Grouping key for the UI (for example <c>downtown</c>). Purely presentational.</summary>
    public string Area { get; init; } = string.Empty;

    /// <summary>Energy consumed per attempt; always &gt;= 1 so a job can never be free.</summary>
    public required int EnergyCost { get; init; }

    /// <summary>Minimum player level permitted to attempt this job; always &gt;= 1.</summary>
    public required int LevelRequirement { get; init; }

    /// <summary>Lowest cash payout, inclusive.</summary>
    public required long CashMin { get; init; }

    /// <summary>Highest cash payout, inclusive.</summary>
    public required long CashMax { get; init; }

    /// <summary>Experience granted per successful attempt. Fixed, not rolled.</summary>
    public required long Xp { get; init; }

    /// <summary>
    /// Minimum <b>effective</b> attack required to attempt this job — the player's own stat plus
    /// everything they have equipped. Zero means ungated.
    /// </summary>
    /// <remarks>
    /// Effective rather than base so equipping a weapon opens jobs up, which gives items a purpose
    /// before M4 combat exists and reuses the T3.1 <c>Loadout</c> aggregation rather than inventing a
    /// second notion of "your attack".
    /// </remarks>
    public int AttackRequirement { get; init; }

    /// <summary>Minimum effective defense required to attempt this job. Zero means ungated.</summary>
    public int DefenseRequirement { get; init; }

    /// <summary>
    /// The success chance for a player exactly at this job's stat requirements, in <c>[0, 1]</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>1.0</c>: a <see cref="Job"/> built in code — by a test, or a future generator —
    /// must never wander into the failure path because a field was left unset. Content is where the
    /// strictness lives; the loader treats a missing <c>risk</c> block as a named error rather than
    /// letting it read as zeros.
    /// </remarks>
    public double BaseSuccessChance { get; init; } = 1.0;

    /// <summary>How much one point of surplus stat adds to the success chance.</summary>
    public double SuccessChancePerSurplusPoint { get; init; }

    /// <summary>
    /// Item-quantity gates this job additionally requires, alongside the stat gate. Empty — the
    /// default — means ungated: every job that does not opt in behaves exactly as it did before this
    /// field existed.
    /// </summary>
    public IReadOnlyList<ItemRequirement> ItemRequirements { get; init; } = [];

    /// <summary>
    /// How much one point of surplus held-item quantity (above a requirement's minimum) adds to the
    /// success chance. Mirrors <see cref="SuccessChancePerSurplusPoint"/> as its own rate rather than
    /// being blended into it, so a large item surplus cannot paper over a stats shortfall or vice
    /// versa.
    /// </summary>
    public double ItemSuccessChancePerSurplusPoint { get; init; }

    /// <summary>Lowest health lost on a failed attempt, inclusive.</summary>
    public int HealthLossMin { get; init; }

    /// <summary>Highest health lost on a failed attempt, inclusive.</summary>
    public int HealthLossMax { get; init; }

    /// <summary>
    /// Whether <paramref name="effectiveAttack"/> and <paramref name="effectiveDefense"/> clear this
    /// job's stat minimums.
    /// </summary>
    /// <remarks>
    /// A gate, not a penalty: below it the attempt is refused and nothing is spent, exactly like the
    /// level gate. The comparison is <c>&gt;=</c>, so content saying "attack 5" admits a player with
    /// attack 5.
    /// </remarks>
    public bool MeetsStatRequirements(int effectiveAttack, int effectiveDefense) =>
        effectiveAttack >= AttackRequirement && effectiveDefense >= DefenseRequirement;

    /// <summary>
    /// Whether <paramref name="held"/> — the player's current item quantities, keyed by item id —
    /// clears every one of this job's <see cref="ItemRequirements"/>.
    /// </summary>
    /// <remarks>
    /// A gate, not a penalty: below it the attempt is refused and nothing is spent, exactly like
    /// <see cref="MeetsStatRequirements"/>. A job with no <see cref="ItemRequirements"/> admits
    /// anyone — <c>null</c> is treated the same as an empty holding, so a caller with nothing to
    /// report need not construct a dictionary just to ask.
    /// </remarks>
    public bool MeetsItemRequirements(IReadOnlyDictionary<string, int>? held)
    {
        held ??= EmptyHeldItems;
        return ItemRequirements.All(r => held.GetValueOrDefault(r.ItemId) >= r.MinQuantity);
    }

    /// <summary>
    /// Shared empty holding, so callers with nothing to report need not allocate one. Internal
    /// (not private) so <see cref="JobService"/> can reuse the same instance rather than keeping its
    /// own copy of a constant the two classes would otherwise need to keep in sync by hand.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, int> EmptyHeldItems = new Dictionary<string, int>();

    /// <summary>
    /// This job's success chance for a player with the given effective stats, under the pack-wide
    /// ceiling <paramref name="maxSuccessChance"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>chance = base + perPoint × surplus</c>, clamped to
    /// <c>[base, min(1, maxSuccessChance)]</c>. Each stat's surplus floors at zero
    /// <b>independently</b> — summed algebraically, a large attack overshoot would cancel a defense
    /// shortfall and the defense requirement would stop meaning anything.
    /// </para>
    /// <para>
    /// One implementation serves both the read path and the roll: <c>GET /jobs</c> reports the figure
    /// this returns and the attempt rolls against it, so the odds a player is shown cannot drift from
    /// the odds they get.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>The ceiling always wins</b>, including over this job's own floor. The two can legitimately
    /// disagree: the domain default is <c>1.0</c> so a hand-built job never fails by accident, and a
    /// realistic pack caps at <c>0.95</c> — a job built in a test and evaluated under real pack rules
    /// meets exactly that. Throwing there would turn the benign default into an exception at roll
    /// time. Content that genuinely disagrees is caught far earlier and far louder: the loader
    /// rejects a <c>baseSuccessChance</c> above <c>risk.maxSuccessChance</c> at startup, naming the
    /// job.
    /// </para>
    /// <para>
    /// The optional <paramref name="heldItemQuantities"/> feeds a second, independent surplus term
    /// at <see cref="ItemSuccessChancePerSurplusPoint"/> per point — added alongside the stat term,
    /// not blended into it, for the same reason stat surplus floors per stat rather than summing
    /// algebraically: a large item surplus must not paper over a stats shortfall or vice versa.
    /// Omitted (or a job with no <see cref="ItemRequirements"/>), the term is simply zero, so every
    /// existing call site and every job that does not opt in computes exactly what it always has.
    /// </para>
    /// </remarks>
    public double SuccessChanceFor(
        int effectiveAttack,
        int effectiveDefense,
        double maxSuccessChance,
        IReadOnlyDictionary<string, int>? heldItemQuantities = null)
    {
        double ceiling = Math.Min(1.0, maxSuccessChance);

        int statSurplus = Math.Max(0, effectiveAttack - AttackRequirement)
                        + Math.Max(0, effectiveDefense - DefenseRequirement);

        var held = heldItemQuantities ?? EmptyHeldItems;
        int itemSurplus = ItemRequirements.Sum(
            r => Math.Max(0, held.GetValueOrDefault(r.ItemId) - r.MinQuantity));

        double chance = BaseSuccessChance
            + SuccessChancePerSurplusPoint * statSurplus
            + ItemSuccessChancePerSurplusPoint * itemSurplus;

        // The floor is the job's base, but never above the ceiling — otherwise Math.Clamp is handed
        // an inverted range and throws.
        return Math.Clamp(chance, Math.Min(BaseSuccessChance, ceiling), ceiling);
    }

    /// <summary>
    /// Validates the invariants the engine relies on. Called by the content loader after its own
    /// error-collecting pass, and directly by tests that build a <see cref="Job"/> by hand.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="Id"/> or <see cref="Name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric invariant is violated.</exception>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Job id must not be blank.", nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"Job '{Id}' has a blank name.", nameof(Name));
        if (EnergyCost < 1)
            throw new ArgumentOutOfRangeException(nameof(EnergyCost), EnergyCost,
                $"Job '{Id}' must cost at least 1 energy.");
        if (LevelRequirement < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelRequirement), LevelRequirement,
                $"Job '{Id}' must require at least level 1.");
        if (CashMin < 0)
            throw new ArgumentOutOfRangeException(nameof(CashMin), CashMin,
                $"Job '{Id}' cannot pay negative cash.");
        if (CashMax < CashMin)
            throw new ArgumentOutOfRangeException(nameof(CashMax), CashMax,
                $"Job '{Id}' has cashMax below cashMin ({CashMin}).");
        if (Xp < 0)
            throw new ArgumentOutOfRangeException(nameof(Xp), Xp,
                $"Job '{Id}' cannot grant negative XP.");
        if (AttackRequirement < 0)
            throw new ArgumentOutOfRangeException(nameof(AttackRequirement), AttackRequirement,
                $"Job '{Id}' cannot require negative attack.");
        if (DefenseRequirement < 0)
            throw new ArgumentOutOfRangeException(nameof(DefenseRequirement), DefenseRequirement,
                $"Job '{Id}' cannot require negative defense.");
        if (BaseSuccessChance is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(BaseSuccessChance), BaseSuccessChance,
                $"Job '{Id}' must have a base success chance between 0 and 1.");
        if (SuccessChancePerSurplusPoint < 0.0)
            // A negative gain would make gear actively harmful, which no content should be able to say.
            throw new ArgumentOutOfRangeException(nameof(SuccessChancePerSurplusPoint),
                SuccessChancePerSurplusPoint,
                $"Job '{Id}' cannot lose success chance per surplus point.");
        if (HealthLossMin < 0)
            throw new ArgumentOutOfRangeException(nameof(HealthLossMin), HealthLossMin,
                $"Job '{Id}' cannot cost negative health.");
        if (HealthLossMax < HealthLossMin)
            throw new ArgumentOutOfRangeException(nameof(HealthLossMax), HealthLossMax,
                $"Job '{Id}' has healthLossMax below healthLossMin ({HealthLossMin}).");
        var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in ItemRequirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.ItemId))
                throw new ArgumentException(
                    $"Job '{Id}' has an item requirement with a blank item id.",
                    nameof(ItemRequirements));
            if (requirement.MinQuantity < 1)
                throw new ArgumentOutOfRangeException(nameof(ItemRequirements), requirement.MinQuantity,
                    $"Job '{Id}' has an item requirement for '{requirement.ItemId}' with a minimum " +
                    "quantity below 1.");
            if (requirement.QuantityConsumed < 1 || requirement.QuantityConsumed > requirement.MinQuantity)
                throw new ArgumentOutOfRangeException(nameof(ItemRequirements), requirement.QuantityConsumed,
                    $"Job '{Id}' has an item requirement for '{requirement.ItemId}' consuming " +
                    $"{requirement.QuantityConsumed} per attempt, which must be between 1 and its " +
                    $"minimum quantity ({requirement.MinQuantity}): the gate only guarantees that many.");
            // A duplicate would let MeetsItemRequirements evaluate the same held quantity against
            // the same minimum twice — harmless there, but JobService.Attempt would then build two
            // ItemConsumption entries for the one item, and the second Consume() at the
            // infrastructure layer would throw against a row already drained by the first.
            if (!seenItemIds.Add(requirement.ItemId))
                throw new ArgumentException(
                    $"Job '{Id}' has a duplicate item requirement for '{requirement.ItemId}'.",
                    nameof(ItemRequirements));
        }
        if (ItemSuccessChancePerSurplusPoint < 0.0)
            // Same reasoning as SuccessChancePerSurplusPoint: a negative gain would make surplus
            // stock actively harmful, which no content should be able to say.
            throw new ArgumentOutOfRangeException(nameof(ItemSuccessChancePerSurplusPoint),
                ItemSuccessChancePerSurplusPoint,
                $"Job '{Id}' cannot lose success chance per item surplus point.");
    }
}
