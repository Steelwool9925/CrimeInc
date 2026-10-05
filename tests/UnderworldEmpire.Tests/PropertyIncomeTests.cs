using UnderworldEmpire.Domain.Properties;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PropertyIncome"/> (T3.3) — the pure lazy-accrual maths behind property
/// income: how much a holding has earned at an instant, where the cap stops it, and how much of the
/// elapsed time that payout actually consumed.
/// </summary>
/// <remarks>
/// A pure function with an injected instant, so every case below is exact rather than timing-dependent —
/// the same approach <see cref="MeterTests"/> takes to lazy regen. No clock, no database, no waiting.
/// </remarks>
public class PropertyIncomeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>120 cash/hour (2 a minute) held for at most 30 minutes — a 60-cash cap per unit.</summary>
    private static Property TestProperty(
        long incomePerHour = 120,
        int accrualCapMinutes = 30) => new()
        {
            Id = "street-corner",
            Name = "Street Corner",
            LevelRequirement = 1,
            Price = 250,
            IncomePerHour = incomePerHour,
            AccrualCapMinutes = accrualCapMinutes
        };

    // -------------------------------------------------------------------------
    // The cap
    // -------------------------------------------------------------------------

    [Fact]
    public void One_unit_holds_a_full_window_of_income()
    {
        Assert.Equal(60, PropertyIncome.CapFor(TestProperty(), quantity: 1));
    }

    [Fact]
    public void The_cap_scales_with_the_number_of_units_owned()
    {
        // Both the rate and the ceiling scale, so a second unit does not shorten the window before
        // income stops — it doubles what the window holds.
        Assert.Equal(180, PropertyIncome.CapFor(TestProperty(), quantity: 3));
    }

    [Fact]
    public void Owning_none_holds_nothing()
    {
        Assert.Equal(0, PropertyIncome.CapFor(TestProperty(), quantity: 0));
    }

    // -------------------------------------------------------------------------
    // Accrual
    // -------------------------------------------------------------------------

    [Fact]
    public void Income_accrues_in_proportion_to_elapsed_time()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddMinutes(10));

        Assert.Equal(20, accrual.Uncollected);
    }

    [Fact]
    public void Income_accrues_at_the_rate_of_every_unit_owned()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 4, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddMinutes(10));

        Assert.Equal(80, accrual.Uncollected);
    }

    [Fact]
    public void A_fraction_of_a_coin_is_not_yet_earned()
    {
        // 2 cash a minute: 20 seconds has produced two thirds of a coin, which is not a coin.
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddSeconds(20));

        Assert.Equal(0, accrual.Uncollected);
    }

    [Fact]
    public void Unearned_fractions_of_a_coin_are_kept_rather_than_discarded()
    {
        // The clock only advances by the time the payout actually consumed, so the 20 seconds above are
        // still on the meter. Reading again 10 seconds later completes the coin.
        var first = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddSeconds(20));

        var second = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, first.Uncollected, first.AccruedAtUtc, T0.AddSeconds(30));

        Assert.Equal(1, second.Uncollected);
    }

    [Fact]
    public void Settling_repeatedly_earns_exactly_what_settling_once_would_have()
    {
        // The anti-collect-spam property: a client polling every second must not shave a fraction off
        // every read and end up behind a client that polled once.
        var property = TestProperty();
        long uncollected = 0;
        var accruedAt = T0;

        for (int second = 1; second <= 600; second++)
        {
            var step = PropertyIncome.Accrue(property, 1, uncollected, accruedAt, T0.AddSeconds(second));
            uncollected = step.Uncollected;
            accruedAt = step.AccruedAtUtc;
        }

        var inOneGo = PropertyIncome.Accrue(property, 1, 0, T0, T0.AddSeconds(600));

        Assert.Equal(inOneGo.Uncollected, uncollected);
        Assert.Equal(20, uncollected);
    }

    [Fact]
    public void Accrual_adds_to_what_was_already_banked()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 15, accruedAtUtc: T0, nowUtc: T0.AddMinutes(10));

        Assert.Equal(35, accrual.Uncollected);
    }

    // -------------------------------------------------------------------------
    // The cap stops accrual
    // -------------------------------------------------------------------------

    [Fact]
    public void Income_stops_at_the_cap()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddMinutes(45));

        Assert.Equal(60, accrual.Uncollected);
    }

    [Fact]
    public void A_property_left_for_a_week_holds_no_more_than_one_left_for_its_window()
    {
        var forAWeek = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddDays(7));

        var forItsWindow = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddMinutes(30));

        Assert.Equal(forItsWindow.Uncollected, forAWeek.Uncollected);
    }

    [Fact]
    public void Reaching_the_cap_discards_the_time_spent_full()
    {
        // Nothing is owed for the fifteen minutes past the cap, so the clock restarts at now rather than
        // banking a head start toward the next window.
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddMinutes(45));

        Assert.Equal(T0.AddMinutes(45), accrual.AccruedAtUtc);
    }

    [Fact]
    public void A_unit_bought_part_way_through_a_window_fills_the_larger_cap_before_that_window_elapses()
    {
        // The overnight-sized window (8h at 240/h = 1,920 per unit) with a second unit bought at the
        // four-hour mark, 960 already banked. The bigger holding earns 480/h against a 3,840 cap, so
        // the remaining 2,880 arrives in six hours: inside the 8h window, which means the cap is reached
        // by the earned-reaches-room branch and not the elapsed-exceeds-window short-circuit.
        var overnight = TestProperty(incomePerHour: 240, accrualCapMinutes: 480);

        var justShort = PropertyIncome.Accrue(
            overnight, quantity: 2, uncollected: 960, accruedAtUtc: T0, nowUtc: T0.AddMinutes(359));
        Assert.Equal(960 + 359 * 8, justShort.Uncollected);
        Assert.Equal(T0.AddMinutes(359), justShort.AccruedAtUtc);

        var exactly = PropertyIncome.Accrue(
            overnight, quantity: 2, uncollected: 960, accruedAtUtc: T0, nowUtc: T0.AddHours(6));
        Assert.Equal(3_840, exactly.Uncollected);
        Assert.Equal(T0.AddHours(6), exactly.AccruedAtUtc);

        // Seven hours is still short of the 8h window, so only this branch can have capped it, and the
        // hour spent full is discarded as usual.
        var pastIt = PropertyIncome.Accrue(
            overnight, quantity: 2, uncollected: 960, accruedAtUtc: T0, nowUtc: T0.AddHours(7));
        Assert.Equal(3_840, pastIt.Uncollected);
        Assert.Equal(T0.AddHours(7), pastIt.AccruedAtUtc);
    }

    [Fact]
    public void A_holding_already_at_its_cap_earns_nothing_more()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 60, accruedAtUtc: T0, nowUtc: T0.AddHours(3));

        Assert.Equal(60, accrual.Uncollected);
    }

    // -------------------------------------------------------------------------
    // Degenerate inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void An_unowned_property_earns_nothing_however_long_it_sits()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 0, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddDays(30));

        Assert.Equal(0, accrual.Uncollected);
    }

    [Fact]
    public void No_elapsed_time_earns_nothing()
    {
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 7, accruedAtUtc: T0, nowUtc: T0);

        Assert.Equal(7, accrual.Uncollected);
        Assert.Equal(T0, accrual.AccruedAtUtc);
    }

    [Fact]
    public void A_clock_that_reads_backwards_never_takes_income_away()
    {
        // Server clock adjustments happen. Losing banked cash to one would be a bug the player pays for.
        var accrual = PropertyIncome.Accrue(
            TestProperty(), quantity: 1, uncollected: 7, accruedAtUtc: T0, nowUtc: T0.AddMinutes(-5));

        Assert.Equal(7, accrual.Uncollected);
        Assert.Equal(T0, accrual.AccruedAtUtc);
    }

    [Fact]
    public void A_very_large_holding_over_a_long_window_does_not_overflow()
    {
        // Rate is per-unit income times units owned, multiplied by elapsed ticks. Naive long arithmetic
        // wraps here and hands the player a negative — or absurd — balance.
        var property = TestProperty(incomePerHour: 1_000_000, accrualCapMinutes: 1440);

        var accrual = PropertyIncome.Accrue(
            property, quantity: 1_000_000, uncollected: 0, accruedAtUtc: T0, nowUtc: T0.AddDays(365));

        Assert.Equal(PropertyIncome.CapFor(property, 1_000_000), accrual.Uncollected);
        Assert.True(accrual.Uncollected > 0);
    }
}
