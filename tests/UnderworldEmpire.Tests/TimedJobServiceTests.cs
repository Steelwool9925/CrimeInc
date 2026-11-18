using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Timed;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="TimedJobService"/> and <see cref="PlayerTimedJob"/> (the overnight
/// timed-jobs M1 rules). Pure domain: no host, no database, and a scripted RNG so the payout
/// figure is exact.
/// </summary>
public class TimedJobServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static TimedJob TestJob(
        string id = "run-a-late-night-shipment",
        int durationSeconds = 28800,
        int energyCost = 3,
        int levelRequirement = 1,
        long cashMin = 180,
        long cashMax = 320,
        long xp = 40) => new()
        {
            Id = id,
            Name = "Run a Late-Night Shipment",
            DurationSeconds = durationSeconds,
            EnergyCost = energyCost,
            LevelRequirement = levelRequirement,
            CashMin = cashMin,
            CashMax = cashMax,
            Xp = xp
        };

    private static Player TestPlayer(int level = 1, int energy = PlayerDefaults.EnergyMax, long cash = 100, long xp = 0)
    {
        var player = new Player
        {
            CreatedAtUtc = Now,
            Level = level,
            Cash = cash,
            Xp = xp
        };
        player.Energy = new Meter(PlayerDefaults.EnergyMax, energy, Now);
        return player;
    }

    // -------------------------------------------------------------------------
    // Start — refusals, nothing may be mutated
    // -------------------------------------------------------------------------

    [Fact]
    public void Start_below_the_level_requirement_is_refused()
    {
        var player = TestPlayer(level: 1);
        var job = TestJob(levelRequirement: 5);
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, job, existing: null, Now);

        Assert.False(result.WasApplied);
        Assert.Equal(TimedJobFailure.LevelTooLow, result.Failure);
        Assert.Null(result.Running);
    }

    [Fact]
    public void Start_without_enough_energy_is_refused()
    {
        var player = TestPlayer(energy: 1);
        var job = TestJob(energyCost: 4);
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, job, existing: null, Now);

        Assert.False(result.WasApplied);
        Assert.Equal(TimedJobFailure.InsufficientEnergy, result.Failure);
    }

    [Fact]
    public void Start_with_a_slot_already_occupied_is_refused_regardless_of_the_other_gates()
    {
        // A player who meets level and energy for the NEW job is still refused: Tier 1 has one slot.
        var player = TestPlayer(level: 99, energy: PlayerDefaults.EnergyMax);
        var existing = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = "other", StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, TestJob(levelRequirement: 1, energyCost: 1), existing, Now);

        Assert.Equal(TimedJobFailure.AlreadyRunning, result.Failure);
    }

    [Fact]
    public void AlreadyRunning_is_checked_before_level_and_energy()
    {
        // A player failing every gate is told the one reason that blocks starting ANYTHING.
        var player = TestPlayer(level: 1, energy: 0);
        var existing = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = "other", StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, TestJob(levelRequirement: 99, energyCost: 99), existing, Now);

        Assert.Equal(TimedJobFailure.AlreadyRunning, result.Failure);
    }

    [Fact]
    public void A_refused_start_mutates_nothing()
    {
        var player = TestPlayer(energy: 1, cash: 100);
        var job = TestJob(energyCost: 4);
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        service.Start(player, job, existing: null, Now);

        Assert.Equal(1, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(100, player.Cash);
    }

    [Fact]
    public void A_refused_start_takes_no_roll()
    {
        var random = FakeRandomSource.AlwaysMax();
        var player = TestPlayer(energy: 0);
        var service = new TimedJobService(random);

        service.Start(player, TestJob(energyCost: 1), existing: null, Now);

        Assert.Equal(0, random.RollCount);
    }

    // -------------------------------------------------------------------------
    // Start — success
    // -------------------------------------------------------------------------

    [Fact]
    public void A_successful_start_deducts_exactly_the_energy_cost_and_rolls_nothing()
    {
        var random = FakeRandomSource.AlwaysMax();
        var player = TestPlayer(energy: 10);
        var job = TestJob(energyCost: 3);
        var service = new TimedJobService(random);

        var result = service.Start(player, job, existing: null, Now);

        Assert.True(result.WasApplied);
        Assert.Equal(7, player.Energy.CurrentAt(Now, MeterRates.Energy));
        Assert.Equal(0, random.RollCount);
    }

    [Fact]
    public void A_successful_start_returns_a_row_stamped_at_the_given_instant()
    {
        var player = TestPlayer();
        var job = TestJob(id: "launder-a-nights-takings");
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, job, existing: null, Now);

        Assert.Equal(player.Id, result.Running!.PlayerId);
        Assert.Equal("launder-a-nights-takings", result.Running.TimedJobId);
        Assert.Equal(Now, result.Running.StartedAtUtc);
    }

    [Fact]
    public void An_attempt_that_spends_the_last_energy_succeeds()
    {
        var player = TestPlayer(energy: 3);
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Start(player, TestJob(energyCost: 3), existing: null, Now);

        Assert.True(result.WasApplied);
        Assert.Equal(0, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    // -------------------------------------------------------------------------
    // PlayerTimedJob — pure, compute-on-read readiness
    // -------------------------------------------------------------------------

    [Fact]
    public void IsReadyAt_is_false_one_second_before_the_duration_elapses()
    {
        var running = new PlayerTimedJob { StartedAtUtc = Now };
        var duration = TimeSpan.FromHours(8);

        Assert.False(running.IsReadyAt(Now + duration - TimeSpan.FromSeconds(1), duration));
    }

    [Fact]
    public void IsReadyAt_is_true_exactly_at_the_duration_boundary()
    {
        var running = new PlayerTimedJob { StartedAtUtc = Now };
        var duration = TimeSpan.FromHours(8);

        Assert.True(running.IsReadyAt(Now + duration, duration));
    }

    [Fact]
    public void RemainingAt_floors_at_zero_once_ready()
    {
        var running = new PlayerTimedJob { StartedAtUtc = Now };
        var duration = TimeSpan.FromHours(8);

        Assert.Equal(0, running.RemainingAt(Now + duration + TimeSpan.FromHours(1), duration));
    }

    [Fact]
    public void RemainingAt_reports_the_whole_duration_at_the_moment_of_starting()
    {
        var running = new PlayerTimedJob { StartedAtUtc = Now };
        var duration = TimeSpan.FromSeconds(28800);

        Assert.Equal(28800, running.RemainingAt(Now, duration));
    }

    // -------------------------------------------------------------------------
    // Collect — refusals, nothing may be mutated
    // -------------------------------------------------------------------------

    [Fact]
    public void Collect_with_nothing_running_is_refused_as_not_ready()
    {
        var player = TestPlayer(cash: 100, xp: 0);
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Collect(player, job: null, running: null, Now);

        Assert.False(result.WasApplied);
        Assert.Equal(TimedJobFailure.NotReady, result.Failure);
        Assert.Equal(100, player.Cash);
        Assert.Equal(0, player.Xp);
    }

    [Fact]
    public void Collect_before_the_duration_has_elapsed_is_refused()
    {
        var player = TestPlayer();
        var job = TestJob(durationSeconds: 28800);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Collect(player, job, running, Now.AddSeconds(28799));

        Assert.Equal(TimedJobFailure.NotReady, result.Failure);
    }

    [Fact]
    public void A_refused_collect_takes_no_roll()
    {
        var random = FakeRandomSource.AlwaysMax();
        var player = TestPlayer();
        var job = TestJob(durationSeconds: 28800);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(random);

        service.Collect(player, job, running, Now);

        Assert.Equal(0, random.RollCount);
    }

    // -------------------------------------------------------------------------
    // Collect — success
    // -------------------------------------------------------------------------

    [Fact]
    public void Collect_exactly_at_the_duration_boundary_succeeds()
    {
        var player = TestPlayer();
        var job = TestJob(durationSeconds: 28800);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Collect(player, job, running, Now.AddSeconds(28800));

        Assert.True(result.WasApplied);
    }

    [Fact]
    public void A_successful_collect_pays_cash_at_the_low_end_of_the_range()
    {
        var player = TestPlayer(cash: 100);
        var job = TestJob(cashMin: 180, cashMax: 320);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMin());

        var result = service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.Equal(180, result.Outcome!.CashEarned);
        Assert.Equal(280, player.Cash);
    }

    [Fact]
    public void A_successful_collect_can_pay_cash_at_the_high_end_of_the_range()
    {
        var player = TestPlayer(cash: 100);
        var job = TestJob(cashMin: 180, cashMax: 320);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.Equal(320, result.Outcome!.CashEarned);
    }

    [Fact]
    public void The_roll_uses_the_appointments_own_range()
    {
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var job = TestJob(cashMin: 650, cashMax: 1100);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(random);

        service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.Equal((650, 1100), random.LastRange);
    }

    [Fact]
    public void A_successful_collect_takes_exactly_one_roll()
    {
        // Tier 1 always succeeds — there is no pass/fail roll, only the cash roll.
        var random = FakeRandomSource.AlwaysMin();
        var player = TestPlayer();
        var job = TestJob();
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(random);

        service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.Equal(1, random.RollCount);
    }

    [Fact]
    public void Xp_is_fixed_not_rolled()
    {
        var player = TestPlayer(xp: 0);
        var job = TestJob(xp: 40);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMax());

        var result = service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.Equal(40, result.Outcome!.XpEarned);
        Assert.Equal(40, player.Xp);
    }

    [Fact]
    public void A_collect_that_crosses_a_level_threshold_levels_the_player_and_refills_meters()
    {
        var player = TestPlayer(energy: 3, xp: 90);
        var job = TestJob(xp: 10);
        var running = new PlayerTimedJob { PlayerId = player.Id, TimedJobId = job.Id, StartedAtUtc = Now };
        var service = new TimedJobService(FakeRandomSource.AlwaysMin());

        var result = service.Collect(player, job, running, Now.AddSeconds(job.DurationSeconds));

        Assert.True(result.LevelUp!.LeveledUp);
        Assert.Equal(2, player.Level);
        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.CurrentAt(Now, MeterRates.Energy));
    }

    [Fact]
    public void Argument_null_checks_reject_a_missing_player()
    {
        var service = new TimedJobService(FakeRandomSource.AlwaysMin());
        var job = TestJob();

        Assert.Throws<ArgumentNullException>(() => service.Start(null!, job, existing: null, Now));
        Assert.Throws<ArgumentNullException>(() => service.Start(TestPlayer(), null!, existing: null, Now));
        Assert.Throws<ArgumentNullException>(() => service.Collect(null!, job, running: null, Now));
    }
}
