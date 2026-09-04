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
        IReadOnlyList<ItemRequirement>? itemRequirements = null,
        double itemSuccessChancePerSurplusPoint = 0.0,
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
            ItemRequirements = itemRequirements ?? [],
            ItemSuccessChancePerSurplusPoint = itemSuccessChancePerSurplusPoint,
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

    /// <summary>A job gated on holding at least 2 switchblades, +1% success per surplus item.</summary>
    private static Job ItemGatedJob(int minQuantity = 2, double itemSuccessChancePerSurplusPoint = 0.01) =>
        TestJob(
            baseSuccessChance: 0.75,
            itemRequirements: [new ItemRequirement("switchblade", minQuantity)],
            itemSuccessChancePerSurplusPoint: itemSuccessChancePerSurplusPoint);

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
        Assert.Empty(job.ItemRequirements);
        Assert.Equal(0.0, job.ItemSuccessChancePerSurplusPoint);
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

    [Fact]
    public void A_valid_item_gated_job_passes_validation()
    {
        ItemGatedJob().EnsureValid();
    }

    [Fact]
    public void An_item_requirement_with_a_blank_item_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => TestJob(itemRequirements: [new ItemRequirement("", 2)]).EnsureValid());
    }

    [Fact]
    public void An_item_requirement_with_a_minimum_quantity_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(itemRequirements: [new ItemRequirement("switchblade", 0)]).EnsureValid());
    }

    [Fact]
    public void A_negative_item_success_chance_per_surplus_point_is_rejected()
    {
        // Same reasoning as the stat gain: a negative rate would make spare stock actively harmful.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestJob(itemSuccessChancePerSurplusPoint: -0.01).EnsureValid());
    }

    // -------------------------------------------------------------------------
    // MeetsItemRequirements
    // -------------------------------------------------------------------------

    [Fact]
    public void A_job_with_no_item_requirements_admits_anyone()
    {
        Assert.True(TestJob().MeetsItemRequirements(new Dictionary<string, int>()));
    }

    [Fact]
    public void A_job_with_no_item_requirements_admits_a_null_holding()
    {
        // A caller with nothing to report should not need to construct a dictionary just to ask.
        Assert.True(TestJob().MeetsItemRequirements(null));
    }

    [Fact]
    public void Holding_exactly_the_minimum_quantity_meets_the_gate()
    {
        // The gate is >=, not >, matching MeetsStatRequirements: content saying "2" admits a player
        // holding exactly 2.
        Assert.True(ItemGatedJob(minQuantity: 2).MeetsItemRequirements(
            new Dictionary<string, int> { ["switchblade"] = 2 }));
    }

    [Fact]
    public void Holding_less_than_the_minimum_quantity_fails_the_gate()
    {
        Assert.False(ItemGatedJob(minQuantity: 2).MeetsItemRequirements(
            new Dictionary<string, int> { ["switchblade"] = 1 }));
    }

    [Fact]
    public void Holding_none_of_the_required_item_fails_the_gate()
    {
        Assert.False(ItemGatedJob(minQuantity: 2).MeetsItemRequirements(new Dictionary<string, int>()));
    }

    [Fact]
    public void Unrelated_held_items_do_not_satisfy_a_different_requirement()
    {
        Assert.False(ItemGatedJob(minQuantity: 2).MeetsItemRequirements(
            new Dictionary<string, int> { ["brass-knuckles"] = 50 }));
    }

    [Fact]
    public void A_job_with_multiple_item_requirements_needs_every_one_met()
    {
        var job = TestJob(itemRequirements:
        [
            new ItemRequirement("switchblade", 2),
            new ItemRequirement("kevlar-vest", 1)
        ]);

        // Only one of the two requirements met.
        Assert.False(job.MeetsItemRequirements(
            new Dictionary<string, int> { ["switchblade"] = 2 }));

        Assert.True(job.MeetsItemRequirements(
            new Dictionary<string, int> { ["switchblade"] = 2, ["kevlar-vest"] = 1 }));
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
    public void A_ceiling_below_the_jobs_own_floor_still_wins()
    {
        // The two legitimately disagree for a job built in code: the domain default is 1.0 so a
        // hand-built job never fails by accident, while a realistic pack caps at 0.95. Throwing here
        // would turn that benign default into an exception the moment it met real pack rules.
        // Content that genuinely disagrees is caught at load instead, naming the job — see
        // JobCatalogLoaderTests.A_base_success_chance_above_the_pack_ceiling_throws.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 5, effectiveDefense: 3, maxSuccessChance: 0.5);

        Assert.Equal(0.5, chance, precision: 10);
    }

    [Fact]
    public void The_permissive_default_survives_a_realistic_pack_ceiling()
    {
        // The exact collision that caught this: TestJob's default 1.0 under the shipped 0.95.
        var chance = TestJob().SuccessChanceFor(
            effectiveAttack: 5, effectiveDefense: 5, maxSuccessChance: 0.95);

        Assert.Equal(0.95, chance, precision: 10);
    }

    // -------------------------------------------------------------------------
    // SuccessChanceFor — item surplus
    // -------------------------------------------------------------------------

    [Fact]
    public void Omitting_held_items_computes_exactly_what_it_always_has()
    {
        // No heldItemQuantities argument at all — every pre-existing call site (and every job with
        // no ItemRequirements) must see the identical figure it always has.
        var chance = RiskyJob().SuccessChanceFor(
            effectiveAttack: 9, effectiveDefense: 5, maxSuccessChance: 0.95);

        Assert.Equal(0.81, chance, precision: 10);
    }

    [Fact]
    public void Item_surplus_raises_the_chance_alongside_the_stat_term()
    {
        // Requirement is 2, holding 5: three points of item surplus at 1% each, on top of the 0.75
        // base with no stat surplus (requirements are 0/0 by default).
        var job = ItemGatedJob(minQuantity: 2, itemSuccessChancePerSurplusPoint: 0.01);

        var chance = job.SuccessChanceFor(
            effectiveAttack: 0, effectiveDefense: 0, maxSuccessChance: 0.95,
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 5 });

        Assert.Equal(0.78, chance, precision: 10);
    }

    [Fact]
    public void Item_surplus_floors_at_zero_and_does_not_paper_over_a_shortfall_elsewhere()
    {
        // Holding fewer than the minimum contributes no *negative* surplus — the shortfall is what
        // the gate is for, not a penalty layered onto the odds of an attempt that should be refused.
        var job = ItemGatedJob(minQuantity: 5, itemSuccessChancePerSurplusPoint: 0.01);

        var chance = job.SuccessChanceFor(
            effectiveAttack: 0, effectiveDefense: 0, maxSuccessChance: 0.95,
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 1 });

        Assert.Equal(0.75, chance, precision: 10);
    }

    [Fact]
    public void Item_surplus_and_stat_surplus_add_independently()
    {
        // 5 points of stat surplus at 1% (from RiskyJob's 5/3 requirement) plus 3 points of item
        // surplus at 1%: neither term is folded into the other's floor.
        var job = TestJob(
            attackRequirement: 5,
            defenseRequirement: 3,
            baseSuccessChance: 0.75,
            successChancePerSurplusPoint: 0.01,
            itemRequirements: [new ItemRequirement("switchblade", 2)],
            itemSuccessChancePerSurplusPoint: 0.01);

        var chance = job.SuccessChanceFor(
            effectiveAttack: 8, effectiveDefense: 5, maxSuccessChance: 0.95,
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 5 });

        // Stat surplus: (8-5) + (5-3) = 5. Item surplus: 5-2 = 3. Total 8 points at 1% = 0.08.
        Assert.Equal(0.83, chance, precision: 10);
    }

    [Fact]
    public void The_item_surplus_ceiling_still_wins()
    {
        var job = ItemGatedJob(minQuantity: 2, itemSuccessChancePerSurplusPoint: 0.5);

        var chance = job.SuccessChanceFor(
            effectiveAttack: 0, effectiveDefense: 0, maxSuccessChance: 0.95,
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 100 });

        Assert.Equal(0.95, chance, precision: 10);
    }
}
