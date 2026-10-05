namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// One item gate on a job: the attempt needs <see cref="MinQuantity"/> copies of <see cref="ItemId"/>,
/// uses every one of them up, win or lose (see <see cref="JobOutcome.ItemsConsumed"/>), and lets the
/// player add up to <see cref="MaxExtras"/> more to improve the odds.
/// </summary>
/// <remarks>
/// Sits alongside <see cref="Job.MeetsStatRequirements"/>, not instead of it: a job can gate on
/// stats, items, both, or neither, and the two gates are answered independently.
/// <para>
/// <b>Two kinds of item, and the player chooses only one of them.</b> The required count is not a
/// choice: the job cannot be done without it. Extras are: each one the player commits is spent as
/// well and adds <see cref="Job.ExtraItemSuccessChance"/> to the chance of success. Nothing is
/// spent that the player did not commit or the job did not require, so a stack that merely sits in
/// the inventory never changes the odds. Counts are of <i>spare</i> copies: the one a player is
/// wearing is never spent (see <c>PlayerItem.SpareQuantity</c>).
/// </para>
/// </remarks>
/// <param name="ItemId">Content id of the required weapon/armor item.</param>
/// <param name="MinQuantity">Copies the job needs and uses up. Always &gt;= 1.</param>
/// <param name="MaxExtras">
/// The most extra copies a player may add. Zero (the default) means this item cannot be committed
/// beyond the requirement.
/// </param>
public readonly record struct ItemRequirement(string ItemId, int MinQuantity, int MaxExtras = 0);

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
    /// How much each extra item a player commits adds to the success chance. Its own rate, not blended
    /// into <see cref="SuccessChancePerSurplusPoint"/>, so committing items cannot paper over a stats
    /// shortfall or vice versa.
    /// </summary>
    /// <remarks>
    /// Applies per extra copy, up to each requirement's <see cref="ItemRequirement.MaxExtras"/>. The
    /// required copies themselves add nothing: they are the price of admission, not a boost.
    /// </remarks>
    public double ExtraItemSuccessChance { get; init; }

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
    public bool MeetsItemRequirements(
        IReadOnlyDictionary<string, int>? held,
        IReadOnlyDictionary<string, int>? extraItems = null)
    {
        held ??= EmptyHeldItems;
        extraItems ??= EmptyHeldItems;
        return ItemRequirements.All(
            r => held.GetValueOrDefault(r.ItemId) >= r.MinQuantity + extraItems.GetValueOrDefault(r.ItemId));
    }

    /// <summary>
    /// Whether the extras a player asked to commit are ones this job accepts: each named item is one of
    /// its requirements, and each count is between zero and that requirement's
    /// <see cref="ItemRequirement.MaxExtras"/>. <see langword="null"/> or empty means none, which is
    /// always acceptable.
    /// </summary>
    /// <remarks>
    /// A malformed request rather than a game state: a player who simply lacks the items is refused by
    /// <see cref="MeetsItemRequirements"/>, but asking for an item the job does not use, a negative
    /// number, or more than the content allows cannot be satisfied by getting more of anything.
    /// </remarks>
    public bool AcceptsExtras(IReadOnlyDictionary<string, int>? extraItems)
    {
        if (extraItems is null)
            return true;

        foreach (var (itemId, count) in extraItems)
        {
            var requirement = ItemRequirements.FirstOrDefault(r => r.ItemId == itemId);
            if (requirement.ItemId is null || count < 0 || count > requirement.MaxExtras)
                return false;
        }

        return true;
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
    /// The optional <paramref name="extraItems"/> — how many extra copies of each required item the
    /// player has committed — feeds a second, independent term at
    /// <see cref="ExtraItemSuccessChance"/> per extra, up to each requirement's
    /// <see cref="ItemRequirement.MaxExtras"/>. It is added alongside the stat term, not blended into
    /// it, for the same reason stat surplus floors per stat rather than summing algebraically: items
    /// must not paper over a stats shortfall or vice versa. Omitted (which is what <c>GET /jobs</c>
    /// passes, since the player has not chosen yet), the term is zero, so the listed odds are the
    /// odds of an attempt with no extras.
    /// </para>
    /// </remarks>
    public double SuccessChanceFor(
        int effectiveAttack,
        int effectiveDefense,
        double maxSuccessChance,
        IReadOnlyDictionary<string, int>? extraItems = null)
    {
        double ceiling = Math.Min(1.0, maxSuccessChance);

        int statSurplus = Math.Max(0, effectiveAttack - AttackRequirement)
                        + Math.Max(0, effectiveDefense - DefenseRequirement);

        var extras = extraItems ?? EmptyHeldItems;
        int committedExtras = ItemRequirements.Sum(
            r => Math.Clamp(extras.GetValueOrDefault(r.ItemId), 0, r.MaxExtras));

        double chance = BaseSuccessChance
            + SuccessChancePerSurplusPoint * statSurplus
            + ExtraItemSuccessChance * committedExtras;

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
            if (requirement.MaxExtras < 0)
                throw new ArgumentOutOfRangeException(nameof(ItemRequirements), requirement.MaxExtras,
                    $"Job '{Id}' has an item requirement for '{requirement.ItemId}' allowing a negative " +
                    "number of extras.");
            // A duplicate would let MeetsItemRequirements evaluate the same held quantity against
            // the same minimum twice — harmless there, but JobService.Attempt would then build two
            // ItemConsumption entries for the one item, and the second Consume() at the
            // infrastructure layer would throw against a row already drained by the first.
            if (!seenItemIds.Add(requirement.ItemId))
                throw new ArgumentException(
                    $"Job '{Id}' has a duplicate item requirement for '{requirement.ItemId}'.",
                    nameof(ItemRequirements));
        }
        if (ExtraItemSuccessChance < 0.0)
            // A negative gain would make committing items actively harmful, which no content should be
            // able to say.
            throw new ArgumentOutOfRangeException(nameof(ExtraItemSuccessChance),
                ExtraItemSuccessChance,
                $"Job '{Id}' cannot lose success chance per extra item.");
        // Extras that change nothing would cost the player items for no effect: allowing them to be
        // committed with a rate of zero is a content mistake, not a design choice.
        if (ExtraItemSuccessChance == 0.0 && ItemRequirements.Any(r => r.MaxExtras > 0))
            throw new ArgumentOutOfRangeException(nameof(ExtraItemSuccessChance),
                ExtraItemSuccessChance,
                $"Job '{Id}' lets extra items be committed but gives each a success chance of zero.");
    }
}
