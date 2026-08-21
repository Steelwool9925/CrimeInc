using UnderworldEmpire.Domain.Jobs;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="Job"/> (T3R.1) — the risk fields and the success-chance curve. Mirrors
/// <see cref="PropertyTests"/>: the content loader is the primary validation path, but
/// <see cref="Job.EnsureValid"/> protects hand-built definitions.
/// </summary>
public class JobTests
{
    private static Job TestJob(
        string id = "shake-down-corner-store",
        string name = "Shake Down a Corner Store",
        int energyCost = 1,
        int levelRequirement = 1,
        long cashMin = 15,
        long cashMax = 30,
        long xp = 10,
        int attackRequirement = 0,
        int defenseRequirement = 0,
        double baseSuccessChance = 1.0,
        double successChancePerSurplusPoint = 0.0,
        int healthLossMin = 0,
        int healthLossMax = 0) => new()
        {
            Id = id,
            Name = name,
            EnergyCost = energyCost,
            LevelRequirement = levelRequirement,
            CashMin = cashMin,
            CashMax = cashMax,
            Xp = xp,
            AttackRequirement = attackRequirement,
            DefenseRequirement = defenseRequirement,
            BaseSuccessChance = baseSuccessChance,
            SuccessChancePerSurplusPoint = successChancePerSurplusPoint,
            HealthLossMin = healthLossMin,
            HealthLossMax = healthLossMax
        };

    /// <summary>A risky job in the shape content will supply: 5/3 required, 0.75 base, +1% a point.</summary>
    private static Job RiskyJob() => TestJob(
        attackRequirement: 5,
        defenseRequirement: 3,
        baseSuccessChance: 0.75,
        successChancePerSurplusPoint: 0.01,
        healthLossMin: 0,
        healthLossMax: 3);

    // -------------------------------------------------------------------------
    // Defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void A_job_built_in_code_is_certain_and_ungated_by_default()
    {
        // Permissive domain defaults, strict content validation: a Job constructed by a test or a
        // future generator must never wander into the failure path because a field was left unset.
        var job = TestJob();

        Assert.Equal(1.0, job.BaseSuccessChance);
        Assert.Equal(0, job.AttackRequirement);
        Assert.Equal(0, job.DefenseRequirement);
        Assert.Equal(0.0, job.SuccessChancePerSurplusPoint);
        Assert.Equal(0, job.HealthLossMin);
        Assert.Equal(0, job.HealthLossMax);
    }

    // -------------------------------------------------------------------------
    // EnsureValid
    // -------------------------------------------------------------------------

    [Fact]
    public void A_valid_risky_job_passes_validation()
    {
        RiskyJob().EnsureValid();
    }

    [Fact]
    public void A_negative_attack_requirement_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(attackRequirement: -1).EnsureValid());
    }

    [Fact]
    public void A_negative_defense_requirement_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(defenseRequirement: -1).EnsureValid());
    }

    [Fact]
    public void A_base_success_chance_below_zero_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(baseSuccessChance: -0.01).EnsureValid());
    }

    [Fact]
    public void A_base_success_chance_above_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(baseSuccessChance: 1.01).EnsureValid());
    }

    [Fact]
    public void A_negative_per_surplus_point_gain_is_rejected()
    {
        // A negative gain would make gear actively harmful, which no content should be able to say.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(successChancePerSurplusPoint: -0.01).EnsureValid());
    }

    [Fact]
    public void A_negative_minimum_health_loss_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(healthLossMin: -1).EnsureValid());
    }

    [Fact]
    public void A_maximum_health_loss_below_the_minimum_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(healthLossMin: 5, healthLossMax: 4).EnsureValid());
    }

    // -------------------------------------------------------------------------
    // MeetsStatRequirements
    // -------------------------------------------------------------------------

    [Fact]
    public void Stats_exactly_at_the_requirement_meet_it()
    {
        // The gate is >=, not >: content saying "attack 5" must admit a player with attack 5.
        Assert.True(RiskyJob().MeetsStatRequirements(effectiveAttack: 5, effectiveDefense: 3));
    }

    [Fact]
    public void Attack_below_the_requirement_fails_the_gate()
    {
        Assert.False(RiskyJob().MeetsStatRequirements(effectiveAttack: 4, effectiveDefense: 3));
    }

    [Fact]
    public void Defense_below_the_requirement_fails_the_gate()
    {
        Assert.False(RiskyJob().MeetsStatRequirements(effectiveAttack: 5, effectiveDefense: 2));
    }

    [Fact]
    public void A_job_with_no_requirements_admits_anyone()
    {
        Assert.True(TestJob().MeetsStatRequirements(effectiveAttack: 0, effectiveDefense: 0));
    }

    // -------------------------------------------------------------------------
    // SuccessChanceFor
    // -------------------------------------------------------------------------

    [Fact]
    public void Stats_exactly_at_the_requirement_give_the_base_chance()
    {
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 5, effectiveDefense: 3, maxSuccessChance: 0.95);

        Assert.Equal(0.75, chance, precision: 10);
    }

    [Fact]
    public void Surplus_in_either_stat_raises_the_chance()
    {
        // Four points of attack surplus and two of defense: six points at 1% each.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 9, effectiveDefense: 5, maxSuccessChance: 0.95);

        Assert.Equal(0.81, chance, precision: 10);
    }

    [Fact]
    public void A_surplus_in_one_stat_does_not_paper_over_a_shortfall_in_the_other()
    {
        // Ten points of attack surplus, two points short on defense. Each stat's surplus floors at
        // zero independently — summing them algebraically would let a big attack overshoot cancel
        // the shortfall, and the defense requirement would stop meaning anything.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 15, effectiveDefense: 1, maxSuccessChance: 0.95);

        Assert.Equal(0.85, chance, precision: 10);
    }

    [Fact]
    public void The_chance_is_capped_at_the_packs_ceiling()
    {
        // Fifty points of surplus would reach 1.25 uncapped. No job is ever a certainty.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 55, effectiveDefense: 3, maxSuccessChance: 0.95);

        Assert.Equal(0.95, chance, precision: 10);
    }

    [Fact]
    public void The_chance_never_exceeds_one_even_if_content_says_it_may()
    {
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 500, effectiveDefense: 3, maxSuccessChance: 2.0);

        Assert.Equal(1.0, chance, precision: 10);
    }

    [Fact]
    public void The_chance_never_falls_below_the_base()
    {
        // Below the requirement the gate refuses the attempt outright, but GET /jobs still reports a
        // figure for the row — and that figure must not read as worse than the job's own floor.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 0, effectiveDefense: 0, maxSuccessChance: 0.95);

        Assert.Equal(0.75, chance, precision: 10);
    }

    [Fact]
    public void A_ceiling_below_the_jobs_own_floor_is_a_contradiction_and_throws()
    {
        // The loader validates this pack-wide, so reaching here means a hand-built job disagrees
        // with the rules it was handed. Failing loudly beats silently picking one of the two.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RiskyJob().SuccessChanceFor(
                effectiveAttack: 5, effectiveDefense: 3, maxSuccessChance: 0.5));

        Assert.Contains("shake-down-corner-store", ex.Message);
    }
}
