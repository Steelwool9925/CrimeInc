using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Design properties of the shipped objective pool, asserted against the real <c>content/crime</c>
/// files. Same job as <c>JobEconomyInvariantTests</c>: a content edit that quietly breaks what the
/// feature exists for fails here rather than in a player's hands.
/// </summary>
/// <remarks>
/// The feature exists to un-dominate the job catalogue (from level 8 one job beat the other five on
/// both axes) and to hand a daily player something worth doing with the energy they have. So the
/// pool must reach every job, and nothing in it may ask for more than a day's energy.
/// <para>
/// Everything that is a game constant is read from one, never a literal: <see cref="PlayerDefaults.EnergyMax"/>
/// has already changed once (10 to 20) and will again.
/// </para>
/// </remarks>
public class ObjectivePoolInvariantTests
{
    private const string Pack = "crime";

    private static readonly ContentPackLoader Loader = new(RepoContent.FindRoot());
    private static readonly ItemCatalog Items = Loader.LoadItems(Pack);
    private static readonly JobCatalog Jobs = Loader.LoadJobs(Pack, Items);
    private static readonly PropertyCatalog Properties = Loader.LoadProperties(Pack);
    private static readonly IReadOnlyList<Objective> Pool = Loader.LoadObjectives(Pack, Jobs).Objectives;

    // ---- what a player at a given level can do ----

    /// <summary>
    /// Jobs a player at <paramref name="level"/> can attempt. A job behind an item gate counts: the item
    /// is for sale, so it is a cost rather than a wall.
    /// </summary>
    private static IReadOnlyList<Job> Eligible(int level) =>
        Jobs.Jobs.Where(j => j.LevelRequirement <= level).ToList();

    /// <summary>The most attack and defense a player at <paramref name="level"/> can have: skill points plus the best gear each slot offers.</summary>
    private static (int Attack, int Defense) Attainable(int level)
    {
        int attack = PlayerDefaults.BaseAttack + (level - 1) * PlayerDefaults.SkillPointsPerLevel * PlayerDefaults.StatIncreasePerSkillPoint;
        int defense = PlayerDefaults.BaseDefense + (level - 1) * PlayerDefaults.SkillPointsPerLevel * PlayerDefaults.StatIncreasePerSkillPoint;

        foreach (var slot in Items.Items.Where(i => i.LevelRequirement <= level).GroupBy(i => i.Slot))
        {
            attack += slot.Max(i => i.Attack);
            defense += slot.Max(i => i.Defense);
        }

        return (attack, defense);
    }

    /// <summary>
    /// The least energy that can possibly complete <paramref name="o"/> if every attempt succeeds, or
    /// <see langword="null"/> when it cannot be completed at its own minimum level at all.
    /// </summary>
    /// <remarks>
    /// Deliberately the <i>floor</i>, not an expectation: it assumes every attempt is a win. Failed
    /// attempts spend energy and count for nothing, so a real player needs more (a job at 75% needs about a
    /// third again). The invariant therefore proves an objective is <i>possible</i> inside a full bar, not
    /// that it is likely to be finished from one; a regenerating bar and level-up refills cover the rest.
    /// </remarks>
    private static int? EnergyNeeded(Objective o)
    {
        var eligible = Eligible(o.MinLevel);

        switch (o.Type)
        {
            case ObjectiveType.CompleteJobTimes when o.JobId is not null:
                return (int)(o.Target * Jobs.Find(o.JobId)!.EnergyCost);

            case ObjectiveType.CompleteJobTimes:
                return eligible.Count == 0 ? null : (int)(o.Target * eligible.Min(j => j.EnergyCost));

            case ObjectiveType.CompleteDistinctJobs:
                return eligible.Count < o.Target
                    ? null
                    : eligible.Select(j => j.EnergyCost).Order().Take((int)o.Target).Sum();

            case ObjectiveType.EarnCash:
                if (eligible.Count == 0) return null;
                // Best guaranteed cash per energy: the minimum of each job's range, so luck is not assumed.
                double best = eligible.Max(j => (double)j.CashMin / j.EnergyCost);
                return (int)Math.Ceiling(o.Target / best);

            case ObjectiveType.CollectProperties:
                return 0;

            default:
                throw new InvalidOperationException($"No energy model for {o.Type}.");
        }
    }

    // ---- the invariants ----

    [Fact]
    public void Every_job_in_the_pack_is_named_by_at_least_one_objective()
    {
        var named = Pool.Where(o => o.JobId is not null).Select(o => o.JobId!).ToHashSet(StringComparer.Ordinal);
        var missing = Jobs.Jobs.Select(j => j.Id).Where(id => !named.Contains(id)).ToList();

        Assert.True(missing.Count == 0,
            $"These jobs are never an objective, so nothing gives them a purpose: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void Every_job_is_reachable_by_a_named_objective_at_its_own_level_gate()
    {
        // An objective offered below its job's level gate could never be completed. The loader rejects
        // that, but the property is the point of the feature, so it is asserted here as well.
        foreach (var o in Pool.Where(o => o.JobId is not null))
            Assert.True(o.MinLevel >= Jobs.Find(o.JobId!)!.LevelRequirement, $"{o.Id} is offered below {o.JobId}'s level gate.");
    }

    [Fact]
    public void Every_objective_fits_within_one_days_energy()
    {
        foreach (var o in Pool)
        {
            var needed = EnergyNeeded(o);

            Assert.True(needed is not null, $"{o.Id} cannot be completed at level {o.MinLevel}: too few jobs to satisfy it.");
            Assert.True(needed <= PlayerDefaults.EnergyMax,
                $"{o.Id} needs at least {needed} energy but a full bar is {PlayerDefaults.EnergyMax}.");
        }
    }

    [Fact]
    public void Every_objective_pays_at_least_the_floor_rate_for_the_energy_it_asks_for()
    {
        // The floor is the pack's lowest guaranteed cash per energy: the minimum payout of the least
        // rewarding job. An objective paying less than that for its energy is worse than idling.
        double floor = Jobs.Jobs.Min(j => (double)j.CashMin / j.EnergyCost);

        foreach (var o in Pool)
        {
            long needed = EnergyNeeded(o)!.Value;
            Assert.True(o.RewardCash >= needed * floor,
                $"{o.Id} pays {o.RewardCash} for {needed} energy; the floor rate ({floor:0.##}/energy) is {needed * floor:0.##}.");
        }
    }

    [Fact]
    public void Every_objective_pays_something()
    {
        Assert.All(Pool, o => Assert.True(o.RewardCash > 0 || o.RewardXp > 0, o.Id));
    }

    [Fact]
    public void A_job_an_objective_names_can_be_done_with_gear_a_player_of_that_level_can_have()
    {
        foreach (var o in Pool.Where(o => o.JobId is not null))
        {
            var job = Jobs.Find(o.JobId!)!;
            var (attack, defense) = Attainable(o.MinLevel);

            Assert.True(job.AttackRequirement <= attack && job.DefenseRequirement <= defense,
                $"{o.Id} is offered at level {o.MinLevel}, where the best attainable stats are {attack}/{defense}, " +
                $"but {job.Id} needs {job.AttackRequirement}/{job.DefenseRequirement}.");
        }
    }

    [Fact]
    public void A_collection_objective_is_only_offered_once_a_property_can_be_bought()
    {
        foreach (var o in Pool.Where(o => o.Type == ObjectiveType.CollectProperties))
            Assert.Contains(Properties.Properties, p => p.LevelRequirement <= o.MinLevel);
    }

    [Fact]
    public void Every_level_is_offered_at_least_a_full_days_worth()
    {
        // Fewer than three eligible objectives would silently hand a player a short list.
        for (int level = 1; level <= LevelCapForTest; level++)
        {
            int eligible = Pool.Count(o => o.MinLevel <= level);
            Assert.True(eligible >= DailyObjectiveService.ObjectivesPerDay,
                $"Level {level} has only {eligible} eligible objectives.");
        }
    }

    [Fact]
    public void The_pool_is_large_enough_for_the_selection_to_vary()
    {
        // A pool barely above three would repeat; the spread tests in ObjectiveSelectionTests lean on this.
        Assert.True(Pool.Count >= 12, $"Only {Pool.Count} objectives.");
    }

    [Fact]
    public void Every_kind_of_objective_is_in_the_pool()
    {
        var present = Pool.Select(o => o.Type).ToHashSet();

        Assert.All(Enum.GetValues<ObjectiveType>(), t => Assert.Contains(t, present));
    }

    private static int LevelCapForTest => PlayerDefaults.MaxLevel;
}
