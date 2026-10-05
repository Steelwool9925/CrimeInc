using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Domain.Randomness;

namespace UnderworldEmpire.Domain.Jobs;

/// <summary>
/// The rules of performing a job: what is allowed, what it costs, and what it pays. Pure game logic
/// with no I/O — it mutates the objects handed to it and returns what happened. Persisting the
/// result is the caller's job.
/// </summary>
/// <remarks>
/// This is where the server's authority over the game loop actually lives. Every figure the player
/// receives is decided here from content plus a server-side roll; nothing is taken from the request.
/// </remarks>
public sealed class JobService
{
    private readonly IRandomSource _random;

    public JobService(IRandomSource random)
    {
        _random = random;
    }

    /// <summary>
    /// Attempts <paramref name="job"/> for <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// Three outcomes, not two. On <b>refusal</b> — level, stats or energy — nothing is mutated at
    /// all: checks run to completion before the first write, so a refused attempt cannot leave a
    /// player half-charged. On a <b>failed</b> attempt the energy is still spent, a consolation
    /// share of the cash and XP is still paid, health may drop, and mastery does not move. On a
    /// <b>successful</b> attempt everything applies in full.
    /// </remarks>
    /// <param name="player">The acting player; mutated whenever the attempt is applied.</param>
    /// <param name="job">The job definition from content.</param>
    /// <param name="mastery">
    ///     This player's mastery row for this job; mutated only on a <b>successful</b> attempt.
    /// </param>
    /// <param name="loadout">
    ///     The player's effective combat stats — base plus equipped gear. The stat gate and the odds
    ///     both read this rather than <see cref="Player.Attack"/>, which is what makes equipping a
    ///     weapon open jobs up.
    /// </param>
    /// <param name="completionsPerRank">Content-supplied completions per mastery rank.</param>
    /// <param name="maxRank">Content-supplied mastery rank ceiling.</param>
    /// <param name="risk">Content-supplied pack-wide risk rules.</param>
    /// <param name="nowUtc">The instant to evaluate meter regeneration against.</param>
    /// <param name="heldItemQuantities">
    ///     The player's current item quantities, keyed by item id — read independently of
    ///     <paramref name="loadout"/>'s equipped-only view, since a job's item gate asks "how many do
    ///     you hold" rather than "what do you have on". Omitted (or <see langword="null"/>) for a job
    ///     with no <see cref="Job.ItemRequirements"/>, which every job shipped before this parameter
    ///     existed is.
    /// </param>
    public JobAttemptResult Attempt(
        Player player,
        Job job,
        PlayerJobMastery mastery,
        Loadout loadout,
        int completionsPerRank,
        int maxRank,
        JobRiskRules risk,
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<string, int>? heldItemQuantities = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mastery);
        ArgumentNullException.ThrowIfNull(loadout);

        var held = heldItemQuantities ?? Job.EmptyHeldItems;

        // ---- Validate before mutating anything ----

        // Precedence is level -> stats -> items -> energy: most permanent reason first, so a player
        // failing several gates is told the one that will take longest to fix. Items sits with
        // stats — both are "go buy something" fixes — ahead of energy, which just needs waiting out.
        // Same order as GET /jobs.
        if (player.Level < job.LevelRequirement)
            return JobAttemptResult.Refused(JobFailure.LevelTooLow);

        if (!job.MeetsStatRequirements(loadout.TotalAttack, loadout.TotalDefense))
            return JobAttemptResult.Refused(JobFailure.StatsTooLow);

        if (!job.MeetsItemRequirements(held))
            return JobAttemptResult.Refused(JobFailure.InsufficientItems);

        int currentEnergy = player.Energy.CurrentAt(nowUtc, MeterRates.Energy);
        if (currentEnergy < job.EnergyCost)
            return JobAttemptResult.Refused(JobFailure.InsufficientEnergy);

        // ---- Apply ----

        // SpendAt re-resolves the current value itself and resets the regen clock, so partial
        // progress toward the next point is deliberately forfeited on a spend. Spent on both
        // branches: energy is what a failed attempt costs, and is the whole risk.
        player.Energy = player.Energy.SpendAt(job.EnergyCost, nowUtc, MeterRates.Energy);

        // Roll order is part of the contract, because FakeRandomSource.Sequence scripts against it:
        // (1) success, (2) cash, (3) health loss on the failure branch only.
        bool succeeded = RollSucceeded(job, loadout, risk, held);

        // The cash range is rolled on both branches — the consolation is a fraction of what the
        // attempt *would* have earned, and skipping the roll on a failure would also shift every
        // later roll in the stream.
        long rolledCash = _random.NextInclusive(job.CashMin, job.CashMax);

        long cashEarned = succeeded ? rolledCash : Fraction(rolledCash, risk.ConsolationCashFraction);
        long xpEarned = succeeded ? job.Xp : Fraction(job.Xp, risk.ConsolationXpFraction);

        int healthLost = 0;
        if (!succeeded)
        {
            long rolledLoss = _random.NextInclusive(job.HealthLossMin, job.HealthLossMax);

            // Clamped to what is actually left: Meter.SpendAt throws on an overspend, so it must
            // never be handed a raw roll. The floor is 0, not a negative bar and not an exception.
            int currentHealth = player.Health.CurrentAt(nowUtc, MeterRates.Health);
            healthLost = (int)Math.Min(rolledLoss, currentHealth);

            if (healthLost > 0)
                player.Health = player.Health.SpendAt(healthLost, nowUtc, MeterRates.Health);
        }

        player.AddCash(cashEarned);

        // Health loss is applied *before* this. A failed attempt still grants consolation XP, which
        // can still cross a level threshold, and a level-up refills every meter — in the other order
        // the health penalty would be silently undone by the refill.
        //
        // Levelling is applied here, inside the same attempt, so the caller persists the level, the
        // granted skill points and the refilled meters in the one SaveChanges as everything else.
        var levelUp = player.ApplyXpAndLevelUp(xpEarned, nowUtc);

        // Mastery measures completions, not attempts. A failure advances nothing.
        bool rankedUp = succeeded && mastery.RecordCompletion(completionsPerRank, maxRank);

        // Consumption is deterministic, not rolled: every requirement spends its declared quantity
        // whether the attempt succeeds or fails, so it is computed here rather than needing its own
        // place in the roll order. Empty for the jobs that opt into no item gate.
        IReadOnlyList<ItemConsumption> itemsConsumed = job.ItemRequirements.Count == 0
            ? Array.Empty<ItemConsumption>()
            : job.ItemRequirements
                .Select(requirement => new ItemConsumption(
                    ItemId: requirement.ItemId,
                    QuantityConsumed: requirement.QuantityConsumed,
                    QuantityRemaining: held.GetValueOrDefault(requirement.ItemId) - requirement.QuantityConsumed))
                .ToList();

        return JobAttemptResult.Applied(new JobOutcome(
            JobId: job.Id,
            Succeeded: succeeded,
            EnergySpent: job.EnergyCost,
            CashEarned: cashEarned,
            XpEarned: xpEarned,
            HealthLost: healthLost,
            Completions: mastery.Completions,
            MasteryRank: mastery.Rank,
            MasteryRankedUp: rankedUp,
            CompletionsUntilNextRank: mastery.CompletionsUntilNextRank(completionsPerRank, maxRank),
            ItemsConsumed: itemsConsumed),
            levelUp);
    }

    /// <summary>
    /// Rolls the attempt against the job's live success chance, in basis points.
    /// </summary>
    /// <remarks>
    /// Basis points rather than a <c>NextDouble</c> added to <see cref="IRandomSource"/>: that keeps
    /// the single-method seam, and leaves <c>FakeRandomSource</c>'s scripted and boundary modes
    /// exactly assertable. The comparison is <c>&lt;=</c>, so a chance of 0.95 wins on a roll of
    /// 9500 and loses on 9501.
    /// </remarks>
    private bool RollSucceeded(Job job, Loadout loadout, JobRiskRules risk, IReadOnlyDictionary<string, int> held)
    {
        double chance = job.SuccessChanceFor(
            loadout.TotalAttack, loadout.TotalDefense, risk.MaxSuccessChance, held);

        long chanceBasisPoints = (long)Math.Round(chance * BasisPoints, MidpointRounding.AwayFromZero);

        return _random.NextInclusive(1, BasisPoints) <= chanceBasisPoints;
    }

    /// <summary>
    /// A consolation share of <paramref name="amount"/>, rounded <b>up</b>.
    /// </summary>
    /// <remarks>
    /// Rounded up so a consolation is never quite nothing: 1% of the cheapest job's 15 cash is 0.15,
    /// which floors to zero and would make the consolation a lie on every cheap job in the pack.
    /// </remarks>
    private static long Fraction(long amount, double fraction) =>
        (long)Math.Ceiling(amount * fraction);

    /// <summary>One hundred percent, expressed in basis points.</summary>
    private const int BasisPoints = 10_000;
}
