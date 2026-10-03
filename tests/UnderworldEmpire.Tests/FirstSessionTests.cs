using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Progression;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Pins the <b>first session</b> as a property of the shipped content pack, not of any one number.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists.</b> The 2026-10-02 core-loop audit measured that only <b>1%</b> of new
/// players reached level 2 in their first session. The cause was not a wrong value — it was that
/// nothing asserted the property the values existed to produce, so a comment in
/// <see cref="LevelCurve"/> drifted from reality (it claimed a full bar banked "roughly 91 XP"; the
/// measured figure was 68.6) and nothing caught it for months.
/// </para>
/// <para>
/// A first session with no level-up fires <i>none</i> of the game's reward vocabulary: no level, no
/// skill point, no meter refill, no job unlock. So "a new player can reach level 2 on their opening
/// bar" is the single most load-bearing tuning property in the pack, and these tests make it a
/// build failure rather than a playtest discovery.
/// </para>
/// <para>
/// Every assertion reads the <b>real</b> <c>content/crime</c> pack and the real domain constants —
/// no literals — so a later balance pass that breaks the property breaks a test, whichever of the
/// several contributing values it happens to move.
/// </para>
/// </remarks>
public class FirstSessionTests
{
    /// <summary>
    /// The share of new players that must reach level 2 on their opening bar.
    /// </summary>
    /// <remarks>
    /// Not 100%: the starter job can fail, so an unlucky player will always exist and pinning
    /// certainty would forbid the job having any risk at all. 95% keeps the reward moment
    /// effectively universal while leaving the failure outcome meaningful. The shipped pack sits
    /// at 99.99%, so there is a wide margin before this trips.
    /// </remarks>
    private const double RequiredChance = 0.95;

    /// <summary>
    /// The core invariant: a full opening energy bar of the cheapest level-1 job, rolled at its
    /// <b>base</b> success chance, must get <i>almost every</i> new player to level 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This asserts a percentile, not a mean, and the distinction is the whole point.</b> The
    /// first version of this test compared <i>expected</i> XP against the threshold, which sounds
    /// equivalent and is not: an average is cleared by a player having a good run, while the
    /// property the pack actually needs is that a player having a <i>normal</i> run gets there.
    /// Measured against this very catalogue, an expectation-only assertion still passes at a base
    /// chance of 0.45 — where expected XP is 101 against a threshold of 100, and yet only
    /// <b>58.6%</b> of new players reach level 2. Two in five would finish their first session
    /// with nothing, which is the exact failure this class exists to prevent.
    /// </para>
    /// <para>
    /// Base chance, not geared chance — the floor a player sitting exactly on the requirements
    /// gets, never the better odds a player with gear enjoys. A new player has nothing equipped.
    /// Mirrors the reasoning in
    /// <c>JobCatalogLoaderTests.Real_crime_catalog_lets_a_fresh_player_reach_a_mastery_rank_on_one_energy_bar</c>,
    /// which protects the mastery half of the same checkpoint.
    /// </para>
    /// </remarks>
    [Fact]
    public void Nearly_every_new_player_reaches_level_2_on_their_opening_bar()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");
        var cheapest = CheapestLevel1Job(catalog);

        int attemptsOnAFullBar = PlayerDefaults.EnergyMax / cheapest.EnergyCost;
        long needed = LevelCurve.XpToReach(2);
        double chance = ChanceOfReaching(catalog, cheapest, attemptsOnAFullBar, needed);

        Assert.True(chance >= RequiredChance,
            $"Only {chance:P2} of new players would reach level 2 on their opening bar, below the " +
            $"{RequiredChance:P0} this pack is required to deliver. A bar of " +
            $"{PlayerDefaults.EnergyMax} energy buys {attemptsOnAFullBar} attempts at " +
            $"'{cheapest.Id}' ({cheapest.EnergyCost} energy each) at a base chance of " +
            $"{cheapest.BaseSuccessChance}, worth {ExpectedXpPerAttempt(catalog, cheapest):0.##} XP " +
            $"an attempt on average against the {needed} level 2 needs. Note the average alone can " +
            $"clear the threshold while most players still fall short — that is why this asserts a " +
            $"proportion. Raise {nameof(PlayerDefaults)}.{nameof(PlayerDefaults.EnergyMax)}, raise " +
            $"the job's base success chance or XP, or lower the level-2 threshold.");
    }

    /// <summary>
    /// The very first tap must be legal for a brand-new player. An opening bar is worthless if
    /// nothing in the catalogue can be attempted with base stats and no gear.
    /// </summary>
    [Fact]
    public void The_cheapest_level_1_job_is_attemptable_with_nothing_equipped()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");
        var cheapest = CheapestLevel1Job(catalog);

        Assert.True(cheapest.EnergyCost <= PlayerDefaults.EnergyMax,
            $"'{cheapest.Id}' costs {cheapest.EnergyCost} energy but a new player's bar is only " +
            $"{PlayerDefaults.EnergyMax}.");
        Assert.True(cheapest.AttackRequirement <= PlayerDefaults.BaseAttack,
            $"'{cheapest.Id}' needs {cheapest.AttackRequirement} attack but a new player has " +
            $"{PlayerDefaults.BaseAttack} and no gear.");
        Assert.True(cheapest.DefenseRequirement <= PlayerDefaults.BaseDefense,
            $"'{cheapest.Id}' needs {cheapest.DefenseRequirement} defense but a new player has " +
            $"{PlayerDefaults.BaseDefense} and no gear.");
        Assert.Empty(cheapest.ItemRequirements);
    }

    /// <summary>
    /// A new player's <i>first ever action</i> should usually work. The audit measured a 35% chance
    /// that it failed, which is a poor opening impression for a tutorial job.
    /// </summary>
    /// <remarks>
    /// 0.75 is a floor, not a target — it exists to stop the starter job drifting back toward a
    /// coin flip, not to pin the current 0.80.
    /// </remarks>
    [Fact]
    public void The_first_tap_is_more_likely_to_succeed_than_not_by_a_clear_margin()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");
        var cheapest = CheapestLevel1Job(catalog);

        Assert.True(cheapest.BaseSuccessChance >= 0.75,
            $"The starter job '{cheapest.Id}' succeeds {cheapest.BaseSuccessChance:P0} of the time " +
            $"at base stats, so a new player's first action in the game fails " +
            $"{1 - cheapest.BaseSuccessChance:P0} of the time.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// The job a brand-new player actually taps first: the cheapest one they are allowed to attempt.
    /// </summary>
    private static Job CheapestLevel1Job(JobCatalog catalog) => catalog.Jobs
        .Where(j => j.LevelRequirement == 1)
        .OrderBy(j => j.EnergyCost)
        .First();

    /// <summary>
    /// Expected XP from one attempt at the job's base chance, counting the consolation a failure
    /// still pays.
    /// </summary>
    /// <remarks>
    /// The consolation is rounded <b>up</b>, matching <c>JobService.Fraction</c> — 10% of a 10-XP
    /// job is 1.0 exactly, but on any job where it is fractional the engine pays the ceiling, and a
    /// test that floored it here would understate the real figure.
    /// </remarks>
    private static double ExpectedXpPerAttempt(JobCatalog catalog, Job job)
    {
        double consolationXp = Math.Ceiling(job.Xp * catalog.ConsolationXpFraction);
        return job.BaseSuccessChance * job.Xp
             + (1 - job.BaseSuccessChance) * consolationXp;
    }

    /// <summary>
    /// The share of players who bank at least <paramref name="neededXp"/> from
    /// <paramref name="attempts"/> attempts at <paramref name="job"/>, rolled at its base chance.
    /// </summary>
    /// <remarks>
    /// Every attempt is an independent Bernoulli trial paying either the job's full XP or the
    /// pack's consolation, so the number of wins is binomial and the whole distribution is cheap
    /// to enumerate exactly at these sizes — no sampling, no seeded simulation, no flakiness.
    /// </remarks>
    private static double ChanceOfReaching(JobCatalog catalog, Job job, int attempts, long neededXp)
    {
        double p = job.BaseSuccessChance;
        long win = job.Xp;
        long consolation = (long)Math.Ceiling(job.Xp * catalog.ConsolationXpFraction);

        double chance = 0;
        for (int successes = 0; successes <= attempts; successes++)
        {
            long xp = successes * win + (attempts - successes) * consolation;
            if (xp >= neededXp)
                chance += BinomialProbability(attempts, successes, p);
        }

        return chance;
    }

    /// <summary>C(n,k) · p^k · (1-p)^(n-k).</summary>
    /// <remarks>
    /// The coefficient is built up multiplicatively rather than from factorials, which would
    /// overflow long before it needed to.
    /// </remarks>
    private static double BinomialProbability(int n, int k, double p)
    {
        double coefficient = 1;
        for (int i = 0; i < k; i++)
            coefficient = coefficient * (n - i) / (i + 1);

        return coefficient * Math.Pow(p, k) * Math.Pow(1 - p, n - k);
    }
}
