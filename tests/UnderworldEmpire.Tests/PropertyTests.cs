using UnderworldEmpire.Domain.Properties;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="Property"/> (T3.3) — the content-supplied property definition and the
/// invariants the accrual maths relies on. Mirrors <see cref="ItemTests"/>: the content loader is the
/// primary validation path, but <see cref="Property.EnsureValid"/> protects hand-built definitions.
/// </summary>
public class PropertyTests
{
    private static Property TestProperty(
        string id = "street-corner",
        string name = "Street Corner",
        int levelRequirement = 1,
        long price = 250,
        long incomePerHour = 120,
        int accrualCapMinutes = 30) => new()
        {
            Id = id,
            Name = name,
            LevelRequirement = levelRequirement,
            Price = price,
            IncomePerHour = incomePerHour,
            AccrualCapMinutes = accrualCapMinutes
        };

    [Fact]
    public void A_valid_property_passes_validation()
    {
        TestProperty().EnsureValid();
    }

    [Fact]
    public void A_blank_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TestProperty(id: "  ").EnsureValid());
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TestProperty(name: " ").EnsureValid());
    }

    [Fact]
    public void A_level_requirement_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestProperty(levelRequirement: 0).EnsureValid());
    }

    [Fact]
    public void A_negative_price_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestProperty(price: -1).EnsureValid());
    }

    [Fact]
    public void A_property_that_earns_nothing_is_rejected()
    {
        // A property is bought for its income. One that pays zero is a content mistake, not a design
        // choice — it would sit in the store charging cash for nothing.
        Assert.Throws<ArgumentOutOfRangeException>(() => TestProperty(incomePerHour: 0).EnsureValid());
    }

    [Fact]
    public void A_non_positive_accrual_cap_is_rejected()
    {
        // A zero-minute window would cap every holding at nothing, so the property could never pay out.
        Assert.Throws<ArgumentOutOfRangeException>(() => TestProperty(accrualCapMinutes: 0).EnsureValid());
    }

    [Fact]
    public void A_window_too_short_to_hold_a_single_coin_is_rejected()
    {
        // Cap cash is (perHour * capMinutes) / 60, floored. 30/hour over 1 minute floors to zero, so the
        // property would accept the purchase and then never pay anything back.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestProperty(incomePerHour: 30, accrualCapMinutes: 1).EnsureValid());
    }

    [Fact]
    public void The_shortest_window_that_holds_one_coin_is_accepted()
    {
        TestProperty(incomePerHour: 60, accrualCapMinutes: 1).EnsureValid();
    }

    [Fact]
    public void A_window_longer_than_a_month_is_rejected()
    {
        // Past a month the "come back before it fills" mechanic has stopped being a mechanic, and an
        // unbounded window is what lets the rate-times-window product below wrap.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestProperty(accrualCapMinutes: 43_201).EnsureValid());
    }

    [Fact]
    public void A_rate_beyond_any_plausible_balance_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestProperty(incomePerHour: 1_000_001).EnsureValid());
    }

    [Fact]
    public void A_rate_and_window_whose_product_wraps_a_long_are_rejected()
    {
        // 2^62 an hour over 9 minutes multiplies out past 2^64 and wraps back to a positive number, so
        // the "floors to zero" guard does not catch it — the property would load with an absurd cap, and
        // enough units of it would wrap the cap negative and pay a negative collection.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TestProperty(incomePerHour: 4_611_686_018_427_387_904L, accrualCapMinutes: 9).EnsureValid());
    }

    [Fact]
    public void The_largest_permitted_rate_and_window_are_accepted()
    {
        TestProperty(incomePerHour: 1_000_000, accrualCapMinutes: 43_200).EnsureValid();
    }
}
