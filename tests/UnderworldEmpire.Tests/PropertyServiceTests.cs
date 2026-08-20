using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Properties;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PropertyService"/> (T3.3) — the rules of owning income property: who may
/// buy, and what collecting pays into the player's pocket.
/// </summary>
/// <remarks>
/// Mirrors <see cref="InventoryServiceTests"/>. Nothing here is rolled: a property's price and its
/// income are figures the player plans against, so there is no RNG to seed.
/// </remarks>
public class PropertyServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>120 cash/hour (2 a minute) held for at most 30 minutes — a 60-cash cap per unit.</summary>
    private static Property TestProperty(
        string id = "street-corner",
        int levelRequirement = 1,
        long price = 250,
        long incomePerHour = 120,
        int accrualCapMinutes = 30) => new()
        {
            Id = id,
            Name = "Street Corner",
            LevelRequirement = levelRequirement,
            Price = price,
            IncomePerHour = incomePerHour,
            AccrualCapMinutes = accrualCapMinutes
        };

    private static Player TestPlayer(int level = 1, long cash = 1000) => new()
    {
        CreatedAtUtc = T0,
        DeviceToken = "t",
        Level = level,
        Cash = cash
    };

    private static PlayerProperty Holding(Guid playerId, string propertyId = "street-corner") =>
        new() { PlayerId = playerId, PropertyId = propertyId };

    // -------------------------------------------------------------------------
    // Buying
    // -------------------------------------------------------------------------

    [Fact]
    public void Buying_a_property_charges_its_price_and_adds_a_unit()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);

        var result = service.Purchase(player, property, holding, T0);

        Assert.True(result.Succeeded);
        Assert.Equal(250, result.CashSpent);
        Assert.Equal(1, result.QuantityOwned);
        Assert.Equal(750, player.Cash);
        Assert.Equal(1, holding.Quantity);
    }

    [Fact]
    public void A_purchase_starts_the_income_clock_rather_than_paying_for_time_before_it()
    {
        var service = new PropertyService();
        var player = TestPlayer();
        var property = TestProperty();
        var holding = Holding(player.Id);

        service.Purchase(player, property, holding, T0);

        Assert.Equal(0, holding.PendingAt(property, T0));
        Assert.Equal(20, holding.PendingAt(property, T0.AddMinutes(10)));
    }

    [Fact]
    public void Buying_below_the_level_requirement_is_refused()
    {
        var service = new PropertyService();
        var player = TestPlayer(level: 1);

        var result = service.Purchase(player, TestProperty(levelRequirement: 5), Holding(player.Id), T0);

        Assert.Equal(PropertyFailure.LevelTooLow, result.Failure);
    }

    [Fact]
    public void Buying_beyond_your_cash_is_refused()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 249);

        var result = service.Purchase(player, TestProperty(price: 250), Holding(player.Id), T0);

        Assert.Equal(PropertyFailure.InsufficientCash, result.Failure);
    }

    [Fact]
    public void The_level_gate_is_reported_ahead_of_the_price()
    {
        // The permanent obstacle first, same precedence as a job or an item refusal: it tells the player
        // the thing that will not fix itself by earning more.
        var service = new PropertyService();
        var player = TestPlayer(level: 1, cash: 0);

        var result = service.Purchase(
            player, TestProperty(levelRequirement: 5, price: 250), Holding(player.Id), T0);

        Assert.Equal(PropertyFailure.LevelTooLow, result.Failure);
    }

    [Fact]
    public void A_refused_purchase_changes_nothing()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 100);
        var holding = Holding(player.Id);

        service.Purchase(player, TestProperty(price: 250), holding, T0);

        Assert.Equal(100, player.Cash);
        Assert.Equal(0, holding.Quantity);
    }

    [Fact]
    public void A_second_unit_raises_the_quantity_and_charges_again()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);

        service.Purchase(player, property, holding, T0);
        var second = service.Purchase(player, property, holding, T0.AddMinutes(10));

        Assert.Equal(2, second.QuantityOwned);
        Assert.Equal(500, player.Cash);
    }

    [Fact]
    public void Buying_a_second_unit_does_not_forfeit_what_the_first_had_earned()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);

        service.Purchase(player, property, holding, T0);
        service.Purchase(player, property, holding, T0.AddMinutes(10));

        Assert.Equal(20, holding.UncollectedCash);
    }

    // -------------------------------------------------------------------------
    // Read-only gating — the same rules, asked without acting on them
    // -------------------------------------------------------------------------

    [Fact]
    public void An_affordable_in_level_property_reports_no_purchase_obstacle()
    {
        var service = new PropertyService();

        Assert.Equal(
            PropertyFailure.None,
            service.CheckPurchase(TestPlayer(level: 1, cash: 250), TestProperty(price: 250)));
    }

    [Fact]
    public void Checking_a_purchase_reports_what_purchasing_would_refuse()
    {
        var service = new PropertyService();
        var player = TestPlayer(level: 1, cash: 0);
        var property = TestProperty(levelRequirement: 5, price: 250);

        Assert.Equal(PropertyFailure.LevelTooLow, service.CheckPurchase(player, property));
        Assert.Equal(
            PropertyFailure.LevelTooLow,
            service.Purchase(player, property, Holding(player.Id), T0).Failure);
    }

    [Fact]
    public void Checking_a_purchase_does_not_charge_the_player()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);

        service.CheckPurchase(player, TestProperty(price: 250));

        Assert.Equal(1000, player.Cash);
    }

    // -------------------------------------------------------------------------
    // Collecting
    // -------------------------------------------------------------------------

    [Fact]
    public void Collecting_pays_the_accrued_income_into_the_players_cash()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        var result = service.Collect(player, property, holding, T0.AddMinutes(10));

        Assert.True(result.Succeeded);
        Assert.Equal(20, result.CashCollected);
        Assert.Equal(770, player.Cash);
    }

    [Fact]
    public void Collecting_reports_what_is_still_pending_and_what_the_holding_can_hold()
    {
        // The client draws a "next collection" bar from these two, so the answer has to come back with
        // the payout rather than from a second call that would already be a moment out of date.
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        var result = service.Collect(player, property, holding, T0.AddMinutes(10));

        Assert.Equal(0, result.PendingAfter);
        Assert.Equal(60, result.Cap);
    }

    [Fact]
    public void Collecting_a_property_you_do_not_own_is_refused()
    {
        var service = new PropertyService();
        var player = TestPlayer();

        var result = service.Collect(player, TestProperty(), Holding(player.Id), T0.AddHours(1));

        Assert.Equal(PropertyFailure.NotOwned, result.Failure);
    }

    [Fact]
    public void Collecting_when_nothing_has_accrued_yet_is_refused()
    {
        // Not an error, but not a payout either: answering "you collected 0" would have the client draw
        // a reward panel over nothing.
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        var result = service.Collect(player, property, holding, T0.AddSeconds(10));

        Assert.Equal(PropertyFailure.NothingToCollect, result.Failure);
    }

    [Fact]
    public void A_refused_collection_leaves_the_unearned_fraction_on_the_clock()
    {
        // The refusal must not settle the holding, or a player tapping collect early would repeatedly
        // reset their own progress.
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        for (int second = 1; second <= 29; second++)
            service.Collect(player, property, holding, T0.AddSeconds(second));

        Assert.Equal(1, holding.PendingAt(property, T0.AddSeconds(30)));
    }

    [Fact]
    public void A_refused_collection_pays_nothing()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        service.Collect(player, property, holding, T0.AddSeconds(10));

        Assert.Equal(750, player.Cash);
    }

    [Fact]
    public void Collecting_a_capped_holding_pays_the_cap_and_no_more()
    {
        var service = new PropertyService();
        var player = TestPlayer(cash: 1000);
        var property = TestProperty(price: 250);
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        var result = service.Collect(player, property, holding, T0.AddDays(2));

        Assert.Equal(60, result.CashCollected);
    }

    // -------------------------------------------------------------------------
    // CheckCollect — the read-only question GET /properties asks (T3.4)
    // -------------------------------------------------------------------------

    [Fact]
    public void CheckCollect_reports_an_unowned_property_as_not_owned()
    {
        var service = new PropertyService();
        var property = TestProperty();
        var holding = Holding(Guid.NewGuid());

        Assert.Equal(PropertyFailure.NotOwned, service.CheckCollect(property, holding, T0));
    }

    [Fact]
    public void CheckCollect_reports_a_holding_that_has_not_earned_a_whole_coin()
    {
        var service = new PropertyService();
        var player = TestPlayer();
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        // 2 cash a minute: ten seconds is a third of a coin.
        Assert.Equal(
            PropertyFailure.NothingToCollect,
            service.CheckCollect(property, holding, T0.AddSeconds(10)));
    }

    [Fact]
    public void CheckCollect_reports_no_obstacle_once_a_whole_coin_has_accrued()
    {
        var service = new PropertyService();
        var player = TestPlayer();
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        Assert.Equal(
            PropertyFailure.None,
            service.CheckCollect(property, holding, T0.AddMinutes(1)));
    }

    [Fact]
    public void CheckCollect_does_not_settle_the_holding_it_is_asked_about()
    {
        var service = new PropertyService();
        var player = TestPlayer();
        var property = TestProperty();
        var holding = Holding(player.Id);
        service.Purchase(player, property, holding, T0);

        // Asking 600 times must cost the player nothing. If CheckCollect settled, each call would
        // reset the clock and discard the part-earned coin — the very bug PropertyIncome exists to
        // avoid, reintroduced at the read path that the listing calls on every poll.
        for (int i = 1; i <= 600; i++)
            service.CheckCollect(property, holding, T0.AddSeconds(i));

        var result = service.Collect(player, property, holding, T0.AddSeconds(600));

        Assert.Equal(20, result.CashCollected);
    }

    [Fact]
    public void Collect_refuses_on_exactly_what_CheckCollect_reports()
    {
        var service = new PropertyService();
        var player = TestPlayer();
        var property = TestProperty();
        var holding = Holding(player.Id);
        var at = T0.AddSeconds(10);

        // The whole point of extracting the check: a client told it may collect is never then
        // refused, and a client told why is given the same token the write path would return.
        service.Purchase(player, property, holding, T0);

        Assert.Equal(service.CheckCollect(property, holding, at),
            service.Collect(player, property, holding, at).Failure);
    }
}
