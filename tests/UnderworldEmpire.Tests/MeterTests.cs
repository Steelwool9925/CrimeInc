using UnderworldEmpire.Domain;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for the <see cref="Meter"/> value object (T1.1 / S1.1).
/// All time values are fixed — DateTimeOffset.UtcNow is never called here.
/// </summary>
public class MeterTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    // -------------------------------------------------------------------------
    // Construction & guard rails
    // -------------------------------------------------------------------------

    [Fact]
    public void Ctor_valid_values_succeeds()
    {
        var m = new Meter(10, 5, T0);
        Assert.Equal(10, m.Max);
        Assert.Equal(5, m.StoredValue);
        Assert.Equal(T0, m.StoredAtUtc);
    }

    [Fact]
    public void Ctor_negative_max_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Meter(-1, 0, T0));
    }

    [Fact]
    public void Ctor_stored_value_below_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Meter(10, -1, T0));
    }

    [Fact]
    public void Ctor_stored_value_above_max_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Meter(10, 11, T0));
    }

    [Fact]
    public void Full_factory_sets_stored_to_max()
    {
        var m = Meter.Full(20, T0);
        Assert.Equal(20, m.Max);
        Assert.Equal(20, m.StoredValue);
        Assert.Equal(T0, m.StoredAtUtc);
    }

    // -------------------------------------------------------------------------
    // CurrentAt — regen accrues exactly 1 point per regenPerPoint elapsed
    // -------------------------------------------------------------------------

    [Fact]
    public void CurrentAt_exactly_one_period_accrues_one_point()
    {
        var m = new Meter(10, 3, T0);
        int current = m.CurrentAt(T0 + OneMinute, OneMinute);
        Assert.Equal(4, current);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(15, 10)] // capped at Max=10; stored=0, +15 periods but max is 10
    public void CurrentAt_n_periods_accrues_n_points(int periods, int expected)
    {
        // Max=10, stored=0 so we have headroom for 10 points
        var m = new Meter(10, 0, T0);
        int current = m.CurrentAt(T0 + TimeSpan.FromMinutes(periods), OneMinute);
        Assert.Equal(expected, current);
    }

    [Fact]
    public void CurrentAt_partial_period_accrues_zero_floor_semantics()
    {
        var m = new Meter(10, 3, T0);
        // 90 seconds elapsed with 2-minute regen period — half a period => 0 points
        int current = m.CurrentAt(T0 + TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(2));
        Assert.Equal(3, current);
    }

    [Fact]
    public void CurrentAt_caps_at_max_never_exceeds()
    {
        var m = new Meter(5, 4, T0);
        // 10 periods elapsed but only 1 point of headroom
        int current = m.CurrentAt(T0 + TimeSpan.FromMinutes(10), OneMinute);
        Assert.Equal(5, current);
    }

    [Fact]
    public void CurrentAt_at_max_more_time_still_returns_max()
    {
        var m = new Meter(5, 5, T0);
        int current = m.CurrentAt(T0 + TimeSpan.FromHours(1), OneMinute);
        Assert.Equal(5, current);
    }

    [Fact]
    public void CurrentAt_zero_elapsed_returns_stored_value()
    {
        var m = new Meter(10, 3, T0);
        int current = m.CurrentAt(T0, OneMinute);
        Assert.Equal(3, current);
    }

    [Fact]
    public void CurrentAt_negative_elapsed_returns_stored_value()
    {
        var m = new Meter(10, 3, T0);
        // nowUtc is before StoredAtUtc
        int current = m.CurrentAt(T0 - TimeSpan.FromSeconds(1), OneMinute);
        Assert.Equal(3, current);
    }

    [Fact]
    public void CurrentAt_regen_per_point_zero_no_regen()
    {
        var m = new Meter(10, 3, T0);
        int current = m.CurrentAt(T0 + TimeSpan.FromHours(1), TimeSpan.Zero);
        Assert.Equal(3, current);
    }

    [Fact]
    public void CurrentAt_regen_per_point_negative_no_regen()
    {
        var m = new Meter(10, 3, T0);
        int current = m.CurrentAt(T0 + TimeSpan.FromHours(1), TimeSpan.FromMinutes(-1));
        Assert.Equal(3, current);
    }

    // -------------------------------------------------------------------------
    // SpendAt
    // -------------------------------------------------------------------------

    [Fact]
    public void SpendAt_reduces_current_and_resets_clock()
    {
        var m = new Meter(10, 6, T0);
        // After 3 minutes, current = 9; spend 4 -> new stored = 5 at T0+3min
        DateTimeOffset now = T0 + TimeSpan.FromMinutes(3);
        Meter spent = m.SpendAt(4, now, OneMinute);

        Assert.Equal(5, spent.StoredValue);
        Assert.Equal(now, spent.StoredAtUtc);
        Assert.Equal(10, spent.Max);
    }

    [Fact]
    public void SpendAt_exactly_current_leaves_zero()
    {
        var m = new Meter(10, 5, T0);
        DateTimeOffset now = T0 + TimeSpan.FromMinutes(2);
        // current = 7; spend 7 -> 0
        Meter spent = m.SpendAt(7, now, OneMinute);
        Assert.Equal(0, spent.StoredValue);
    }

    [Fact]
    public void SpendAt_more_than_current_throws_invalid_operation()
    {
        var m = new Meter(10, 5, T0);
        DateTimeOffset now = T0 + TimeSpan.FromMinutes(2);
        // current = 7; attempt to spend 8
        Assert.Throws<InvalidOperationException>(() => m.SpendAt(8, now, OneMinute));
    }

    [Fact]
    public void SpendAt_subsequent_regen_starts_from_reduced_value_at_now()
    {
        var m = new Meter(10, 6, T0);
        DateTimeOffset now = T0 + TimeSpan.FromMinutes(3); // current = 9
        Meter spent = m.SpendAt(4, now, OneMinute);        // stored = 5 at now

        // 2 more minutes -> 5 + 2 = 7
        int laterCurrent = spent.CurrentAt(now + TimeSpan.FromMinutes(2), OneMinute);
        Assert.Equal(7, laterCurrent);
    }

    [Fact]
    public void SpendAt_negative_amount_throws()
    {
        var m = new Meter(10, 5, T0);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SpendAt(-1, T0, OneMinute));
    }

    // -------------------------------------------------------------------------
    // GrantAt
    // -------------------------------------------------------------------------

    [Fact]
    public void GrantAt_adds_points_up_to_max()
    {
        var m = new Meter(10, 3, T0);
        Meter granted = m.GrantAt(4, T0, OneMinute);
        Assert.Equal(7, granted.StoredValue);
    }

    [Fact]
    public void GrantAt_caps_at_max()
    {
        var m = new Meter(10, 8, T0);
        Meter granted = m.GrantAt(5, T0, OneMinute);
        Assert.Equal(10, granted.StoredValue);
    }

    [Fact]
    public void GrantAt_negative_amount_throws()
    {
        var m = new Meter(10, 5, T0);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GrantAt(-1, T0, OneMinute));
    }

    // -------------------------------------------------------------------------
    // RefillAt
    // -------------------------------------------------------------------------

    [Fact]
    public void RefillAt_sets_stored_to_max_at_given_time()
    {
        var m = new Meter(10, 2, T0);
        DateTimeOffset refillTime = T0 + TimeSpan.FromHours(1);
        Meter refilled = m.RefillAt(refillTime);

        Assert.Equal(10, refilled.StoredValue);
        Assert.Equal(refillTime, refilled.StoredAtUtc);
        Assert.Equal(10, refilled.Max);
    }

    // -------------------------------------------------------------------------
    // MeterRates defaults (sanity check)
    // -------------------------------------------------------------------------

    [Fact]
    public void MeterRates_energy_is_ninety_seconds_per_point()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), MeterRates.Energy);
    }

    [Fact]
    public void MeterRates_stamina_is_five_minutes_per_point()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MeterRates.Stamina);
    }

    [Fact]
    public void MeterRates_health_is_one_minute_per_point()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), MeterRates.Health);
    }

    // -------------------------------------------------------------------------
    // PlayerDefaults
    // -------------------------------------------------------------------------

    [Fact]
    public void PlayerDefaults_energy_max_is_20()
    {
        // Raised 10 -> 20 so one opening bar of the cheapest job reaches level 2; at 10 only 1% of
        // new players did. FirstSessionTests pins that property — this is just the value.
        Assert.Equal(20, PlayerDefaults.EnergyMax);
    }

    [Fact]
    public void Energy_max_is_a_buffer_and_does_not_change_the_regen_rate()
    {
        // The distinction that matters, and the one an earlier balance analysis got backwards:
        // CurrentAt is min(Max, stored + elapsed / regenPerPoint), so MeterRates.Energy alone sets
        // throughput and EnergyMax only bounds what can bank between visits.
        //
        // Two meters with the same rate but different ceilings therefore accumulate at an identical
        // rate while both are below their cap, and diverge only once the smaller one saturates.
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var small = new Meter(10, 0, start);
        var large = new Meter(20, 0, start);

        // 7 points' worth of regeneration: below both ceilings, so the ceiling is irrelevant.
        var belowBoth = start + 7 * MeterRates.Energy;
        Assert.Equal(7, small.CurrentAt(belowBoth, MeterRates.Energy));
        Assert.Equal(7, large.CurrentAt(belowBoth, MeterRates.Energy));

        // 15 points' worth: the smaller meter has saturated, the larger has not. Same rate, and
        // the only difference is how much each was able to hold.
        var pastSmall = start + 15 * MeterRates.Energy;
        Assert.Equal(10, small.CurrentAt(pastSmall, MeterRates.Energy));
        Assert.Equal(15, large.CurrentAt(pastSmall, MeterRates.Energy));

        // And a grant cannot exceed the ceiling either — the reason a reward "worth a full extra
        // bar" is not expressible today. See the daily-login-streak plan.
        Assert.Equal(20, large.GrantAt(999, pastSmall, MeterRates.Energy)
            .CurrentAt(pastSmall, MeterRates.Energy));
    }

    [Fact]
    public void PlayerDefaults_stamina_max_is_5()
    {
        Assert.Equal(5, PlayerDefaults.StaminaMax);
    }

    [Fact]
    public void PlayerDefaults_health_max_is_100()
    {
        Assert.Equal(100, PlayerDefaults.HealthMax);
    }

    // -------------------------------------------------------------------------
    // Player integration: meters are present and start full
    // -------------------------------------------------------------------------

    [Fact]
    public void New_player_energy_is_full()
    {
        var player = new Player { CreatedAtUtc = T0 };
        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.StoredValue);
        Assert.Equal(PlayerDefaults.EnergyMax, player.Energy.Max);
    }

    [Fact]
    public void New_player_stamina_is_full()
    {
        var player = new Player { CreatedAtUtc = T0 };
        Assert.Equal(PlayerDefaults.StaminaMax, player.Stamina.StoredValue);
    }

    [Fact]
    public void New_player_health_is_full()
    {
        var player = new Player { CreatedAtUtc = T0 };
        Assert.Equal(PlayerDefaults.HealthMax, player.Health.StoredValue);
    }
}
