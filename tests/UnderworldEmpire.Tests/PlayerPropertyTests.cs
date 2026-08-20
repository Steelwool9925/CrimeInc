using UnderworldEmpire.Domain.Properties;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PlayerProperty"/> (T3.3) — one player's holding of one property: how many
/// units they own, what it has banked, and what collecting pays out.
/// </summary>
/// <remarks>
/// The accrual arithmetic itself is proved in <see cref="PropertyIncomeTests"/>. These tests are about
/// the state machine on top of it: settling before the rate changes, collecting, and refusing a
/// definition that belongs to some other property.
/// </remarks>
public class PlayerPropertyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>120 cash/hour (2 a minute) held for at most 30 minutes — a 60-cash cap per unit.</summary>
    private static Property TestProperty(
        string id = "street-corner",
        long incomePerHour = 120,
        int accrualCapMinutes = 30) => new()
        {
            Id = id,
            Name = "Street Corner",
            LevelRequirement = 1,
            Price = 250,
            IncomePerHour = incomePerHour,
            AccrualCapMinutes = accrualCapMinutes
        };

    private static PlayerProperty Holding(string propertyId = "street-corner") =>
        new() { PlayerId = Guid.NewGuid(), PropertyId = propertyId };

    // -------------------------------------------------------------------------
    // A fresh holding
    // -------------------------------------------------------------------------

    [Fact]
    public void A_fresh_holding_owns_nothing()
    {
        var holding = Holding();

        Assert.Equal(0, holding.Quantity);
        Assert.False(holding.IsOwned);
        Assert.Equal(0, holding.UncollectedCash);
    }

    [Fact]
    public void A_fresh_holding_has_nothing_pending_however_long_the_row_has_existed()
    {
        // A row created empty carries a default timestamp far in the past. Owning nothing must earn
        // nothing regardless, or the first purchase would arrive pre-loaded with cash.
        var holding = Holding();

        Assert.Equal(0, holding.PendingAt(TestProperty(), T0));
    }

    // -------------------------------------------------------------------------
    // Buying units
    // -------------------------------------------------------------------------

    [Fact]
    public void Buying_a_unit_starts_the_clock()
    {
        var holding = Holding();

        holding.AddUnits(TestProperty(), 1, T0);

        Assert.Equal(1, holding.Quantity);
        Assert.True(holding.IsOwned);
        Assert.Equal(0, holding.PendingAt(TestProperty(), T0));
        Assert.Equal(20, holding.PendingAt(TestProperty(), T0.AddMinutes(10)));
    }

    [Fact]
    public void Buying_a_second_unit_banks_what_the_first_had_already_earned()
    {
        // The rate changes at the moment of purchase, so the ten minutes before it must be paid at the
        // old rate. Settling first is what stops the new rate being applied retroactively.
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        holding.AddUnits(TestProperty(), 1, T0.AddMinutes(10));

        Assert.Equal(20, holding.UncollectedCash);
    }

    [Fact]
    public void Income_after_a_second_purchase_accrues_at_the_higher_rate()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);
        holding.AddUnits(TestProperty(), 1, T0.AddMinutes(10));

        // 20 banked, then ten minutes at 4 a minute.
        Assert.Equal(60, holding.PendingAt(TestProperty(), T0.AddMinutes(20)));
    }

    [Fact]
    public void Buying_fewer_than_one_unit_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Holding().AddUnits(TestProperty(), 0, T0));
    }

    [Fact]
    public void A_definition_for_a_different_property_is_refused()
    {
        // Accruing against the wrong definition would silently pay the wrong rate. Cheap to check, and
        // the alternative is a balance bug that never throws.
        var holding = Holding("street-corner");

        Assert.Throws<ArgumentException>(() => holding.AddUnits(TestProperty("casino"), 1, T0));
        Assert.Throws<ArgumentException>(() => holding.PendingAt(TestProperty("casino"), T0));
        Assert.Throws<ArgumentException>(() => holding.CollectAt(TestProperty("casino"), T0));
    }

    // -------------------------------------------------------------------------
    // Reading is not settling
    // -------------------------------------------------------------------------

    [Fact]
    public void Asking_what_is_pending_banks_nothing()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        holding.PendingAt(TestProperty(), T0.AddMinutes(10));

        Assert.Equal(0, holding.UncollectedCash);
    }

    // -------------------------------------------------------------------------
    // Collecting
    // -------------------------------------------------------------------------

    [Fact]
    public void Collecting_pays_out_everything_pending()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        Assert.Equal(20, holding.CollectAt(TestProperty(), T0.AddMinutes(10)));
    }

    [Fact]
    public void Collecting_empties_the_holding()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);
        holding.CollectAt(TestProperty(), T0.AddMinutes(10));

        Assert.Equal(0, holding.UncollectedCash);
        Assert.Equal(0, holding.PendingAt(TestProperty(), T0.AddMinutes(10)));
    }

    [Fact]
    public void Income_starts_accruing_again_the_moment_it_is_collected()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);
        holding.CollectAt(TestProperty(), T0.AddMinutes(10));

        Assert.Equal(20, holding.PendingAt(TestProperty(), T0.AddMinutes(20)));
    }

    [Fact]
    public void Collecting_keeps_the_units_owned()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 3, T0);

        holding.CollectAt(TestProperty(), T0.AddMinutes(10));

        Assert.Equal(3, holding.Quantity);
    }

    [Fact]
    public void Collecting_a_capped_holding_pays_the_cap()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        Assert.Equal(60, holding.CollectAt(TestProperty(), T0.AddHours(6)));
    }

    [Fact]
    public void Collecting_nothing_pays_nothing()
    {
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        Assert.Equal(0, holding.CollectAt(TestProperty(), T0));
    }

    [Fact]
    public void Repeated_collecting_does_not_shave_the_unearned_fraction()
    {
        // A player mashing the collect button must end up with exactly what one patient collect would
        // have paid. Resetting the clock on every tap would earn them nothing at all.
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        long total = 0;
        for (int second = 1; second <= 600; second++)
            total += holding.CollectAt(TestProperty(), T0.AddSeconds(second));

        Assert.Equal(20, total);
    }

    [Fact]
    public void Buying_another_unit_of_a_full_holding_gives_it_room_again()
    {
        // The cap scales with the units owned, so expanding a property that had stopped earning starts
        // it earning again — without forfeiting the full window it was already sitting on.
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        holding.AddUnits(TestProperty(), 1, T0.AddHours(6));

        Assert.Equal(60, holding.UncollectedCash);
        Assert.Equal(120, holding.CapAt(TestProperty()));

        // Two units earn 4 a minute, on top of the full window it was already holding.
        Assert.Equal(68, holding.PendingAt(TestProperty(), T0.AddHours(6).AddMinutes(2)));
    }

    [Fact]
    public void A_holding_that_sat_full_for_days_does_not_bank_the_waiting_as_a_head_start()
    {
        // Reaching the cap discards the time spent full. Collecting then leaves the holding empty and
        // earning from now, not part-way through the next window.
        var holding = Holding();
        holding.AddUnits(TestProperty(), 1, T0);

        holding.CollectAt(TestProperty(), T0.AddDays(3));

        Assert.Equal(0, holding.PendingAt(TestProperty(), T0.AddDays(3)));
        Assert.Equal(2, holding.PendingAt(TestProperty(), T0.AddDays(3).AddMinutes(1)));
    }
}
