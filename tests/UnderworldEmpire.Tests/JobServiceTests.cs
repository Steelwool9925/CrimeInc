using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Progression;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="JobService"/> (T2.1 — the rules of performing a job). Pure domain: no
/// host, no database, and a scripted RNG so every payout figure is exact.
/// </summary>
public class JobServiceTests
{
    private const int PerRank = 10;
    private const int MaxRank = 5;

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The pack-wide risk rules these tests run under. Consolation fractions match the shipped crime
    /// pack; the ceiling does not.
    /// </summary>
    /// <remarks>
    /// <see cref="MaxSuccessChance"/> is 1.0 rather than the pack's 0.95 so that a job left at the
    /// permissive default of certain stays certain here. Most of this file is about what an attempt
    /// costs and pays, not about odds, and under a 0.95 ceiling <c>AlwaysMax</c> would roll 10000
    /// against 9500 and fail every one of them. The tests that are about odds set their own chance
    /// and script their own rolls.
    /// </remarks>
    private static readonly JobRiskRules Rules = new(
        MaxSuccessChance: 1.0,
        ConsolationCashFraction: 0.01,
        ConsolationXpFraction: 0.10);

    private static Job TestJob(
        string id = "shake-down",
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
        double extraItemSuccessChance = 0.0,
        int healthLossMin = 0,
        int healthLossMax = 0) => new()
        {
            Id = id,
            Name = "Shake Down",
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
            ExtraItemSuccessChance = extraItemSuccessChance,
            HealthLossMin = healthLossMin,
            HealthLossMax = healthLossMax
        };

    /// <summary>
    /// A job that needs <paramref name="minQuantity"/> switchblades (all spent) and lets a player add up
    /// to <paramref name="maxExtras"/> more, each worth <paramref name="extraItemSuccessChance"/>.
    /// </summary>
    private static Job ItemGatedJob(
        int minQuantity = 2, int maxExtras = 0, double extraItemSuccessChance = 0.0) =>
        TestJob(
            baseSuccessChance: 0.8,
            itemRequirements: [new ItemRequirement("switchblade", minQuantity, maxExtras)],
            extraItemSuccessChance: extraItemSuccessChance);

    private static Dictionary<string, int> Held(int switchblades) => new() { ["switchblade"] = switchblades };

    private static Dictionary<string, int> Extras(int switchblades) => new() { ["switchblade"] = switchblades };

    // `energy` defaults to a full bar, like `health` beside it — the 32 bare TestPlayer() callers
    // mean "a healthy player, energy is not what this test is about", and every test that does care
    // passes an explicit value. It was previously a literal 10, which silently stopped meaning
    // "full" when PlayerDefaults.EnergyMax moved to 20 and broke the two gate tests that assert
    // energy is untouched.
    private static Player TestPlayer(
        int level = 1, int energy = PlayerDefaults.EnergyMax, long cash = 100, long xp = 0,
        int health = PlayerDefaults.HealthMax)
    {
        var player = new Player
        {
            CreatedAtUtc = Now,
            Level = level,
            Cash = cash,
            Xp = xp
        };
        // Store the desired meters explicitly at Now so no regeneration is in flight.
        player.Energy = new Meter(PlayerDefaults.EnergyMax, energy, Now);
        player.Health = new Meter(PlayerDefaults.HealthMax, health, Now);
        return player;
    }

    private static PlayerJobMastery TestMastery(Guid playerId, string jobId = "shake-down") =>
        new() { PlayerId = playerId, JobId = jobId };

    /// <summary>The player's own stats with nothing equipped — base 5/5.</summary>
    private static Loadout Bare(Player player) => Loadout.For(player, []);

    private static JobAttemptResult Attempt(
        JobService service,
        Player player,
        Job job,
        PlayerJobMastery mastery,
        Loadout? loadout = null,
        IReadOnlyDictionary<string, int>? heldItemQuantities = null,
        IReadOnlyDictionary<string, int>? extraItems = null) =>
        service.Attempt(
            player, job, mastery, loadout ?? Bare(player), PerRank, MaxRank, Rules, Now,
            heldItemQuantities, extraItems);

    // -------------------------------------------------------------------------
    // Refusals — nothing may be mutated
    // -------------------------------------------------------------------------

    [Fact]
    public void Attempt_below_the_level_requirement_is_refused()
    {
        var player = TestPlayer(level: 1);
        var job = TestJob(levelRequirement: 5);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        var result = Attempt(service, player, job, TestMastery(player.Id));

        Assert.False(result.WasApplied);
        Assert.Equal(JobFailure.LevelTooLow, result.Failure);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void Attempt_without_enough_energy_is_refused()
    {
        var player = TestPlayer(energy: 1);
        var job = TestJob(energyCost: 4);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        var result = Attempt(service, player, job, TestMastery(player.Id));

        Assert.False(result.WasApplied);
        Assert.Equal(JobFailure.InsufficientEnergy, result.Failure);
    }

    [Fact]
    public void A_refused_attempt_mutates_nothing()
    {
        // The whole point of validating before applying: a refusal must not half-charge a player.
        var player = TestPlayer(energy: 1, cash: 100, xp: 0);
        var job = TestJob(energyCost: 4);
        var mastery = TestMastery(player.Id);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        Attempt(service, player, job, mastery);

        Assert.Equal(1, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(100, player.Cash);
        Assert.Equal(0, player.Xp);
        Assert.Equal(0, mastery.Completions);
    }

    [Fact]
    public void A_refused_attempt_takes_no_roll()
    {
        // If the RNG were consumed on a refusal, a player could burn through a scripted sequence (or
        // in production, shift the roll stream) by spamming unaffordable jobs.
        var random = FakeRandomSource.AlwaysMax();
        var player = TestPlayer(energy: 0);
        var service = new JobService(random);

        Attempt(service, player, TestJob(energyCost: 1), TestMastery(player.Id));

        Assert.Equal(0, random.RollCount);
    }

    [Fact]
    public void Level_is_checked_before_energy()
    {
        // A job failing both checks reports the permanent reason, matching GET /jobs.
        var player = TestPlayer(level: 1, energy: 0);
        var job = TestJob(levelRequirement: 5, energyCost: 4);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        var result = Attempt(service, player, job, TestMastery(player.Id));

        Assert.Equal(JobFailure.LevelTooLow, result.Failure);
    }

    // -------------------------------------------------------------------------
    // Success — costs and payouts
    // -------------------------------------------------------------------------

    [Fact]
    public void A_successful_attempt_deducts_exactly_the_energy_cost()
    {
        var player = TestPlayer(energy: 10);
        var job = TestJob(energyCost: 4, xp: 0);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, job, TestMastery(player.Id));

        Assert.True(result.WasApplied);
        Assert.Equal(6, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(4, result.Outcome!.EnergySpent);
    }

    [Fact]
    public void Cash_payout_can_hit_the_low_end_of_the_range()
    {
        var player = TestPlayer(cash: 100);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(cashMin: 15, cashMax: 30), TestMastery(player.Id));

        Assert.Equal(15, result.Outcome!.CashEarned);
        Assert.Equal(115, player.Cash);
    }

    [Fact]
    public void Cash_payout_can_hit_the_high_end_of_the_range()
    {
        // Proves the inclusive upper bound. An exclusive roll would make 30 unreachable and no
        // range-based assertion would ever notice.
        var player = TestPlayer(cash: 100);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        var result = Attempt(service, player, TestJob(cashMin: 15, cashMax: 30), TestMastery(player.Id));

        Assert.Equal(30, result.Outcome!.CashEarned);
        Assert.Equal(130, player.Cash);
    }

    [Fact]
    public void The_roll_uses_the_jobs_own_range()
    {
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, TestJob(cashMin: 700, cashMax: 1300), TestMastery(player.Id));

        Assert.Equal((700, 1300), random.LastRange);
    }

    [Fact]
    public void A_successful_attempt_takes_exactly_two_rolls()
    {
        // Since M3R every attempt rolls for success before it rolls the cash, so a success is two
        // rolls and a failure is three. The order is part of the contract — FakeRandomSource
        // sequences are scripted against it.
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, TestJob(), TestMastery(player.Id));

        Assert.Equal(2, random.RollCount);
    }

    [Fact]
    public void Xp_is_fixed_not_rolled()
    {
        var player = TestPlayer(xp: 0);
        var service = new JobService(FakeRandomSource.AlwaysMax());

        var result = Attempt(service, player, TestJob(xp: 22), TestMastery(player.Id));

        Assert.Equal(22, result.Outcome!.XpEarned);
        Assert.Equal(22, player.Xp);
    }

    [Fact]
    public void Spending_energy_forfeits_progress_toward_the_next_point()
    {
        // SpendAt resets the regen clock. Whatever partial progress had built up toward the next
        // point is deliberately lost — documented Meter behaviour, pinned here because the job
        // loop is where a player would actually notice it. Both deltas below are 60s, under the
        // 90s-per-point rate, so neither alone ticks a point over — but 60s + 60s = 120s *would*
        // if the clock did not reset on spend, which is exactly what this test pins.
        var player = TestPlayer(energy: 5);
        var sixtySecondsLater = Now.AddSeconds(60);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        service.Attempt(player, TestJob(energyCost: 1), TestMastery(player.Id), Bare(player),
            PerRank, MaxRank, Rules, sixtySecondsLater);

        // 5 - 1 = 4, and the clock restarts, so a further 60 seconds does not tip a point over.
        Assert.Equal(4, player.Energy.CurrentAt(sixtySecondsLater.AddSeconds(60), MeterRates.Energy));
    }

    [Fact]
    public void An_attempt_that_spends_the_last_energy_succeeds()
    {
        // Boundary: cost exactly equals current energy.
        var player = TestPlayer(energy: 4);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(energyCost: 4), TestMastery(player.Id));

        Assert.True(result.WasApplied);
        Assert.Equal(0, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    // -------------------------------------------------------------------------
    // Mastery
    // -------------------------------------------------------------------------

    [Fact]
    public void A_successful_attempt_records_one_completion()
    {
        var player = TestPlayer();
        var mastery = TestMastery(player.Id);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(), mastery);

        Assert.Equal(1, mastery.Completions);
        Assert.Equal(1, result.Outcome!.Completions);
        Assert.False(result.Outcome.MasteryRankedUp);
    }

    [Fact]
    public void Crossing_the_mastery_threshold_is_reported()
    {
        var player = TestPlayer(energy: 10);
        var mastery = TestMastery(player.Id);
        var service = new JobService(FakeRandomSource.AlwaysMin());
        var job = TestJob(energyCost: 1, xp: 0);

        JobAttemptResult? last = null;
        for (int i = 0; i < PerRank; i++)
            last = Attempt(service, player, job, mastery);

        Assert.True(last!.Outcome!.MasteryRankedUp);
        Assert.Equal(1, last.Outcome.MasteryRank);
        Assert.Equal(PerRank, last.Outcome.Completions);
    }

    [Fact]
    public void Outcome_reports_completions_until_the_next_rank()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(), TestMastery(player.Id));

        Assert.Equal(PerRank - 1, result.Outcome!.CompletionsUntilNextRank);
    }

    // -------------------------------------------------------------------------
    // Levelling, applied inside the attempt
    // -------------------------------------------------------------------------

    [Fact]
    public void An_attempt_that_crosses_no_threshold_reports_no_level_up()
    {
        var player = TestPlayer(xp: 0);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(xp: 10), TestMastery(player.Id));

        Assert.NotNull(result.LevelUp);
        Assert.False(result.LevelUp!.LeveledUp);
        Assert.Equal(1, player.Level);
    }

    [Fact]
    public void An_attempt_that_crosses_a_threshold_levels_the_player_and_refills_meters()
    {
        // 90 XP + a 10 XP job = 100, exactly the level-2 threshold. This is the CP2 moment.
        var player = TestPlayer(energy: 3, xp: 90);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(energyCost: 1, xp: 10), TestMastery(player.Id));

        Assert.True(result.LevelUp!.LeveledUp);
        Assert.Equal(2, result.LevelUp.NewLevel);
        Assert.Equal(2, player.Level);
        Assert.Equal(PlayerDefaults.SkillPointsPerLevel, player.SkillPoints);

        // The refill happens after the spend, so energy ends full rather than at 2.
        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.True(result.LevelUp.MetersRefilled);
    }

    [Fact]
    public void Argument_null_checks_reject_missing_collaborators()
    {
        var service = new JobService(FakeRandomSource.AlwaysMin());
        var player = TestPlayer();

        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(null!, TestJob(), TestMastery(player.Id), Bare(player), PerRank, MaxRank, Rules, Now));
        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(player, null!, TestMastery(player.Id), Bare(player), PerRank, MaxRank, Rules, Now));
        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(player, TestJob(), null!, Bare(player), PerRank, MaxRank, Rules, Now));
        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(player, TestJob(), TestMastery(player.Id), null!, PerRank, MaxRank, Rules, Now));
    }

    // -------------------------------------------------------------------------
    // The stat gate (T3R.4) — a refusal, so nothing may be mutated
    // -------------------------------------------------------------------------

    /// <summary>
    /// A job the fresh 1/1 player cannot reach: 5 attack required.
    /// </summary>
    /// <remarks>
    /// Five, not more, so that Brass Knuckles (+4) takes a base-1 player to exactly the requirement.
    /// That is the shape of the real unlock in the shipped pack, and what makes
    /// <see cref="The_gate_reads_equipped_gear_not_just_base_stats"/> mean something.
    /// </remarks>
    private static Job GatedJob(int attack = 5, int defense = 0) =>
        TestJob(attackRequirement: attack, defenseRequirement: defense, baseSuccessChance: 0.8);

    [Fact]
    public void Attempt_below_the_attack_requirement_is_refused()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, GatedJob(), TestMastery(player.Id));

        Assert.Equal(JobFailure.StatsTooLow, result.Failure);
        Assert.False(result.WasApplied);
    }

    [Fact]
    public void Attempt_below_the_defense_requirement_is_refused()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, GatedJob(attack: 0, defense: 9), TestMastery(player.Id));

        Assert.Equal(JobFailure.StatsTooLow, result.Failure);
    }

    [Fact]
    public void The_stat_gate_spends_nothing_and_rolls_nothing()
    {
        // A gate, not a penalty — exactly like the level gate. Spamming a job you cannot reach must
        // cost neither energy nor a position in the roll stream.
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, GatedJob(), TestMastery(player.Id));

        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(100, player.Cash);
        Assert.Equal(0, random.RollCount);
    }

    [Fact]
    public void Stats_exactly_at_the_requirement_are_admitted()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player,
            GatedJob(attack: PlayerDefaults.BaseAttack, defense: PlayerDefaults.BaseDefense),
            TestMastery(player.Id));

        Assert.Equal(JobFailure.None, result.Failure);
    }

    [Fact]
    public void The_gate_reads_equipped_gear_not_just_base_stats()
    {
        // The whole reason M3R follows M3: a job out of reach on base stats becomes reachable once
        // something is equipped. This is the CP3R demo in miniature.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());
        var knuckles = new Item
        {
            Id = "brass-knuckles",
            Name = "Brass Knuckles",
            Slot = ItemSlot.Weapon,
            LevelRequirement = 1,
            Price = 75,
            Attack = 4,
            Defense = 0
        };

        var barehanded = Attempt(service, TestPlayer(), GatedJob(), TestMastery(player.Id));
        var armed = Attempt(service, player, GatedJob(), TestMastery(player.Id),
            Loadout.For(player, [knuckles]));

        Assert.Equal(JobFailure.StatsTooLow, barehanded.Failure);
        Assert.Equal(JobFailure.None, armed.Failure);
    }

    [Fact]
    public void The_level_gate_is_reported_before_the_stat_gate()
    {
        // Precedence is level -> stats -> energy, most permanent reason first. A player failing all
        // three should be told the one that will take longest to fix.
        var player = TestPlayer(level: 1, energy: 0);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player,
            TestJob(levelRequirement: 5, attackRequirement: 99, energyCost: 1),
            TestMastery(player.Id));

        Assert.Equal(JobFailure.LevelTooLow, result.Failure);
    }

    [Fact]
    public void The_stat_gate_is_reported_before_the_energy_gate()
    {
        var player = TestPlayer(energy: 0);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, GatedJob(), TestMastery(player.Id));

        Assert.Equal(JobFailure.StatsTooLow, result.Failure);
    }

    // -------------------------------------------------------------------------
    // The item gate — sits between stats and energy, a refusal so nothing may be mutated
    // -------------------------------------------------------------------------

    [Fact]
    public void Attempt_below_the_item_minimum_is_refused()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 1 });

        Assert.Equal(JobFailure.InsufficientItems, result.Failure);
        Assert.False(result.WasApplied);
    }

    [Fact]
    public void Attempt_with_no_held_items_at_all_is_refused_by_the_item_gate()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        // No heldItemQuantities argument at all, mirroring a caller with nothing to report.
        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id));

        Assert.Equal(JobFailure.InsufficientItems, result.Failure);
    }

    [Fact]
    public void Holding_exactly_the_item_minimum_is_admitted()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 2 });

        Assert.Equal(JobFailure.None, result.Failure);
    }

    [Fact]
    public void The_item_gate_spends_nothing_and_rolls_nothing()
    {
        // A gate, not a penalty — exactly like the stat gate. Spamming a job you lack the stock for
        // must cost neither energy nor a position in the roll stream.
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 1 });

        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(100, player.Cash);
        Assert.Equal(0, random.RollCount);
    }

    [Fact]
    public void The_stat_gate_is_reported_before_the_item_gate()
    {
        // Precedence is level -> stats -> items -> energy: a player failing both is told the stat
        // reason, matching GET /jobs.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());
        var job = TestJob(
            attackRequirement: 99,
            itemRequirements: [new ItemRequirement("switchblade", 2)]);

        var result = Attempt(service, player, job, TestMastery(player.Id));

        Assert.Equal(JobFailure.StatsTooLow, result.Failure);
    }

    [Fact]
    public void The_item_gate_is_reported_before_the_energy_gate()
    {
        var player = TestPlayer(energy: 0);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 1 });

        Assert.Equal(JobFailure.InsufficientItems, result.Failure);
    }

    [Fact]
    public void A_job_with_no_item_requirements_ignores_held_items_entirely()
    {
        // No ItemRequirements at all: the gate must admit a player even with an empty holding, and
        // behave exactly as every job did before this field existed.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int>());

        Assert.Equal(JobFailure.None, result.Failure);
    }

    // -------------------------------------------------------------------------
    // Item consumption — deterministic, applied on both success and failure
    // -------------------------------------------------------------------------

    [Fact]
    public void A_successful_attempt_uses_up_the_whole_required_count()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: Held(5));

        var consumed = Assert.Single(result.Outcome!.ItemsConsumed);
        Assert.Equal("switchblade", consumed.ItemId);
        Assert.Equal(2, consumed.QuantityConsumed);
        Assert.Equal(3, consumed.QuantityRemaining);
    }

    [Fact]
    public void A_failed_attempt_still_uses_up_the_whole_required_count()
    {
        // True one-time-use: the items are spent on the attempt, not the outcome.
        var player = TestPlayer();
        var service = new JobService(ScriptedFailure());

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMax: 5,
                itemRequirements: [new ItemRequirement("switchblade", 2)]),
            TestMastery(player.Id),
            heldItemQuantities: Held(5));

        Assert.False(result.Outcome!.Succeeded);
        var consumed = Assert.Single(result.Outcome.ItemsConsumed);
        Assert.Equal(2, consumed.QuantityConsumed);
        Assert.Equal(3, consumed.QuantityRemaining);
    }

    [Fact]
    public void Committed_extras_are_spent_on_top_of_the_requirement()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(5), extraItems: Extras(2));

        // One required plus two chosen is three spent, leaving two.
        var consumed = Assert.Single(result.Outcome!.ItemsConsumed);
        Assert.Equal(3, consumed.QuantityConsumed);
        Assert.Equal(2, consumed.QuantityRemaining);
    }

    [Fact]
    public void Extras_are_spent_on_a_failure_too()
    {
        var player = TestPlayer();
        var service = new JobService(ScriptedFailure());

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMax: 5,
                itemRequirements: [new ItemRequirement("switchblade", 1, MaxExtras: 3)],
                extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(5), extraItems: Extras(3));

        Assert.False(result.Outcome!.Succeeded);
        Assert.Equal(4, Assert.Single(result.Outcome.ItemsConsumed).QuantityConsumed);
    }

    [Fact]
    public void Nothing_beyond_the_requirement_is_spent_unless_the_player_commits_it()
    {
        // Stock the player merely owns is never touched.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(10));

        Assert.Equal(1, Assert.Single(result.Outcome!.ItemsConsumed).QuantityConsumed);
    }

    [Fact]
    public void Extras_the_player_cannot_cover_are_refused_as_a_shortage_and_spend_nothing()
    {
        // One required plus two extras is three; holding two is not enough, even though two covers the
        // requirement on its own.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());
        long energyBefore = player.Energy.CurrentAt(Now, MeterRates.Energy);

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(2), extraItems: Extras(2));

        Assert.Equal(JobFailure.InsufficientItems, result.Failure);
        Assert.Null(result.Outcome);
        Assert.Equal(energyBefore, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    [Theory]
    [InlineData("switchblade", 4)]      // above the content's allowance of 3
    [InlineData("switchblade", -1)]     // negative
    [InlineData("brass-knuckles", 1)]   // an item the job does not use at all
    public void Extras_the_job_does_not_accept_are_refused_as_malformed_and_spend_nothing(string itemId, int count)
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());
        long energyBefore = player.Energy.CurrentAt(Now, MeterRates.Energy);

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 10, ["brass-knuckles"] = 10 },
            extraItems: new Dictionary<string, int> { [itemId] = count });

        Assert.Equal(JobFailure.InvalidExtras, result.Failure);
        Assert.Null(result.Outcome);
        Assert.Equal(energyBefore, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    [Fact]
    public void A_malformed_extras_request_is_refused_ahead_of_every_game_state_gate()
    {
        // Wrong whatever the player's level or stock, so a shortage must not be what they are told.
        var player = TestPlayer(level: 1);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player,
            TestJob(levelRequirement: 9, itemRequirements: [new ItemRequirement("switchblade", 1, MaxExtras: 1)],
                extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(0), extraItems: Extras(5));

        Assert.Equal(JobFailure.InvalidExtras, result.Failure);
    }

    [Fact]
    public void A_refused_attempt_consumes_nothing()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, ItemGatedJob(minQuantity: 2), TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int> { ["switchblade"] = 1 });

        Assert.Null(result.Outcome);
    }

    [Fact]
    public void A_job_with_no_item_requirements_reports_no_consumption()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());

        var result = Attempt(service, player, TestJob(), TestMastery(player.Id));

        Assert.Empty(result.Outcome!.ItemsConsumed);
    }

    [Fact]
    public void An_attempt_with_multiple_item_requirements_uses_up_the_required_count_of_each()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.AlwaysMin());
        var job = TestJob(itemRequirements:
        [
            new ItemRequirement("switchblade", 2),
            new ItemRequirement("kevlar-vest", 1)
        ]);

        var result = Attempt(service, player, job, TestMastery(player.Id),
            heldItemQuantities: new Dictionary<string, int>
            {
                ["switchblade"] = 2,
                ["kevlar-vest"] = 3
            });

        Assert.Equal(2, result.Outcome!.ItemsConsumed.Count);
        Assert.Contains(result.Outcome.ItemsConsumed,
            c => c.ItemId == "switchblade" && c.QuantityConsumed == 2 && c.QuantityRemaining == 0);
        Assert.Contains(result.Outcome.ItemsConsumed,
            c => c.ItemId == "kevlar-vest" && c.QuantityConsumed == 1 && c.QuantityRemaining == 2);
    }

    // -------------------------------------------------------------------------
    // Committed extras raise the odds the attempt actually rolls against
    // -------------------------------------------------------------------------

    [Fact]
    public void Committed_extras_raise_the_chance_the_attempt_rolls_against()
    {
        // Base 0.8, +5% per extra, two committed: 0.8 -> 0.9, so a roll of 9000 wins where it would have
        // lost at the base chance.
        var random = FakeRandomSource.Sequence(9000, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(5), extraItems: Extras(2));

        Assert.True(result.Outcome!.Succeeded);
    }

    [Fact]
    public void The_same_roll_loses_without_the_extras()
    {
        // The control for the test above: nothing about the roll changed, only whether extras were
        // committed. Without them the chance is 0.8 and 9000 is a miss.
        var random = FakeRandomSource.Sequence(9000, 20, 1);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(5));

        Assert.False(result.Outcome!.Succeeded);
    }

    [Fact]
    public void Owning_a_large_stack_does_not_raise_the_odds()
    {
        // Odds follow what the player chooses to spend, not what they merely hold.
        var random = FakeRandomSource.Sequence(8100, 20, 1);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(100));

        Assert.False(result.Outcome!.Succeeded);
    }

    [Fact]
    public void Committing_extras_does_not_change_the_roll_order()
    {
        // Still success, then cash, on a success.
        var random = FakeRandomSource.Sequence(1, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player,
            ItemGatedJob(minQuantity: 1, maxExtras: 3, extraItemSuccessChance: 0.05),
            TestMastery(player.Id),
            heldItemQuantities: Held(5), extraItems: Extras(2));

        Assert.Equal(2, random.RollCount);
    }

    // -------------------------------------------------------------------------
    // The success roll
    // -------------------------------------------------------------------------

    [Fact]
    public void The_success_roll_is_taken_in_basis_points()
    {
        var random = FakeRandomSource.Sequence(1, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, TestJob(baseSuccessChance: 0.5), TestMastery(player.Id));

        // Basis points over 1..10000 rather than a NextDouble added to IRandomSource, so the single
        // -method seam holds and FakeRandomSource's modes stay exactly assertable.
        Assert.Equal((1, 10_000), random.Ranges[0]);
    }

    [Fact]
    public void A_roll_at_exactly_the_chance_succeeds()
    {
        // 0.85 is 8500 basis points, and the comparison is <=, so 8500 is the last winning roll.
        var random = FakeRandomSource.Sequence(8500, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player, TestJob(baseSuccessChance: 0.85), TestMastery(player.Id));

        Assert.True(result.Outcome!.Succeeded);
    }

    [Fact]
    public void A_roll_one_past_the_chance_fails()
    {
        var random = FakeRandomSource.Sequence(8501, 20, 0);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player, TestJob(baseSuccessChance: 0.85), TestMastery(player.Id));

        Assert.False(result.Outcome!.Succeeded);
    }

    [Fact]
    public void Surplus_stats_raise_the_chance_the_attempt_rolls_against()
    {
        // An ungated job against the fresh 1/1: two points of surplus at 1% each takes 0.85 to
        // 0.87, so 8700 wins where it would have lost at the base chance.
        var random = FakeRandomSource.Sequence(8700, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player,
            TestJob(attackRequirement: 0, defenseRequirement: 0,
                    baseSuccessChance: 0.85, successChancePerSurplusPoint: 0.01),
            TestMastery(player.Id));

        Assert.True(result.Outcome!.Succeeded);
    }

    // -------------------------------------------------------------------------
    // A failed attempt — applied, not refused
    // -------------------------------------------------------------------------

    /// <summary>Scripts a guaranteed failure: a losing success roll, then cash, then health loss.</summary>
    private static FakeRandomSource ScriptedFailure(long cash = 2000, long healthLoss = 3) =>
        FakeRandomSource.Sequence(10000, cash, healthLoss);

    [Fact]
    public void A_failed_attempt_is_applied_rather_than_refused()
    {
        // It is an outcome, not a refusal: energy was spent and state changed, so the endpoint owes
        // a 200 with a result rather than a 409.
        var player = TestPlayer();
        var service = new JobService(ScriptedFailure());

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMax: 5), TestMastery(player.Id));

        Assert.Equal(JobFailure.None, result.Failure);
        Assert.True(result.WasApplied);
        Assert.NotNull(result.Outcome);
        Assert.False(result.Outcome!.Succeeded);
    }

    [Fact]
    public void A_failed_attempt_spends_the_energy_in_full()
    {
        // That is the risk. A failure that cost nothing would make spamming the hardest job free.
        var player = TestPlayer(energy: 10);
        var service = new JobService(ScriptedFailure());

        Attempt(service, player,
            TestJob(energyCost: 4, baseSuccessChance: 0.5, healthLossMax: 5), TestMastery(player.Id));

        Assert.Equal(6, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    [Fact]
    public void A_failed_attempt_pays_the_consolation_fraction_of_the_rolled_cash()
    {
        // 1% of a rolled 2000, rounded up.
        var player = TestPlayer(cash: 0);
        var service = new JobService(ScriptedFailure(cash: 2000));

        var result = Attempt(service, player,
            TestJob(cashMin: 0, cashMax: 5000, baseSuccessChance: 0.5, healthLossMax: 5),
            TestMastery(player.Id));

        Assert.Equal(20, result.Outcome!.CashEarned);
        Assert.Equal(20, player.Cash);
    }

    [Fact]
    public void The_consolation_cash_rounds_up_so_a_failure_is_never_quite_nothing()
    {
        // 1% of 15 is 0.15. Rounded down that is zero, and the consolation would be a lie on every
        // cheap job in the pack.
        var player = TestPlayer(cash: 0);
        var service = new JobService(ScriptedFailure(cash: 15));

        var result = Attempt(service, player,
            TestJob(cashMin: 15, cashMax: 30, baseSuccessChance: 0.5, healthLossMax: 5),
            TestMastery(player.Id));

        Assert.Equal(1, result.Outcome!.CashEarned);
    }

    [Fact]
    public void A_failed_attempt_grants_the_consolation_fraction_of_the_xp()
    {
        // 10% of 10, rounded up.
        var player = TestPlayer(xp: 0);
        var service = new JobService(ScriptedFailure());

        var result = Attempt(service, player,
            TestJob(xp: 10, baseSuccessChance: 0.5, healthLossMax: 5), TestMastery(player.Id));

        Assert.Equal(1, result.Outcome!.XpEarned);
        Assert.Equal(1, player.Xp);
    }

    [Fact]
    public void A_failed_attempt_does_not_advance_mastery()
    {
        var player = TestPlayer();
        var mastery = TestMastery(player.Id);
        var service = new JobService(ScriptedFailure());

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMax: 5), mastery);

        Assert.Equal(0, mastery.Completions);
        Assert.Equal(0, result.Outcome!.Completions);
        Assert.False(result.Outcome.MasteryRankedUp);
    }

    [Fact]
    public void A_failed_attempt_costs_the_rolled_health()
    {
        var player = TestPlayer(health: 50);
        var service = new JobService(ScriptedFailure(healthLoss: 7));

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMin: 0, healthLossMax: 25),
            TestMastery(player.Id));

        Assert.Equal(7, result.Outcome!.HealthLost);
        Assert.Equal(43, player.Health.CurrentAt(Now, MeterRates.Health));
    }

    [Fact]
    public void The_health_roll_uses_the_jobs_own_range()
    {
        var random = ScriptedFailure(healthLoss: 4);
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMin: 2, healthLossMax: 8), TestMastery(player.Id));

        Assert.Equal((2, 8), random.LastRange);
    }

    [Fact]
    public void Health_loss_is_clamped_to_what_the_player_has_left()
    {
        // Meter.SpendAt throws on an overspend, so a raw roll must never reach it. The floor is 0,
        // not a negative bar and not an exception.
        var player = TestPlayer(health: 3);
        var service = new JobService(ScriptedFailure(healthLoss: 25));

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMin: 0, healthLossMax: 25),
            TestMastery(player.Id));

        Assert.Equal(3, result.Outcome!.HealthLost);
        Assert.Equal(0, player.Health.CurrentAt(Now, MeterRates.Health));
    }

    [Fact]
    public void A_failed_attempt_takes_three_rolls()
    {
        var random = ScriptedFailure();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMax: 5), TestMastery(player.Id));

        Assert.Equal(3, random.RollCount);
    }

    [Fact]
    public void The_cash_range_is_rolled_on_a_failure_too()
    {
        // The consolation is a fraction of what the attempt *would* have earned, so the cash roll
        // happens on both branches. Skipping it on failure would also shift every later roll.
        var random = FakeRandomSource.Sequence(10000, 3000, 0);
        var player = TestPlayer(cash: 0);
        var service = new JobService(random);

        var result = Attempt(service, player,
            TestJob(cashMin: 0, cashMax: 5000, baseSuccessChance: 0.5, healthLossMax: 5),
            TestMastery(player.Id));

        Assert.Equal(30, result.Outcome!.CashEarned);
    }

    [Fact]
    public void Health_loss_is_applied_before_the_xp_grant()
    {
        // A failure still grants consolation XP, which can still cross a level threshold, and a
        // level-up refills every meter. Applied in the other order the health penalty would be
        // silently undone by the refill.
        var player = TestPlayer(xp: LevelCurve.XpToReach(2) - 1, health: 50);
        var service = new JobService(ScriptedFailure(healthLoss: 20));

        var result = Attempt(service, player,
            TestJob(xp: 10, baseSuccessChance: 0.5, healthLossMin: 0, healthLossMax: 25),
            TestMastery(player.Id));

        Assert.True(result.LevelUp!.LeveledUp);
        Assert.True(result.LevelUp.MetersRefilled);

        // The refill is the last word: health ends full, and the loss is still reported honestly.
        Assert.Equal(20, result.Outcome!.HealthLost);
        Assert.Equal(PlayerDefaults.HealthMax, player.Health.CurrentAt(Now, MeterRates.Health));
    }

    [Fact]
    public void A_successful_attempt_loses_no_health_and_takes_no_health_roll()
    {
        var random = FakeRandomSource.Sequence(1, 20);
        var player = TestPlayer(health: 50);
        var service = new JobService(random);

        var result = Attempt(service, player,
            TestJob(baseSuccessChance: 0.5, healthLossMin: 2, healthLossMax: 8), TestMastery(player.Id));

        Assert.True(result.Outcome!.Succeeded);
        Assert.Equal(0, result.Outcome.HealthLost);
        Assert.Equal(50, player.Health.CurrentAt(Now, MeterRates.Health));
        Assert.Equal(2, random.RollCount);
    }

    // -------------------------------------------------------------------------
    // Mastery pays
    // -------------------------------------------------------------------------

    /// <summary>A mastery row that has earned exactly <paramref name="rank"/> ranks.</summary>
    private static PlayerJobMastery MasteryAtRank(Guid playerId, int rank)
    {
        var mastery = TestMastery(playerId);
        for (int i = 0; i < rank * PerRank; i++)
            mastery.RecordCompletion(PerRank, MaxRank);
        Assert.Equal(rank, mastery.Rank);
        return mastery;
    }

    private static JobAttemptResult AttemptWithBonus(
        JobService service, Player player, Job job, PlayerJobMastery mastery, double bonusPerRank) =>
        service.Attempt(
            player, job, mastery, Bare(player), PerRank, MaxRank, Rules, Now,
            masteryPayoutBonusPerRank: bonusPerRank);

    [Fact]
    public void An_unranked_job_pays_the_base_even_when_the_pack_configures_a_bonus()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonus(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), bonusPerRank: 0.10);

        Assert.Equal(20, result.Outcome!.CashEarned);
        Assert.Equal(10, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_max_rank_job_pays_the_per_rank_bonus_times_its_rank_on_cash_and_xp()
    {
        // Rank 5 at 10% per rank is +50%: a rolled 20 cash pays 30 and 10 XP pays 15.
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonus(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), MasteryAtRank(player.Id, MaxRank), bonusPerRank: 0.10);

        Assert.Equal(30, result.Outcome!.CashEarned);
        Assert.Equal(15, result.Outcome.XpEarned);
        Assert.Equal(30, player.Cash - 100);
        Assert.Equal(15, player.Xp);
    }

    [Fact]
    public void The_bonus_scales_with_rank_not_just_with_being_ranked()
    {
        var rank2 = TestPlayer();
        var rank4 = TestPlayer();

        var at2 = AttemptWithBonus(new JobService(FakeRandomSource.Sequence(1, 100)), rank2,
            TestJob(cashMin: 0, cashMax: 200, xp: 10), MasteryAtRank(rank2.Id, 2), bonusPerRank: 0.10);
        var at4 = AttemptWithBonus(new JobService(FakeRandomSource.Sequence(1, 100)), rank4,
            TestJob(cashMin: 0, cashMax: 200, xp: 10), MasteryAtRank(rank4.Id, 4), bonusPerRank: 0.10);

        Assert.Equal(120, at2.Outcome!.CashEarned);
        Assert.Equal(140, at4.Outcome!.CashEarned);
    }

    [Fact]
    public void A_zero_bonus_pays_the_base_at_every_rank()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonus(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), MasteryAtRank(player.Id, MaxRank), bonusPerRank: 0);

        Assert.Equal(20, result.Outcome!.CashEarned);
        Assert.Equal(10, result.Outcome.XpEarned);
    }

    [Fact]
    public void The_rank_that_pays_is_the_one_held_before_the_attempt()
    {
        // The attempt that earns rank 1 is paid at rank 0; the bonus starts with the next one.
        var player = TestPlayer();
        var mastery = TestMastery(player.Id);
        for (int i = 0; i < PerRank - 1; i++)
            mastery.RecordCompletion(PerRank, MaxRank);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonus(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), mastery, bonusPerRank: 0.10);

        Assert.True(result.Outcome!.MasteryRankedUp);
        Assert.Equal(20, result.Outcome.CashEarned);
        Assert.Equal(10, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_failed_attempt_consolation_is_a_fraction_of_the_boosted_amounts()
    {
        // Rolled 2000 cash is boosted to 3000 at rank 5, then 1% consolation is 30 (base would be 20);
        // 10 XP is boosted to 15, then 10% consolation is 1.5 rounded up to 2 (base would be 1).
        var player = TestPlayer(cash: 0);
        var service = new JobService(ScriptedFailure(cash: 2000));

        var result = AttemptWithBonus(service, player,
            TestJob(cashMin: 0, cashMax: 5000, xp: 10, baseSuccessChance: 0.5, healthLossMax: 5),
            MasteryAtRank(player.Id, MaxRank), bonusPerRank: 0.10);

        Assert.False(result.Outcome!.Succeeded);
        Assert.Equal(30, result.Outcome.CashEarned);
        Assert.Equal(2, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_negative_bonus_is_rejected()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AttemptWithBonus(service, player, TestJob(), TestMastery(player.Id), bonusPerRank: -0.1));
    }

    // -------------------------------------------------------------------------
    // Level pays
    // -------------------------------------------------------------------------

    private static JobAttemptResult AttemptWithBonuses(
        JobService service, Player player, Job job, PlayerJobMastery mastery,
        double bonusPerRank, double bonusPerLevel) =>
        service.Attempt(
            player, job, mastery, Bare(player), PerRank, MaxRank, Rules, Now,
            masteryPayoutBonusPerRank: bonusPerRank,
            levelPayoutBonusPerLevel: bonusPerLevel);

    [Fact]
    public void A_level_one_player_earns_no_level_bonus()
    {
        var player = TestPlayer(level: 1);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), 0, bonusPerLevel: 0.02);

        Assert.Equal(20, result.Outcome!.CashEarned);
        Assert.Equal(10, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_higher_level_pays_its_bonus_on_cash_and_xp()
    {
        // Level 11 at 2% per level past the first is +20%: 20 cash pays 24, 10 XP pays 12.
        var player = TestPlayer(level: 11);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), 0, bonusPerLevel: 0.02);

        Assert.Equal(24, result.Outcome!.CashEarned);
        Assert.Equal(12, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_fractional_boosted_payout_rounds_to_nearest_rather_than_down()
    {
        // Level 4 at 2% per level is +6%: 10 XP becomes 10.6, which must pay 11 (flooring would pay 10).
        var player = TestPlayer(level: 4);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), 0, bonusPerLevel: 0.02);

        Assert.Equal(11, result.Outcome!.XpEarned);
        Assert.Equal(21, result.Outcome.CashEarned);
    }

    [Fact]
    public void A_boosted_payout_exactly_halfway_rounds_away_from_zero()
    {
        // Level 2 at 5% per level: 10 XP becomes 10.5 and 30 cash becomes 31.5. Away-from-zero pays 11 and 32;
        // banker's rounding would pay 10 and 32, flooring 10 and 31.
        var player = TestPlayer(level: 2);
        var service = new JobService(FakeRandomSource.Sequence(1, 30));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), 0, bonusPerLevel: 0.05);

        Assert.Equal(11, result.Outcome!.XpEarned);
        Assert.Equal(32, result.Outcome.CashEarned);
    }

    [Fact]
    public void The_level_bonus_adds_to_the_mastery_bonus_rather_than_compounding_it()
    {
        // Rank 5 at 10% is +50% and level 11 at 2% is +20%: x1.7, not x1.5 x 1.2 = x1.8.
        var player = TestPlayer(level: 11);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), MasteryAtRank(player.Id, MaxRank),
            bonusPerRank: 0.10, bonusPerLevel: 0.02);

        Assert.Equal(34, result.Outcome!.CashEarned);
        Assert.Equal(17, result.Outcome.XpEarned);
    }

    [Fact]
    public void The_attempt_that_levels_the_player_up_is_paid_at_the_level_they_held()
    {
        // 95 XP plus a 10 XP job crosses the level-2 threshold (100), but that attempt is paid at level 1.
        var player = TestPlayer(level: 1, xp: 95);
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 100, xp: 10), TestMastery(player.Id), 0, bonusPerLevel: 0.5);

        Assert.True(result.LevelUp!.LeveledUp);
        Assert.Equal(20, result.Outcome!.CashEarned);
        Assert.Equal(10, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_failed_attempt_consolation_is_a_fraction_of_the_level_boosted_amounts()
    {
        // Level 11, +20%: a rolled 2000 becomes 2400, 1% of which is 24; 10 XP becomes 12, 10% of which
        // is 1.2 rounded up to 2 (the base 1.0 would be 1).
        var player = TestPlayer(level: 11, cash: 0);
        var service = new JobService(ScriptedFailure(cash: 2000));

        var result = AttemptWithBonuses(service, player,
            TestJob(cashMin: 0, cashMax: 5000, xp: 10, baseSuccessChance: 0.5, healthLossMax: 5),
            TestMastery(player.Id), 0, bonusPerLevel: 0.02);

        Assert.False(result.Outcome!.Succeeded);
        Assert.Equal(24, result.Outcome.CashEarned);
        Assert.Equal(2, result.Outcome.XpEarned);
    }

    [Fact]
    public void A_negative_level_bonus_is_rejected()
    {
        var player = TestPlayer();
        var service = new JobService(FakeRandomSource.Sequence(1, 20));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AttemptWithBonuses(service, player, TestJob(), TestMastery(player.Id), 0, bonusPerLevel: -0.01));
    }
}
