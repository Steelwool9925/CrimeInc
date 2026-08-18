using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Jobs;

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

    private static Job TestJob(
        string id = "shake-down",
        int energyCost = 1,
        int levelRequirement = 1,
        long cashMin = 15,
        long cashMax = 30,
        long xp = 10) => new()
        {
            Id = id,
            Name = "Shake Down",
            EnergyCost = energyCost,
            LevelRequirement = levelRequirement,
            CashMin = cashMin,
            CashMax = cashMax,
            Xp = xp
        };

    private static Player TestPlayer(int level = 1, int energy = 10, long cash = 100, long xp = 0)
    {
        var player = new Player
        {
            CreatedAtUtc = Now,
            Level = level,
            Cash = cash,
            Xp = xp
        };
        // Store the desired energy explicitly at Now so no regeneration is in flight.
        player.Energy = new Meter(PlayerDefaults.EnergyMax, energy, Now);
        return player;
    }

    private static PlayerJobMastery TestMastery(Guid playerId, string jobId = "shake-down") =>
        new() { PlayerId = playerId, JobId = jobId };

    private static JobAttemptResult Attempt(
        JobService service, Player player, Job job, PlayerJobMastery mastery) =>
        service.Attempt(player, job, mastery, PerRank, MaxRank, Now);

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

        Assert.False(result.Succeeded);
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

        Assert.False(result.Succeeded);
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

        Assert.True(result.Succeeded);
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
    public void Exactly_one_roll_is_taken_per_attempt()
    {
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var service = new JobService(random);

        Attempt(service, player, TestJob(), TestMastery(player.Id));

        Assert.Equal(1, random.RollCount);
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

        service.Attempt(player, TestJob(energyCost: 1), TestMastery(player.Id), PerRank, MaxRank, fourMinutesLater);

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

        Assert.True(result.Succeeded);
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
            service.Attempt(null!, TestJob(), TestMastery(player.Id), PerRank, MaxRank, Now));
        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(player, null!, TestMastery(player.Id), PerRank, MaxRank, Now));
        Assert.Throws<ArgumentNullException>(() =>
            service.Attempt(player, TestJob(), null!, PerRank, MaxRank, Now));
    }
}
