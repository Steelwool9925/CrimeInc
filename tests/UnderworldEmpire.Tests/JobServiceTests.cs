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
            HealthLossMin = healthLossMin,
            HealthLossMax = healthLossMax
        };

    private static Player TestPlayer(
        int level = 1, int energy = 10, long cash = 100, long xp = 0, int health = PlayerDefaults.HealthMax)
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
        Loadout? loadout = null) =>
        service.Attempt(
            player, job, mastery, loadout ?? Bare(player), PerRank, MaxRank, Rules, Now);

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
        // SpendAt resets the regen clock. Four minutes into a five-minute point, that partial
        // progress is deliberately lost — documented Meter behaviour, pinned here because the job
        // loop is where a player would actually notice it.
        var player = TestPlayer(energy: 5);
        var fourMinutesLater = Now.AddMinutes(4);
        var service = new JobService(FakeRandomSource.AlwaysMin());

        service.Attempt(player, TestJob(energyCost: 1), TestMastery(player.Id), Bare(player),
            PerRank, MaxRank, Rules, fourMinutesLater);

        // 5 - 1 = 4, and the clock restarts, so a further minute does not tip a point over.
        Assert.Equal(4, player.Energy.CurrentAt(fourMinutesLater.AddMinutes(1), MeterRates.Energy));
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

    /// <summary>A job the fresh 5/5 player cannot reach: 9 attack required.</summary>
    private static Job GatedJob(int attack = 9, int defense = 0) =>
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
        // Requirement 3/2 against the fresh 5/5: five points of surplus at 1% each takes 0.85 to
        // 0.90, so 9000 wins where it would have lost at the base chance.
        var random = FakeRandomSource.Sequence(9000, 20);
        var player = TestPlayer();
        var service = new JobService(random);

        var result = Attempt(service, player,
            TestJob(attackRequirement: 3, defenseRequirement: 2,
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
}
